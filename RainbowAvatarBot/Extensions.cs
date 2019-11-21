using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using SixLabors.Primitives;

namespace RainbowAvatarBot {
	internal static class Extensions {
		private static readonly ColorSpaceConverter Converter = new ColorSpaceConverter();
		private static bool IsBrightPicture(this Image<Rgba32> image) {
			using Image<Rgba32> pixelImage = image.Clone(img => img.Resize(1, 1, new Lanczos5Resampler()));
			float calulcatedLight = Converter.ToHsl(pixelImage[0, 0]).L;
			return calulcatedLight >= 0.5;
		}

		[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
		[SuppressMessage("ReSharper", "ImplicitlyCapturedClosure")]
		internal static void Overlay(this Image<Rgba32> sourceImage, Image<Rgba32> overlayImage) {
			using Image<Rgba32> resized = overlayImage.Clone(img => img.Resize(sourceImage.Width, sourceImage.Height));
			sourceImage.Mutate(img => img.DrawImage(resized, Point.Empty, sourceImage.IsBrightPicture() ? PixelColorBlendingMode.HardLight : PixelColorBlendingMode.Normal, 0.5f));
		}
	}
}