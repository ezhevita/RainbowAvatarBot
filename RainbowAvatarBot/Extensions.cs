using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;

namespace RainbowAvatarBot {
	internal static class Extensions {
		private static bool IsBrightPicture(this Image<Rgba32> image) {
			List<float> lightness = new List<float>();
			ColorSpaceConverter converter = new ColorSpaceConverter();
			for (int w = 0; w < image.Width; w++) {
				for (int h = 0; h < image.Height; h++) {
					lightness.Add(converter.ToHsl(image[w, h]).L);
				}
			}

			int count = lightness.Count;
			List<float> ordered = lightness.OrderBy(p => p).ToList();
			double median = ordered.ElementAt(count / 2) + ordered.ElementAt((count - 1) / 2);
			median /= 2;

			return median >= 0.5;
		}

		internal static void Overlay(this Image<Rgba32> sourceImage, Image<Rgba32> overlayImage) {
			using (Image<Rgba32> resized = overlayImage.Clone()) {
				resized.Mutate(rImg => rImg.Resize(sourceImage.Width, sourceImage.Height));
				// We can ignore it safely because lambda is executed within using block
				// ReSharper disable AccessToDisposedClosure
				sourceImage.Mutate(img => img.DrawImage(resized, Point.Empty, sourceImage.IsBrightPicture() ? PixelColorBlendingMode.HardLight : PixelColorBlendingMode.Normal, 0.5f));
				// ReSharper restore AccessToDisposedClosure
			}
		}
	}
}