using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace RainbowAvatarBot {
	internal static class Extensions {
		private static readonly PngEncoder PngEncoder = new() {
			CompressionLevel = PngCompressionLevel.NoCompression
		};

		internal static void Overlay(this Image sourceImage, Image overlayImage) {
			using Image resized = overlayImage.Clone(img => img.Resize(sourceImage.Width, sourceImage.Height, new NearestNeighborResampler()));
			// ReSharper disable once AccessToDisposedClosure
			sourceImage.Mutate(img => img.DrawImage(resized, PixelColorBlendingMode.HardLight, PixelAlphaCompositionMode.SrcAtop, 0.5f));
		}

		internal static async Task SaveToPng(this Image image, Stream streamToWrite) {
			await image.SaveAsPngAsync(streamToWrite, PngEncoder).ConfigureAwait(false);
			streamToWrite.Position = 0;
		}
	}
}
