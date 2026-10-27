namespace RainbowAvatarBot.Database;

internal sealed class UserSettingsEntity
{
	public long UserId { get; set; }
	public string FlagName { get; set; } = "LGBT";
	public byte Opacity { get; set; } = 50;
	public BlendMode BlendMode { get; set; } = BlendMode.HardLight;
}
