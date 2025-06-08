using System;
using SixLabors.ImageSharp.PixelFormats;

namespace RainbowAvatarBot;

// Names must correspond to the FFMpeg's blending mode names (case-insensitive)
internal enum BlendMode
{
	Normal,
	Multiply,
	Screen,
	Overlay,
	Darken,
	Lighten,
	HardLight,
	Addition
}

internal static class BlendModeExtensions
{
	public static PixelColorBlendingMode ToImageSharp(this BlendMode blendMode)
	{
		return blendMode switch
		{
			BlendMode.Addition => PixelColorBlendingMode.Add,
			BlendMode.Darken => PixelColorBlendingMode.Darken,
			BlendMode.HardLight => PixelColorBlendingMode.HardLight,
			BlendMode.Lighten => PixelColorBlendingMode.Lighten,
			BlendMode.Multiply => PixelColorBlendingMode.Multiply,
			BlendMode.Normal => PixelColorBlendingMode.Normal,
			BlendMode.Overlay => PixelColorBlendingMode.Overlay,
			BlendMode.Screen => PixelColorBlendingMode.Screen,
			_ => throw new ArgumentOutOfRangeException(nameof(blendMode), blendMode, null)
		};
	}
}
