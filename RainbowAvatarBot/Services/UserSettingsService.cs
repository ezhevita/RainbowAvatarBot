using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RainbowAvatarBot.Database;

namespace RainbowAvatarBot.Services;

internal sealed class UserSettingsService
{
	private readonly IDbContextFactory<UserSettingsDbContext> _contextFactory;

	public UserSettingsService(IDbContextFactory<UserSettingsDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	public async Task<UserSettings> GetSettingsForUserAsync(long userId)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();

		return await context.UserSettings.AsNoTracking()
			.Where(settings => settings.UserId == userId)
			.Select(settings => new UserSettings
			{
				FlagName = settings.FlagName,
				Opacity = settings.Opacity,
				BlendMode = settings.BlendMode
			})
			.SingleOrDefaultAsync() ?? new UserSettings();
	}

	public Task SetBlendModeForUserAsync(long userId, BlendMode value)
	{
		return UpdateSettingAsync(userId, settings => settings.BlendMode = value);
	}

	public Task SetFlagForUserAsync(long userId, string value)
	{
		return UpdateSettingAsync(userId, settings => settings.FlagName = value);
	}

	public Task SetOpacityForUserAsync(long userId, byte value)
	{
		return UpdateSettingAsync(userId, settings => settings.Opacity = value);
	}

	private async Task UpdateSettingAsync(long userId, Action<UserSettingsEntity> update)
	{
		await using var context = await _contextFactory.CreateDbContextAsync();
		var settings = await context.UserSettings.FindAsync(userId);
		bool isNew = false;
		if (settings is null)
		{
			isNew = true;
			settings = new UserSettingsEntity {UserId = userId};
			context.UserSettings.Add(settings);
		}

		update(settings);
		try
		{
			await context.SaveChangesAsync();
		}
		catch (DbUpdateException exception) when (isNew && exception.InnerException is PostgresException
		                                          {
			                                          SqlState: PostgresErrorCodes.UniqueViolation,
			                                          ConstraintName: "user_settings_pkey"
		                                          })
		{
			context.ChangeTracker.Clear();
			settings = await context.UserSettings.SingleAsync(value => value.UserId == userId);
			update(settings);
			await context.SaveChangesAsync();
		}
	}
}
