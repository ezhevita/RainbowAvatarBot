using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace RainbowAvatarBot;

internal static class Extensions
{
	private static readonly PngEncoder PngEncoder = new()
	{
		CompressionLevel = PngCompressionLevel.NoCompression
	};

	internal static void Overlay(this Image sourceImage, Image overlayImage)
	{
		using var resized = overlayImage.Clone(
			img => img.Resize(sourceImage.Width, sourceImage.Height, new NearestNeighborResampler())
		);
		// ReSharper disable once AccessToDisposedClosure
		sourceImage.Mutate(
			img => img.DrawImage(resized, PixelColorBlendingMode.HardLight, PixelAlphaCompositionMode.SrcAtop, 0.5f)
		);
	}

	internal static Task SaveToPng(this Image image, Stream streamToWrite, CancellationToken cancellationToken = default) =>
		image.SaveAsPngAsync(streamToWrite, PngEncoder, cancellationToken);

	public static FFMpegArgumentOptions WithOverlayVideoFilter(this FFMpegArgumentOptions ffMpegArgumentOptions,
		Action<OverlayVideoFilterArgumentOptions> overlayOptions)
	{
		var options = new OverlayVideoFilterArgumentOptions();
		overlayOptions(options);
		return ffMpegArgumentOptions.WithArgument(new OverlayVideoFilterArgument(options));
	}
}
