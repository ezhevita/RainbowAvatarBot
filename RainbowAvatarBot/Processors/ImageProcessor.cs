using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

public class ImageProcessor : IProcessor
{
	private readonly Dictionary<string, Image> _images = new();
	private readonly RecyclableMemoryStreamManager _memoryStreamManager;

	public ImageProcessor(RecyclableMemoryStreamManager memoryStreamManager) => _memoryStreamManager = memoryStreamManager;

	public async Task Init(IReadOnlyDictionary<string, IReadOnlyCollection<uint>> flagsData)
	{
		var existFiles = Directory.EnumerateFiles("images", "*.png").Select(Path.GetFileNameWithoutExtension);
		if (flagsData.Keys.Any(name => !existFiles.Contains(name)))
		{
			var generateTasks = flagsData.Select(x => GenerateImage(x.Key, x.Value));
			await Task.WhenAll(generateTasks);
		}

		var imageLoadTasks = flagsData.Select(x => Image.LoadAsync(Path.Join("images", x.Key + ".png")));
		var images = await Task.WhenAll(imageLoadTasks);

		foreach (var (image, name) in images.Zip(flagsData.Keys))
		{
			_images.Add(name, image);
		}
	}

	public bool CanProcessMediaType(MediaType mediaType) => mediaType is MediaType.Picture or MediaType.Sticker;

	public async Task<InputMedia> Process(Stream input, string overlayName, MediaType mediaType)
	{
		var image = await Image.LoadAsync(input).ConfigureAwait(false);
		image.Overlay(_images[overlayName]);

		var result = _memoryStreamManager.GetStream("ResultPictureStream", 1 * 1024 * 1024);

		Func<Image, Stream, CancellationToken, Task> saveTask =
			mediaType == MediaType.Picture
				? Extensions.SaveToPng
				: ImageExtensions.SaveAsWebpAsync;

		await saveTask(image, result, default).ConfigureAwait(false);

		result.Position = 0;

		var fileName = mediaType switch
		{
			MediaType.Picture => "picture.png",
			MediaType.Sticker => "sticker.webp",
			_ => throw new ArgumentOutOfRangeException(nameof(mediaType))
		};

		return new InputMedia(result, fileName);
	}

	private static async Task GenerateImage(string name, IReadOnlyCollection<uint> colors)
	{
		using Image<Rgba32> image = new(1, colors.Count);
		byte index = 0;
		foreach (var color in colors)
		{
			var indexCopy = index;

			image.Mutate(
				img => img.Fill(
					new Argb32((byte) (color >> 16), (byte) ((color >> 8) & 0xFF), (byte) (color & 0xFF)),
					new RectangleF(0, indexCopy, 1, 1)
				)
			);
			index++;
		}

		await image.SaveAsPngAsync(Path.Join("images", name + ".png"));
	}
}
