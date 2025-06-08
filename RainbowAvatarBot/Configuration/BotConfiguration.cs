namespace RainbowAvatarBot.Configuration;

internal class BotConfiguration
{
	public string Token { get; init; } = null!;
	public long OwnerId { get; init; }
	public string PackNamePrefix { get; init; } = null!;
	public bool EnableBlendModeSettings { get; init; }
}
