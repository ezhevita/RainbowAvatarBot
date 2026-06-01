namespace RainbowAvatarBot;

#pragma warning disable CA1515 // Consider making public types internal -- necessary for benchmarks
public sealed record UserSettings
#pragma warning restore CA1515 // Consider making public types internal
{
	public string FlagName { get; set; } = "LGBT";
	public byte Opacity { get; set; } = 50;
	public BlendMode BlendMode { get; set; } = BlendMode.HardLight;
}
