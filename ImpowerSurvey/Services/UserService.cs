using ImpowerSurvey.Components.Model;
using ImpowerSurvey.Components.Utilities;
using Microsoft.EntityFrameworkCore;

namespace ImpowerSurvey.Services
{
	/// <summary>
	/// Service for managing user accounts and authentication-related operations
	/// </summary>
	public class UserService(IDbContextFactory<SurveyDbContext> contextFactory, ILogService logService)
	{
		// ReSharper disable ReplaceWithPrimaryConstructorParameter
		private readonly IDbContextFactory<SurveyDbContext> _contextFactory = contextFactory;
		private readonly ILogService _logService = logService;
		// ReSharper restore ReplaceWithPrimaryConstructorParameter

		private static readonly Random Random = new();
		private static readonly char[] Symbols = "!@#$%^*()_+=[]{}|;:<>?".ToCharArray();

		/// <summary>
		/// Creates a new user with the specified details and generates a random strong password
		/// </summary>
		/// <param name="username">Unique username for the user</param>
		/// <param name="displayName">Display name for the user (defaults to username if empty)</param>
		/// <param name="email">Email address for the user</param>
		/// <param name="role">Role assigned to the user</param>
		/// <returns>A service result containing the generated password if successful</returns>
		public async Task<DataServiceResult<string>> CreateUserAsync(string username, string displayName, string email, Roles role)
		{
			try
			{
				await using var context = await _contextFactory.CreateDbContextAsync();
				if (await context.Users.AnyAsync(u => u.Username == username))
				{
					await _logService.LogAsync(LogSource.UserService, LogLevel.Warning,
										   $"Attempt to create user with existing username: {username}", true);
					return ServiceResult.Failure<string>(Constants.Users.Exists);
				}

				if (string.IsNullOrWhiteSpace(displayName))
					displayName = username;

				var password = GenerateStrongPassword();
				var user = new User
				{
					Username = username,
					DisplayName = displayName,
					PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
					Role = role,
					RequirePasswordChange = true,
					Emails = new Dictionary<ParticipationTypes, string> { { ParticipationTypes.Manual, email } }
				};

				context.Users.Add(user);
				await context.SaveChangesAsync();
				await _logService.LogAsync(LogSource.UserService, LogLevel.Information, $"Created new user: {username} with role: {role}", true);
				return ServiceResult.Success(password, Constants.Users.Created);
			}
			catch (Exception ex)
			{
				ex.LogException(_logService, LogSource.UserService, $"Error creating user: {username}", true);
				return ServiceResult.Failure<string>($"Failed to create user: {ex.Message}");
			}
		}

		/// <summary>
		/// Gets a user by their username
		/// </summary>
		/// <param name="username">The username to search for</param>
		/// <returns>The user if found, otherwise null</returns>
		public async Task<User> GetUserAsync(string username)
		{
			await using var context = await _contextFactory.CreateDbContextAsync();
			return await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Username == username);
		}

		/// <summary>
		/// Gets a user by their ID
		/// </summary>
		/// <param name="userId">The user ID to search for</param>
		/// <returns>The user if found, otherwise null</returns>
		public async Task<User> GetUserAsync(Guid userId)
		{
			await using var context = await _contextFactory.CreateDbContextAsync();
			return await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
		}
		
		/// <summary>
		/// Updates a user's timezone preference
		/// </summary>
		/// <param name="userId">The ID of the user to update</param>
		/// <param name="timeZone">The timezone identifier to set</param>
		/// <returns>True if the update was successful, false if the user was not found</returns>
		public async Task<bool> UpdateTimeZone(Guid userId, string timeZone)
		{
			await using var context = await _contextFactory.CreateDbContextAsync();
			var user = await context.Users.FindAsync(userId);
			
			if (user == null)
				return false;
				
			user.TimeZone = timeZone;
			await context.SaveChangesAsync();
			
			await _logService.LogAsync(LogSource.UserService, LogLevel.Information, 
				$"Updated timezone for user {user.Username} to {timeZone}");
				
			return true;
		}

		/// <summary>
		/// Gets all users with the specified roles
		/// </summary>
		/// <param name="roles">Array of roles to filter by</param>
		/// <returns>Collection of users with the specified roles</returns>
		public async Task<IEnumerable<User>> GetUsersByRoles(params Roles[] roles)
		{
			await using var context = await _contextFactory.CreateDbContextAsync();
			return await context.Users
								.Where(x => roles.Contains(x.Role))
								.AsNoTracking()
								.ToListAsync();
		}

		/// <summary>
		/// Generates a strong random password with letters, numbers, and symbols
		/// </summary>
		/// <returns>A randomly generated password string</returns>
		private static string GenerateStrongPassword()
		{
			var partialGuid = Guid.NewGuid().ToString("N")[..12];

			var fourSymbols = new string(Enumerable.Repeat(Symbols, 4)
											   .Select(s => s[Random.Next(s.Length)])
											   .ToArray());

			return new string((partialGuid + fourSymbols).OrderBy(_ => Random.Next()).ToArray());
		}

