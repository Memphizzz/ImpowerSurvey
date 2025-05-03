using BitzArt.Blazor.Cookies;
using ImpowerSurvey;
using ImpowerSurvey.Components;
using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using ImpowerSurvey.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using Radzen;
using SlackNet.AspNetCore;
using SlackNet.Blocks;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
// Set global EF Core minimum level to Information to suppress debug logs
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Information);
// Only log warnings for database SQL commands
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
// Only show critical connection issues (not transient failures)
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Connection", LogLevel.Critical);
// Filter out transient exception retries in Infrastructure category
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure", LogLevel.Warning);
// Filter out SlackNet trace logs
builder.Logging.AddFilter("SlackNet", LogLevel.Information);

// Create a logger for configuration checking and startup process
var loggerFactory = LoggerFactory.Create(configure => configure.AddConsole());
var configLogger = loggerFactory.CreateLogger<Program>();

configLogger.LogInformation("⏳ SERVICES: Registering ASP.NET Core infrastructure components");
builder.Services.AddRazorComponents()
	   .AddInteractiveServerComponents()
	   .AddHubOptions(options => options.MaximumReceiveMessageSize = 10 * 1024 * 1024);

builder.Services.AddControllers();
// Add named HttpClient for inter-instance communication
builder.Services.AddHttpClient(HttpClientExtensions.InstanceHttpClientName, client =>
	   {
		   // Base configuration - headers will be added per request
		   client.Timeout = TimeSpan.FromSeconds(10);            // Reduced timeout to prevent slow operations
		   client.DefaultRequestHeaders.ConnectionClose = false; // Keep connections alive
	   })
	   .ConfigurePrimaryHttpMessageHandler(() =>
	   {
		   var handler = new SocketsHttpHandler
		   {
			   // Internal network certificates may have validation issues since we're using the internal hostnames,
			   // but we authenticate using instance secrets, so we can accept certificate name mismatches
			   SslOptions = new System.Net.Security.SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, _, _, _) => true, },
			   PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			   PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
			   EnableMultipleHttp2Connections = true,
			   MaxConnectionsPerServer = 20
		   };
		   return handler;
	   });
builder.Services.AddRadzenComponents();
builder.Services.AddScoped<IJSUtilityService, JSUtilityService>();
builder.Services.AddMemoryCache();
builder.Services.AddServerSideBlazor(options =>
{
	options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(5);
	options.MaxBufferedUnacknowledgedRenderBatches = 10;
});
configLogger.LogInformation("✅ SERVICES: ASP.NET Core infrastructure components registered");

string[] names =
[
	Constants.App.EnvInstanceSecret,
	Constants.App.EnvConnectionString,
	Constants.App.EnvHostUrl,
	Constants.App.EnvSlackApiToken,
	Constants.App.EnvSlackAppLevelToken,
	Constants.App.EnvCookieSecret,
	Constants.App.EnvClaudeApiKey,
	Constants.App.EnvClaudeModel
];

configLogger.LogInformation("⏳ STARTUP: Initializing Impower Survey application in {Environment} environment", builder.Environment.EnvironmentName);

configLogger.LogInformation("⏳ VALIDATION: Verifying required environment configuration parameters");
var result = VerifyVariables(builder.Environment.IsProduction(), names, configLogger);
if (!result)
{
	configLogger.LogError("❌ VALIDATION: Configuration validation failed - missing required parameters");
	throw new InvalidOperationException("Invalid Configuration!");
}

configLogger.LogInformation("✅ VALIDATION: Environment configuration parameters verified successfully");

// Register LogService first since it's a dependency for all services
builder.Services.AddSingleton<LogService>();
builder.Services.AddSingleton<ILogService>(sp => sp.GetRequiredService<LogService>());

// Register SettingsService before other services that depend on it
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());

