using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RainbowAvatarBot.Services;

internal sealed class UserSettingsService : IDisposable
{
	private readonly string _configLocation = Path.Combine("data", "config.json");
	private readonly SemaphoreSlim _saveLock = new(1, 1);
	private ConcurrentDictionary<long, UserSettings> _userSettings = new();
	private bool _savingScheduled;

	public UserSettings GetSettingsForUser(long userId)
	{
		_userSettings.TryGetValue(userId, out var value);
		return value ?? new UserSettings();
	}

	public void SetBlendModeForUser(long userId, BlendMode value)
	{
		if (_userSettings.TryGetValue(userId, out var userSettings))
		{
			userSettings.BlendMode = value;
		} else
		{
			_userSettings.TryAdd(userId, new UserSettings {BlendMode = value});
		}

		Task.Run(Save);
	}

	public void SetFlagForUser(long userId, string value)
	{
		if (_userSettings.TryGetValue(userId, out var userSettings))
		{
			userSettings.FlagName = value;
		} else
		{
			_userSettings.TryAdd(userId, new UserSettings {FlagName = value});
		}

		Task.Run(Save);
	}

	public void SetOpacityForUser(long userId, byte value)
	{
		if (_userSettings.TryGetValue(userId, out var userSettings))
		{
			userSettings.Opacity = value;
		} else
		{
			_userSettings.TryAdd(userId, new UserSettings {Opacity = value});
		}
	}

	public async Task Initialize()
	{
		if (File.Exists(_configLocation))
		{
			await using var configFile = File.OpenRead(_configLocation);
			_userSettings = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<long, UserSettings>>(configFile) ??
				throw new InvalidOperationException("Settings are null");
		}
		else
		{
			Directory.CreateDirectory("data");
			await Save();
		}
	}

	private async Task Save()
	{
		if (Interlocked.Exchange(ref _savingScheduled, true))
		{
			return;
		}

		await _saveLock.WaitAsync();

		try
		{
			Interlocked.Exchange(ref _savingScheduled, false);
			await using var configFile = File.Open(_configLocation, FileMode.Create, FileAccess.Write, FileShare.None);
			await JsonSerializer.SerializeAsync(configFile, _userSettings);
		}
		finally
		{
			_saveLock.Release();
		}
	}

	public void Dispose()
	{
		_saveLock.Dispose();
	}
}
