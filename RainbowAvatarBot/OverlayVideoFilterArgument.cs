using System.Globalization;
using FFMpegCore.Arguments;

namespace RainbowAvatarBot;

public class OverlayVideoFilterArgument : IArgument
{
	private readonly OverlayVideoFilterArgumentOptions _options;

	public OverlayVideoFilterArgument(OverlayVideoFilterArgumentOptions options)
	{
		_options = options;
	}

	public string Text => $"-filter_complex \"[1:v]scale={_options.Width}:{_options.Height}:flags=neighbor [scld]," +
		$"[0:v][scld]blend=all_mode='{_options.OverlayMode}':all_opacity={_options.Opacity.ToString(CultureInfo.InvariantCulture)}\"";
}