configLogger.LogInformation("⏳ ENV_CONFIG: Initializing environment-specific configuration");
if (builder.Environment.IsProduction())
{
	configLogger.LogInformation("⏳ ENV_CONFIG: Configuring PRODUCTION environment components");
	var connectionString = VerifyConnectionString(Environment.GetEnvironmentVariable(Constants.App.EnvConnectionString, EnvironmentVariableTarget.Process));
	builder.Services.AddDbContextFactory<SurveyDbContext>(x =>
	{
		x.UseNpgsql(connectionString, options =>
		{
			options.CommandTimeout(30);
			// Allow for serverless PostreSQL database
			options.EnableRetryOnFailure(maxRetryCount: 10,
										 maxRetryDelay: TimeSpan.FromSeconds(30),
										 errorCodesToAdd: ["57P03"]);
		});
	});

	builder.Services.Configure<ForwardedHeadersOptions>(options =>
	{
		options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
		options.KnownNetworks.Clear();
		options.KnownProxies.Clear();
	});

	configLogger.LogInformation("⏳ ENV_CONFIG: Initializing SlackNet with production credentials");
	builder.Services.AddSlackNet(x => x.UseApiToken(Environment.GetEnvironmentVariable(Constants.App.EnvSlackApiToken, EnvironmentVariableTarget.Process))
									   .UseAppLevelToken(Environment.GetEnvironmentVariable(Constants.App.EnvSlackAppLevelToken, EnvironmentVariableTarget.Process))
									   //.RegisterEventHandler<MessageEvent, SlackService.SlackMessageEventHandler>()
									   .RegisterBlockActionHandler<BlockAction, SlackService.CompletionCodeHandler>());

	configLogger.LogInformation("⏳ ENV_CONFIG: Initializing ClaudeService with production credentials");
	builder.Services.AddSingleton(sp => new ClaudeService(sp.GetRequiredService<ILogService>(),
														  sp.GetRequiredService<ISettingsService>(),
														  new ClaudeOptions
														  {
															  ApiKey = Environment.GetEnvironmentVariable(Constants.App.EnvClaudeApiKey, EnvironmentVariableTarget.Process),
															  ModelName = Environment.GetEnvironmentVariable(Constants.App.EnvClaudeModel, EnvironmentVariableTarget.Process) ?? "claude-3-7-sonnet-20250219"
														  }));
}
else if (builder.Environment.IsDevelopment())
{
	configLogger.LogInformation("⏳ ENV_CONFIG: Configuring DEVELOPMENT environment components");
	var value = builder.Configuration.GetValue<string>(Constants.App.EnvScaleOut);
	if (value != null)
		Environment.SetEnvironmentVariable(Constants.App.EnvScaleOut, value, EnvironmentVariableTarget.Process);

	var connectionString = VerifyConnectionString(builder.Configuration.GetValue<string>(Constants.App.EnvConnectionString));
	builder.Services.AddDbContextFactory<SurveyDbContext>(x => x.UseNpgsql(connectionString)
																.ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning)));

	configLogger.LogInformation("⏳ ENV_CONFIG: Initializing SlackNet with development credentials");
	builder.Services.AddSlackNet(x => x.UseApiToken(builder.Configuration.GetValue<string>(Constants.App.EnvSlackApiToken))
									   .UseAppLevelToken(builder.Configuration.GetValue<string>(Constants.App.EnvSlackAppLevelToken))
									   //.RegisterEventHandler<MessageEvent, SlackService.SlackMessageEventHandler>()
									   .RegisterBlockActionHandler<BlockAction, SlackService.CompletionCodeHandler>());

	configLogger.LogInformation("⏳ ENV_CONFIG: Initializing ClaudeService with development credentials");

	builder.Services.AddSingleton(sp => new ClaudeService(sp.GetRequiredService<ILogService>(),
														  sp.GetRequiredService<ISettingsService>(),
														  new ClaudeOptions
														  {
															  ApiKey = builder.Configuration.GetValue<string>(Constants.App.EnvClaudeApiKey),
															  ModelName = builder.Configuration.GetValue<string>(Constants.App.EnvClaudeModel) ?? "claude-3-7-sonnet-20250219"
														  }));
}

configLogger.LogInformation("✅ ENV_CONFIG: Environment-specific configuration completed");

configLogger.LogInformation("⏳ APP_SERVICES: Registering DelayedSubmissionService configuration");
builder.Services.Configure<DssConfiguration>(builder.Configuration.GetSection(nameof(DssConfiguration)));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DssConfiguration>>().Value);

configLogger.LogInformation("⏳ APP_SERVICES: Registering core service dependencies");
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<SurveyCodeService>();
builder.Services.AddSingleton<IReportBuilder, ReportBuilder>();

