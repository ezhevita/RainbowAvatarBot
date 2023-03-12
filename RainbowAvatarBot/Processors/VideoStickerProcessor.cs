using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.IO;
using Telegram.Bot.Types;
using File = System.IO.File;

namespace RainbowAvatarBot.Processors;

public class VideoStickerProcessor : IProcessor
{
	private readonly RecyclableMemoryStreamManager _memoryStreamManager;

	public VideoStickerProcessor(RecyclableMemoryStreamManager memoryStreamManager)
	{
		_memoryStreamManager = memoryStreamManager;
	}
	public bool CanProcessMediaType(MediaType mediaType) => mediaType == MediaType.VideoSticker;

	public Task Init(IReadOnlyDictionary<string, IReadOnlyCollection<uint>> flagsData) => Task.CompletedTask;

	public async Task<InputMedia> Process(Stream input, string overlayName, MediaType mediaType)
	{
		var resultStream = _memoryStreamManager.GetStream("ResultVideoStream", 256 * 1024);

		var resultFileName = Path.GetTempFileName();

		try
		{
			var ffMpegArguments = FFMpegArguments.FromPipeInput(new StreamPipeSource(input))
				.AddFileInput(Path.Join("images", overlayName + ".png"))
				.OutputToPipe(
					new StreamPipeSink(resultStream), addArguments: options => options.ForceFormat(VideoType.WebM)
						.ForcePixelFormat("yuva420p")
						.WithVideoCodec("libvpx-vp9")
						.WithVideoBitrate(400)
						.WithOverlayVideoFilter(
							overlayOptions =>
							{
								overlayOptions.OverlayMode = "overlay";
								overlayOptions.Opacity = 0.5f;
								overlayOptions.Width = 512;
								overlayOptions.Height = 512;
							}
						)
				);

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

		return new InputMedia(resultStream, "sticker.webm");
	}
}
