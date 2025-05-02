using ImpowerSurvey.Components.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace ImpowerSurvey.Services;

public class SurveyDbContext : DbContext
{
	public DbSet<User> Users { get; set; }
	public DbSet<Survey> Surveys { get; set; }
	public DbSet<Question> Questions { get; set; }
	public DbSet<QuestionOption> QuestionOptions { get; set; }
	public DbSet<Response> Responses { get; set; }
	public DbSet<EntryCode> EntryCodes { get; set; }
	public DbSet<CompletionCode> CompletionCodes { get; set; }
	public DbSet<ParticipationRecord> ParticipationRecords { get; set; }
	public DbSet<Setting> Settings { get; set; }
	public DbSet<Log> Logs { get; set; }

	public SurveyDbContext(DbContextOptions<SurveyDbContext> options) : base(options) { }

	private const string GUID_DB_TYPE = "uuid";
	private const string DATE_DB_TYPE = "NOW()";

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.ApplyConfiguration(new UserConfiguration());

		modelBuilder.Entity<User>().HasKey(u => u.Id);
		modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
		modelBuilder.Entity<User>()
					.Property(u => u.Emails)
					.HasConversion(
								   v => JsonSerializer.Serialize(v.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToLower()), (JsonSerializerOptions)null),
								   v => JsonSerializer.Deserialize<Dictionary<ParticipationTypes, string>>(v, (JsonSerializerOptions)null),
								   new ValueComparer<Dictionary<ParticipationTypes, string>>((c1, c2) => c1.SequenceEqual(c2),
																							 c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
																							 c => c.ToDictionary(entry => entry.Key, entry => entry.Value)));

		modelBuilder.Entity<Survey>(entity =>
		{
			entity.HasKey(s => s.Id);
			entity.Property(s => s.Id).HasColumnType(GUID_DB_TYPE);
			entity.Property(s => s.ManagerId).HasColumnType(GUID_DB_TYPE);
			entity.HasOne(s => s.Manager)
				  .WithMany(u => u.ManagedSurveys)
				  .HasForeignKey(s => s.ManagerId)
				  .OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<Question>(entity =>
		{
			entity.HasKey(q => q.Id);
			entity.Property(q => q.SurveyId).HasColumnType(GUID_DB_TYPE);
			entity.HasOne(q => q.Survey)
				  .WithMany(s => s.Questions)
				  .HasForeignKey(q => q.SurveyId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<QuestionOption>(entity =>
		{
			entity.HasKey(qo => qo.Id);
			entity.HasOne(qo => qo.Question)
				  .WithMany(q => q.Options)
				  .HasForeignKey(qo => qo.QuestionId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<Response>(entity =>
		{
			entity.HasKey(r => r.Id);
			entity.Property(r => r.SurveyId).HasColumnType(GUID_DB_TYPE);
			entity.HasOne(r => r.Survey)
				  .WithMany(s => s.Responses)
				  .HasForeignKey(r => r.SurveyId)
				  .OnDelete(DeleteBehavior.NoAction);
			entity.HasOne(r => r.Question)
				  .WithMany()
				  .HasForeignKey(r => r.QuestionId)
				  .OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<EntryCode>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.SurveyId).HasColumnType(GUID_DB_TYPE);
			entity.HasOne(e => e.Survey)
				  .WithMany(s => s.EntryCodes)
				  .HasForeignKey(e => e.SurveyId)
				  .OnDelete(DeleteBehavior.Cascade);
			entity.HasIndex(e => e.Code).IsUnique();
		});

		modelBuilder.Entity<CompletionCode>(entity =>
		{
			entity.HasKey(c => c.Id);
			entity.Property(c => c.SurveyId).HasColumnType(GUID_DB_TYPE);
			entity.HasOne(c => c.Survey)
				  .WithMany(s => s.CompletionCodes)
				  .HasForeignKey(c => c.SurveyId)
				  .OnDelete(DeleteBehavior.Cascade);
			entity.HasIndex(c => c.Code).IsUnique();
		});

		modelBuilder.Entity<ParticipationRecord>(entity =>
		{
			entity.HasKey(ct => ct.Id);
			entity.HasOne(ct => ct.CompletionCode)
				  .WithOne()
				  .HasForeignKey<ParticipationRecord>(x => x.CompletionCodeId)
				  .OnDelete(DeleteBehavior.Cascade);
		});
		
		modelBuilder.Entity<Setting>(entity =>
		{
			entity.HasKey(s => s.Id);
			entity.Property(s => s.Id).HasColumnType(GUID_DB_TYPE);
			entity.HasIndex(s => s.Key).IsUnique();
		});
		
		modelBuilder.Entity<Log>(entity =>
		{
			entity.HasKey(l => l.Id);
			entity.Property(l => l.Timestamp).HasDefaultValueSql(DATE_DB_TYPE);
			entity.HasIndex(l => l.Timestamp);
			entity.HasIndex(l => l.Level);
			entity.HasIndex(l => l.Source);
			entity.Property(l => l.Message).IsRequired();
		});
	}

	public class UserConfiguration : IEntityTypeConfiguration<User>
	{
		public void Configure(EntityTypeBuilder<User> builder)
		{
			var lowercaseConverter = new ValueConverter<string, string>(x => x.ToLower(), x => x);

			builder.Property(x => x.Username)
				   .HasConversion(lowercaseConverter);
		}
	}
}