// Register ClaudeService
builder.Services.AddSingleton<IClaudeService>(sp => sp.GetRequiredService<ClaudeService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ClaudeService>());

// Register LeaderElectionService
builder.Services.AddSingleton<LeaderElectionService>();
builder.Services.AddSingleton<ILeaderElectionService>(sp => sp.GetRequiredService<LeaderElectionService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<LeaderElectionService>());

// Register other services that depend on LeaderElectionService
builder.Services.AddSingleton<DelayedSubmissionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DelayedSubmissionService>());
builder.Services.AddHostedService<SurveySchedulerService>();

configLogger.LogInformation("⏳ APP_SERVICES: Registering SlackNet integration services");
builder.Services.AddSingleton<SlackService>();
builder.Services.AddSingleton<ISlackService>(sp => sp.GetRequiredService<SlackService>());

configLogger.LogInformation("⏳ APP_SERVICES: Registering SurveyService with dependencies");
builder.Services.AddSingleton<SurveyService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SlackService>());

configLogger.LogInformation("⏳ APP_SERVICES: Configuring authentication subsystem");
builder.Services.AddAuthenticationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

configLogger.LogInformation("⏳ APP_SERVICES: Registering Radzen UI components");
builder.Services.AddRadzenCookieThemeService(options =>
{
	options.Name = "ImpowerSurveyTheme";
	options.Duration = TimeSpan.FromDays(365);
});

configLogger.LogInformation("⏳ APP_SERVICES: Configuring cookie services");
builder.AddBlazorCookies();
configLogger.LogInformation("✅ APP_SERVICES: Application service registration completed");

configLogger.LogInformation("⏳ BUILD: Building application host instance");
var app = builder.Build();
configLogger.LogInformation("✅ BUILD: Application host instance successfully constructed");

configLogger.LogInformation("⏳ DB_INIT: Creating service scope for database initialization");
using (var scope = app.Services.CreateScope())
{
	var dbContext = scope.ServiceProvider.GetRequiredService<SurveyDbContext>();

	if (app.Environment.IsProduction())
	{
		configLogger.LogInformation("⏳ DATABASE: Testing database connection");
		var retries = 10;
		var retryDelay = TimeSpan.FromSeconds(3);
		for (var i = 0; i < retries; i++)
		{
			// Test connection
			var success = await dbContext.Database.CanConnectAsync();
			if (success)
			{
				app.Logger.LogInformation("✅ Successfully connected to database");
				break;
			}

			if (i == retries - 1)
			{
				app.Logger.LogError("❌ Failed to connect to database after {Retries} retries", retries);
				throw new Exception("Connection Failed");
			}

			app.Logger.LogWarning("⏳ Waiting for serverless database to spin up. Attempt {Attempt}/{MaxRetries}, retrying in {Delay}s...", i + 1, retries, retryDelay.TotalSeconds);
			await Task.Delay(retryDelay);
			// Exponential backoff
			retryDelay = TimeSpan.FromSeconds(Math.Min(30, retryDelay.TotalSeconds * 1.5));
		}
	}

	// Apply database migrations
	configLogger.LogInformation("⏳ DB_INIT: Executing database migrations");
	dbContext.Database.Migrate();
	configLogger.LogInformation("✅ DB_INIT: Database schema updated to latest version");

	// Initialize default admin user if none exist
	if (!dbContext.Users.Any())
	{
		configLogger.LogInformation("⏳ DB_INIT: No users found - creating default administrator account");
		dbContext.Users.Add(new User
		{
			Username = "ImpowerAdmin",
			DisplayName = "Admin",
			PasswordHash = BCrypt.Net.BCrypt.HashPassword("change!me"),
			Role = Roles.Admin,
			RequirePasswordChange = true
		});
		dbContext.SaveChanges();
		configLogger.LogInformation("✅ DB_INIT: Default administrator account created with forced password change");
	}
	else
		configLogger.LogInformation("✅ DB_INIT: Existing users found - skipping default administrator creation");

	// Initialize default settings
	configLogger.LogInformation("⏳ DB_INIT: Initializing application settings");
	var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
	await settingsService.InitializeDefaultSettingsAsync();
	configLogger.LogInformation("✅ DB_INIT: Application settings initialized");
}

