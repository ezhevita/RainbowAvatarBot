using System.Globalization;
using FFMpegCore.Arguments;

namespace RainbowAvatarBot.FFMpeg;

internal class OverlayVideoFilterArgument : IArgument
{
	public OverlayVideoFilterArgument(float opacity, BlendMode blendMode)
	{
		var opacityText = opacity.ToString(CultureInfo.InvariantCulture);
		var isRgbRequired = blendMode is not (BlendMode.Normal or BlendMode.HardLight or BlendMode.Overlay);
		Text = "-filter_complex \"" +
			(isRgbRequired ? "[0:v]format=pix_fmts=gbrap,split=3[vid][ref][alpha];" : "[0:v]split=3[vid][ref][alpha];") +
			(isRgbRequired ? "[alpha]alphaextract[mask];" : "[alpha]format=pix_fmts=yuva420p,alphaextract[mask];") +
			"[1:v][ref]scale=rw:rh:flags=neighbor[scaled];" +
#pragma warning disable CA1308 // Normalize strings to uppercase -- FFmpeg requires lowercase and our enum names perfectly match
			$"[vid][scaled]blend=all_mode={blendMode.ToString().ToLowerInvariant()}:all_opacity={opacityText}[blended];" +
#pragma warning restore CA1308 // Normalize strings to uppercase
			"[blended][mask]alphamerge\"";
	}

	public string Text { get; }
}
