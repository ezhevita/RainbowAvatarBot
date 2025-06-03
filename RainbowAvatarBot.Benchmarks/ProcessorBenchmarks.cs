using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using RainbowAvatarBot.Configuration;
using RainbowAvatarBot.Processors;
using RainbowAvatarBot.Services;
using SixLabors.ImageSharp;

namespace RainbowAvatarBot.Benchmarks;

[SimpleJob]
internal sealed class ProcessorBenchmarks
{
	private Stream _animatedStickerInput = null!;
	private Stream _imageStickerInput = null!;
	private Stream _videoStickerInput = null!;
	private AnimatedStickerProcessor _animatedStickerProcessor = null!;
	private ImageProcessor _imageStickerProcessor = null!;
	private VideoStickerProcessor _videoStickerProcessor = null!;

	[GlobalSetup]
	public async Task Setup()
	{
		_animatedStickerInput = new UnclosableMemoryStream();
		var content = await File.ReadAllBytesAsync(Path.Combine("TestData", "sticker.tgs"));
		_animatedStickerInput.Write(content);

		_imageStickerInput = new UnclosableMemoryStream();
		content = await File.ReadAllBytesAsync(Path.Combine("TestData", "sticker.webp"));
		_imageStickerInput.Write(content);

		_videoStickerInput = new UnclosableMemoryStream();
		content = await File.ReadAllBytesAsync(Path.Combine("TestData", "sticker.webm"));
		_videoStickerInput.Write(content);

		var memoryStreamManager = new RecyclableMemoryStreamManager();
		Configuration configuration;
		await using (var file = File.Open("appsettings.json", FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			configuration = (await JsonSerializer.DeserializeAsync<Configuration>(file))!;
		}

		var options = new OptionsWrapper<ProcessingConfiguration>(configuration.Processing);
		var images = new Dictionary<string, Image>();
		var initService = new InitializationHostedService(null!, null!, null!, images, options, null!);
		await initService.InitializeImages(CancellationToken.None);

		_animatedStickerProcessor = new AnimatedStickerProcessor(options, memoryStreamManager);
		_imageStickerProcessor = new ImageProcessor(new FlagImageService(images), memoryStreamManager);
		_videoStickerProcessor = new VideoStickerProcessor(memoryStreamManager);
	}

	[Params("LGBT")]
	[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
	public string FlagName { get; set; }

	[Benchmark]
	public Task ProcessAnimatedSticker()
	{
		_animatedStickerInput.Position = 0;

		return _animatedStickerProcessor.Process(_animatedStickerInput, FlagName, true);
	}

	[Benchmark]
	public Task ProcessImageSticker()
	{
		_imageStickerInput.Position = 0;

		return _imageStickerProcessor.Process(_imageStickerInput, FlagName, true);
	}

	[Benchmark]
	public Task ProcessVideoSticker()
	{
		_videoStickerInput.Position = 0;

		return _videoStickerProcessor.Process(_videoStickerInput, FlagName, true);
	}
}