// Configure the HTTP request pipeline.
configLogger.LogInformation("⏳ HTTP_PIPELINE: Configuring ASP.NET Core request pipeline");
if (!app.Environment.IsDevelopment())
{
	configLogger.LogInformation("⏳ HTTP_PIPELINE: Configuring production security middleware (HSTS, Error handling)");
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}

configLogger.LogInformation("⏳ HTTP_PIPELINE: Configuring HTTPS redirection middleware");
app.UseHttpsRedirection();

configLogger.LogInformation("⏳ HTTP_PIPELINE: Registering API controllers");
app.MapControllers();

configLogger.LogInformation("⏳ HTTP_PIPELINE: Enabling static file middleware");
app.UseStaticFiles();

configLogger.LogInformation("⏳ HTTP_PIPELINE: Configuring antiforgery protection");
app.UseAntiforgery();

configLogger.LogInformation("⏳ HTTP_PIPELINE: Initializing SlackNet with SocketMode");
app.UseSlackNet(x => x.UseSocketMode(true));

configLogger.LogInformation("⏳ HTTP_PIPELINE: Adding instance info diagnostic endpoint");
app.MapGet("/admin/instance-info", async (HttpContext context) =>
{
	var authStateProvider = context.RequestServices.GetRequiredService<AuthenticationStateProvider>();
	var customAuthStateProvider = CustomAuthStateProvider.Get(authStateProvider);
	var isAuthenticated = await customAuthStateProvider.GetIsAuthenticatedAsync();

	// Check if the user is authenticated and is an Admin
	if (!isAuthenticated || customAuthStateProvider.GetUser()?.Role != Roles.Admin)
	{
		context.Response.StatusCode = 401;
		return Results.Unauthorized();
	}

	var dssService = context.RequestServices.GetRequiredService<DelayedSubmissionService>();
	var dssStatus = dssService.GetStatus();

	try
	{
		return Results.Json(new { MachineName = Environment.MachineName, 
								TimeStamp = DateTime.UtcNow.ToString("o"), 
								DssStatus = dssStatus });
	}
	catch (Exception ex)
	{
		configLogger.LogError("Error in instance-info endpoint: {message}", ex.Message);
		return Results.Problem("Error retrieving instance information");
	}
});

configLogger.LogInformation("⏳ HTTP_PIPELINE: Adding inter-instance communication endpoint");
app.MapPost("/api/internal/responses/transfer", async (HttpContext context, InstanceCommunicationPayload payload) =>
{
	var logService = context.RequestServices.GetRequiredService<ILogService>();
	var leaderElectionService = context.RequestServices.GetRequiredService<ILeaderElectionService>();
	var dssService = context.RequestServices.GetRequiredService<DelayedSubmissionService>();
	var surveyService = context.RequestServices.GetRequiredService<SurveyService>();
	var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

	// Custom endpoint authentication - check instance secret
	if (!context.Request.Headers.TryGetValue(Constants.App.AuthHeaderName, out var authHeader))
	{
		await logService.LogAsync(LogSource.Security, LogLevel.Warning, "Instance auth attempt missing auth header");
		return Results.Unauthorized();
	}

	// Get instance secret from config
	var instanceSecret = configuration[Constants.App.EnvInstanceSecret] ??
						 Environment.GetEnvironmentVariable(Constants.App.EnvInstanceSecret, EnvironmentVariableTarget.Process);

	if (string.IsNullOrEmpty(instanceSecret) || authHeader != instanceSecret)
	{
		await logService.LogAsync(LogSource.Security, LogLevel.Warning, "Instance auth attempt with invalid token");
		return Results.Unauthorized();
	}

	// Verify this instance is the leader
	if (!leaderElectionService.IsLeader)
	{
		await logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Warning,
								  $"Non-leader instance {leaderElectionService.InstanceId} received request from {payload.SourceInstanceId}");
		return Results.Ok(ServiceResult.Failure<int>("This instance is not the leader"));
	}

	// Handle different operation types
	switch (payload.CommunicationType)
	{
		case InstanceCommunicationType.NoOp:
			await logService.LogAsync(LogSource.LeaderElectionService, LogLevel.Information,
								  $"Received NoOp communication test from instance {payload.SourceInstanceId}");
			return Results.Ok(ServiceResult.Success(true, "Communication test successful"));
			
		case InstanceCommunicationType.TransferResponses:
			// Process response transfer
			if (payload.Responses == null || payload.Responses.Count == 0)
				return Results.Ok(ServiceResult.Success(0, "No responses to transfer"));

			// Add responses to the leader's queue
			dssService.QueueResponses(payload.Responses);

			await logService.LogAsync(LogSource.DelayedSubmissionService, LogLevel.Information,
									  $"Received {payload.Responses.Count} responses from instance {payload.SourceInstanceId}");
			return Results.Ok(ServiceResult.Success(payload.Responses.Count, "Responses transferred successfully"));

		case InstanceCommunicationType.CloseSurvey:
			// Process survey close request
			if (!payload.SurveyId.HasValue)
				return Results.Ok(ServiceResult.Failure<bool>("No survey ID provided for close operation"));

			// Call SurveyService to close the survey
			var closeResult = await surveyService.CloseSurvey(payload.SurveyId.Value);

			await logService.LogAsync(LogSource.SurveyService, LogLevel.Information,
									  $"Survey close request for survey ID {payload.SurveyId} from instance {payload.SourceInstanceId}");

			return Results.Ok(closeResult.Successful
								  ? ServiceResult.Success(true, Constants.Survey.CloseSuccess)
								  : ServiceResult.Failure<bool>(closeResult.Message));

		default:
			return Results.Ok(ServiceResult.Failure<int>($"Unknown operation type: {payload.CommunicationType}"));
	}
});

