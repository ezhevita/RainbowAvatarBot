using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.IO;
using RainbowAvatarBot.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

internal class ImageProcessor : IProcessor
{
	private readonly PngEncoder _pngEncoder = new()
	{
		ColorType = PngColorType.RgbWithAlpha,
		CompressionLevel = PngCompressionLevel.NoCompression,
		TransparentColorMode = PngTransparentColorMode.Clear
	};

	private readonly WebpEncoder _webpEncoder = new()
	{
		FileFormat = WebpFileFormatType.Lossless,
		Quality = 0,
		Method = WebpEncodingMethod.Fastest
	};

	private readonly FlagImageService _flagImageService;
	private readonly RecyclableMemoryStreamManager _memoryStreamManager;

	public ImageProcessor(FlagImageService flagImageService, RecyclableMemoryStreamManager memoryStreamManager)
	{
		_flagImageService = flagImageService;
		_memoryStreamManager = memoryStreamManager;
	}

	public IEnumerable<MediaType> SupportedMediaTypes => [MediaType.Picture, MediaType.Sticker];

	public async Task<InputFileStream> Process(Stream input, UserSettings settings, bool isSticker)
	{
		var image = await Image.LoadAsync(input);
		using (var resized = _flagImageService.GetFlag(settings.FlagName)
				   .Clone(img => img.Resize(image.Width, image.Height, new NearestNeighborResampler())))
		{
			// ReSharper disable once AccessToDisposedClosure
			image.Mutate(img => img.DrawImage(
				resized, settings.BlendMode.ToImageSharp(), PixelAlphaCompositionMode.SrcAtop, settings.Opacity / 100.0f));
		}

		var result = _memoryStreamManager.GetStream(nameof(ImageProcessor), input.Length);

		string fileName;
		if (isSticker)
		{
			await image.SaveAsWebpAsync(result, _webpEncoder);
			fileName = "sticker.webp";
		}
		else
		{
			await image.SaveAsPngAsync(result, _pngEncoder);
			fileName = "picture.png";
		}

		result.Position = 0;
		return new InputFileStream(result, fileName);
	}
}
