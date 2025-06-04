using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.IO;
using RainbowAvatarBot.FFMpeg;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

internal class VideoStickerProcessor : IProcessor
{
	private readonly OverlayVideoFilterArgument _overlayFfmpegArgument = new(0.5F, "hardlight");
	private readonly RecyclableMemoryStreamManager _memoryStreamManager;

	private const string videoCodec = "libvpx-vp9";

	public VideoStickerProcessor(RecyclableMemoryStreamManager memoryStreamManager)
	{
		_memoryStreamManager = memoryStreamManager;
	}

	public IEnumerable<MediaType> SupportedMediaTypes => [MediaType.VideoSticker];

	public async Task<InputFileStream> Process(Stream input, string overlayName, bool isSticker)
	{
		if (!isSticker)
		{
			throw new ArgumentException("Expected input to be a sticker.", nameof(isSticker));
		}

		var resultStream = _memoryStreamManager.GetStream(nameof(VideoStickerProcessor), input.Length);

		var resultFileName = Path.GetTempFileName();

		try
		{
			var ffMpegArguments = FFMpegArguments.FromPipeInput(
					new StreamPipeSource(input), options => options.WithVideoCodec(videoCodec))
				.AddFileInput(Path.Combine("images", overlayName + ".png"))
				.OutputToPipe(
					new StreamPipeSink(resultStream), addArguments: options => options.ForceFormat(VideoType.WebM)
						// This is necessary to make TGMac client use video resolution, otherwise it stretches height to 512
						.WithDuration(TimeSpan.FromSeconds(3))
						.WithArgument(_overlayFfmpegArgument));

			await ffMpegArguments.ProcessAsynchronously();

			await using var resultFile = File.OpenRead(resultFileName);

			await resultFile.CopyToAsync(resultStream);
		} finally
		{
			if (File.Exists(resultFileName))
			{
				File.Delete(resultFileName);
			}
		}

		resultStream.Position = 0;

		return new InputFileStream(resultStream, "sticker.webm");
	}
}