configLogger.LogInformation("⏳ HTTP_PIPELINE: Mapping Razor component endpoints");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

configLogger.LogInformation("✅ HTTP_PIPELINE: Request pipeline configuration completed");

configLogger.LogInformation("✅ APPLICATION: Impower Survey startup sequence completed successfully");

#region Helper Methods
bool VerifyVariables(bool isProduction, string[] names, ILogger logger)
{
	var result = true;
	var configStatus = new Dictionary<string, bool>();

	foreach (var name in names)
	{
		bool value;
		if (isProduction)
			value = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process));
		else
		{
			var secretValue = builder.Configuration.GetValue<string>(name);
			value = !string.IsNullOrWhiteSpace(secretValue);
			
			// If the value exists in user secrets but not in process env vars,
			// set it as a process environment variable so it's accessible everywhere
			if (value && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)))
			{
				Environment.SetEnvironmentVariable(name, secretValue, EnvironmentVariableTarget.Process);
				logger.LogInformation("⏳ SECRETS: Mapped user secret {Name} to process environment variable", name);
			}
		}

		configStatus[name] = value;
		if (!value)
			result = false;
	}

	// Build a consolidated configuration status message
	var statusMessage = string.Join(", ", configStatus.Select(kvp => $"{kvp.Key}:{(kvp.Value ? "✓" : "✗")}"));

	if (result)
		logger.LogInformation("⏳ VALIDATION: Checking configuration parameters - [{Status}]", statusMessage);
	else
		logger.LogWarning("⚠️ VALIDATION: Missing configuration parameters - [{Status}]", statusMessage);

	return result;
}

string VerifyConnectionString(string s)
{
	if (s.StartsWith("postgresql://"))
	{
		var uri = new Uri(s);
		var userInfo = uri.UserInfo.Split(':');

		var connectionStringBuilder = new NpgsqlConnectionStringBuilder
		{
			Host = uri.Host,
			Port = uri.Port,
			Database = uri.AbsolutePath.TrimStart('/'),
			Username = userInfo[0],
			Password = userInfo[1],
			Timeout = 120,
			CommandTimeout = 30,
			CancellationTimeout = 30000
		};

		s = connectionStringBuilder.ConnectionString;
		configLogger.LogInformation("⏳ ENV_CONFIG: Converted PostgreSQL connection string from URI to parameter format");
	}
	else if (s.Contains("Host=") || s.Contains("Server="))
	{
		var connectionStringBuilder = new NpgsqlConnectionStringBuilder(s)
		{
			Timeout = 120,
			CommandTimeout = 30,
			CancellationTimeout = 30000
		};
		s = connectionStringBuilder.ConnectionString;
		configLogger.LogInformation("⏳ ENV_CONFIG: Applied timeout settings to existing PostgreSQL parameter format connection string");
	}
	else
		throw new Exception("Invalid PostreSQL ConnectionString!");

	return s;
}
#endregion

app.Run();
