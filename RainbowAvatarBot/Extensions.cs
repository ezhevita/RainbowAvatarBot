using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Imazen.WebP;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using Image = SixLabors.ImageSharp.Image;

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
		
		internal static async Task<MemoryStream> SaveToPng(this Image image) {
			MemoryStream stream = new();
			await image.SaveAsPngAsync(stream, PngEncoder);
			stream.Position = 0;
			return stream;
		}

		internal static async Task<MemoryStream> SaveToWebp(this Image image) {
			MemoryStream bitmapStream = new();
			MemoryStream resultStream = new();
			SimpleEncoder encoder = new();
			await image.SaveAsPngAsync(bitmapStream, PngEncoder);

			bitmapStream.Seek(0, SeekOrigin.Begin);
			
			encoder.Encode(new Bitmap(bitmapStream), resultStream, 95);
			bitmapStream.Position = 0;
			return bitmapStream;
		}
	}
}
