namespace RainbowAvatarBot;

internal sealed record UserSettings
{
	public string FlagName { get; set; } = "LGBT";
	public byte Opacity { get; set; } = 50;
	public BlendMode BlendMode { get; set; } = BlendMode.HardLight;
}