		/// <summary>
		/// Deletes a user by ID
		/// </summary>
		/// <param name="userId">The ID of the user to delete</param>
		/// <returns>A service result indicating success or failure</returns>
		public async Task<ServiceResult> DeleteUserAsync(Guid userId)
		{
			try
			{
				await using var context = await _contextFactory.CreateDbContextAsync();
				var user = await context.Users.FindAsync(userId);
				if (user == null)
				{
					await _logService.LogAsync(LogSource.UserService, LogLevel.Warning,
										   string.Format(Constants.Users.NotFound, "delete", userId), true);
					return ServiceResult.Failure(Constants.Users.NotFound);
				}

				context.Users.Remove(user);
				await context.SaveChangesAsync();
				await _logService.LogAsync(LogSource.UserService, LogLevel.Information, $"Deleted user: {user.Username} (ID: {userId})", true);
				return ServiceResult.Success(Constants.Users.Deleted);
			}
			catch (Exception ex)
			{
				ex.LogException(_logService, LogSource.UserService, $"Error deleting user with ID: {userId}", true);
				return ServiceResult.Failure($"Failed to delete user: {ex.Message}");
			}
		}

		/// <summary>
		/// Resets a user's password and forces them to change it on next login
		/// </summary>
		/// <param name="userId">The ID of the user whose password to reset</param>
		/// <returns>A service result containing the new password if successful</returns>
		public async Task<DataServiceResult<string>> ResetPassword(Guid userId)
		{
			try
			{
				await using var dbContext = await _contextFactory.CreateDbContextAsync();
				var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

				if (user == null)
				{
					await _logService.LogAsync(LogSource.UserService, LogLevel.Warning,
										   string.Format(Constants.Users.NotFound, "reset password for", userId), true);
					return ServiceResult.Failure<string>(Constants.Users.NotFound);
				}

				var newPassword = GenerateStrongPassword();
				user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
				user.RequirePasswordChange = true;
				await dbContext.SaveChangesAsync();

				await _logService.LogAsync(LogSource.UserService, LogLevel.Information, $"Reset password for user: {user.Username} (ID: {userId})", true);
				return ServiceResult.Success(newPassword, Constants.Users.PasswordUpdated);
			}
			catch (Exception ex)
			{
				ex.LogException(_logService, LogSource.UserService, $"Error resetting password for user with ID: {userId}", true);
				return ServiceResult.Failure<string>($"Failed to reset password: {ex.Message}");
			}
		}

		/// <summary>
		/// Updates a user's email address for a specific participation type
		/// </summary>
		/// <param name="userId">The ID of the user to update</param>
		/// <param name="email">The new email address</param>
		/// <param name="participationType">The participation type to associate with the email</param>
		/// <returns>A service result indicating success or failure</returns>
		public async Task<ServiceResult> UpdateEmail(Guid userId, string email, ParticipationTypes participationType = ParticipationTypes.Manual)
		{
			try
			{
				await using var dbContext = await _contextFactory.CreateDbContextAsync();
				var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

				if (user == null)
				{
					await _logService.LogAsync(LogSource.UserService, LogLevel.Warning, string.Format(Constants.Users.NotFound, "update email for", userId), true);
					return ServiceResult.Failure(Constants.Users.NotFound);
				}

				var oldEmail = user.Emails.GetValueOrDefault(participationType);
				user.Emails[participationType] = email;
				await dbContext.SaveChangesAsync();

				await _logService.LogAsync(LogSource.UserService, LogLevel.Information, $"Updated {participationType} email for user: {user.Username} (ID: {userId}) from '{oldEmail}' to '{email}'", true);
				return ServiceResult.Success(Constants.Users.EmailUpdated);
			}
			catch (Exception ex)
			{
				ex.LogException(_logService, LogSource.UserService, $"Error updating email for user with ID: {userId}", true);
				return ServiceResult.Failure($"Failed to update email: {ex.Message}");
			}
		}

		/// <summary>
		/// Changes a user's password and removes the required password change flag
		/// </summary>
		/// <param name="userId">The ID of the user whose password to change</param>
		/// <param name="newPassword">The new password</param>
		/// <returns>A service result containing the login URL if successful</returns>
		public async Task<DataServiceResult<string>> ChangePasswordAsync(Guid userId, string newPassword)
		{
			try
			{
				await using var dbContext = await _contextFactory.CreateDbContextAsync();
				var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

				if (user == null)
				{
					await _logService.LogAsync(LogSource.UserService, LogLevel.Warning,
										   string.Format(Constants.Users.NotFound, "change password for", userId), true);
					return ServiceResult.Failure<string>(Constants.Users.NotFound);
				}

				user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
				user.RequirePasswordChange = false;
				await dbContext.SaveChangesAsync();

				await _logService.LogAsync(LogSource.UserService, LogLevel.Information,
									   $"User {user.Username} (ID: {userId}) changed their password", true);
				return ServiceResult.Success("/login", Constants.Users.PasswordUpdated);
			}
			catch (Exception ex)
			{
				ex.LogException(_logService, LogSource.UserService,
							$"Error changing password for user with ID: {userId}", true);
				return ServiceResult.Failure<string>($"Failed to change password: {ex.Message}");
			}
		}
	}
}