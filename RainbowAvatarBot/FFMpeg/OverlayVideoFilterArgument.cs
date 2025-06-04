using System.Globalization;
using FFMpegCore.Arguments;

namespace RainbowAvatarBot.FFMpeg;

internal class OverlayVideoFilterArgument : IArgument
{
	public OverlayVideoFilterArgument(float opacity, string overlayMode)
	{
		var opacityText = opacity.ToString(CultureInfo.InvariantCulture);
		Text = "-filter_complex \"" +
			"[0:v]split=3[vid][ref][alpha];" +
			"[alpha]format=pix_fmts=yuva420p,alphaextract[mask];" +
			"[1:v][ref]scale=rw:rh:flags=neighbor[scaled];" +
			$"[vid][scaled]blend=all_mode={overlayMode}:all_opacity={opacityText}[blended];" +
			"[blended][mask]alphamerge\"";
	}

	public string Text { get; }
}
