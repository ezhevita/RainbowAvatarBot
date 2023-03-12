namespace RainbowAvatarBot;

public record OverlayVideoFilterArgumentOptions
{
	public int Width { get; set; }
	public int Height { get; set; }
	public float Opacity { get; set; }
	public string OverlayMode { get; set; }
}
