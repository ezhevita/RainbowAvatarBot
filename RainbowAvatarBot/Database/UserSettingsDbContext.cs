using Microsoft.EntityFrameworkCore;

namespace RainbowAvatarBot.Database;

internal sealed class UserSettingsDbContext : DbContext
{
	public UserSettingsDbContext(DbContextOptions<UserSettingsDbContext> options) : base(options)
	{
	}

	public DbSet<UserSettingsEntity> UserSettings => Set<UserSettingsEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		var settings = modelBuilder.Entity<UserSettingsEntity>();
		settings.ToTable(
			"user_settings", table =>
			{
				table.HasCheckConstraint("user_settings_user_id_check", "user_id BETWEEN 1 AND 4503599627370495");
				table.HasCheckConstraint("user_settings_opacity_check", "opacity BETWEEN 0 AND 100");
				table.HasCheckConstraint("user_settings_blend_mode_check", "blend_mode BETWEEN 0 AND 7");
			});
		settings.HasKey(value => value.UserId).HasName("user_settings_pkey");
		settings.Property(value => value.UserId).HasColumnName("user_id").ValueGeneratedNever();
		settings.Property(value => value.FlagName).HasColumnName("flag_name").IsRequired().HasMaxLength(16)
			.HasDefaultValue("LGBT").ValueGeneratedNever();
		settings.Property(value => value.Opacity).HasColumnName("opacity").HasConversion<short>()
			.HasDefaultValue((byte) 50).ValueGeneratedNever();
		settings.Property(value => value.BlendMode).HasColumnName("blend_mode").HasConversion<int>()
			.HasDefaultValue(BlendMode.HardLight).ValueGeneratedNever();
	}
}
