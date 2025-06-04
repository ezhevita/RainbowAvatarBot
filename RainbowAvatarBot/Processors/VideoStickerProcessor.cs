using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

internal partial class VideoStickerProcessor : IProcessor
{
	private readonly OverlayVideoFilterArgument _overlayFfmpegArgument = new(0.5F, "hardlight");
	private readonly RecyclableMemoryStreamManager _memoryStreamManager;
	private readonly ILogger<VideoStickerProcessor> _logger;

	private const string videoCodec = "libvpx-vp9";

	public VideoStickerProcessor(RecyclableMemoryStreamManager memoryStreamManager, ILogger<VideoStickerProcessor> logger)
	{
		_memoryStreamManager = memoryStreamManager;
		_logger = logger;
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
					new StreamPipeSource(input), options => options.WithVideoCodec(videoCodec)
						.WithHardwareAcceleration())
				.AddFileInput(Path.Combine("images", overlayName + ".png"))
				.OutputToPipe(
					new StreamPipeSink(resultStream), addArguments: options => options.ForceFormat(VideoType.WebM)
						.WithVideoCodec(videoCodec)
						.WithVideoBitrate(400)
						.WithArgument(_overlayFfmpegArgument)
						.UsingMultithreading(true))
				.NotifyOnError(LogFFmpegError);

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

	[LoggerMessage(LogLevel.Error, "An error occurred during FFmpeg execution: {Message}")]
	private partial void LogFFmpegError(string message);
}
