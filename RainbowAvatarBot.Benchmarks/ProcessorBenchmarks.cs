using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RainbowAvatarBot.Processors;

namespace RainbowAvatarBot.Benchmarks;

[SimpleJob(RuntimeMoniker.Net70)]
public class ProcessorBenchmarks
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
		var content = await File.ReadAllBytesAsync("sticker.tgs");
		_animatedStickerInput.Write(content);

		_imageStickerInput = new UnclosableMemoryStream();
		content = await File.ReadAllBytesAsync("sticker.webp");
		_imageStickerInput.Write(content);

		_videoStickerInput = new UnclosableMemoryStream();
		content = await File.ReadAllBytesAsync("sticker.webm");
		_videoStickerInput.Write(content);

		var gradientOverlay = await File.ReadAllTextAsync("gradientOverlay.json");

		const string ReferenceObject = "{\"ind\":1,\"ty\":0,\"refId\":\"_\",\"sr\":1,\"ks\":{\"o\":{\"a\":0,\"k\":100},\"r\":" +
			"{\"a\":0,\"k\":0},\"p\":{\"k\":[256,256,0]},\"a\":{\"k\":[256,256,0]}},\"w\":512,\"h\":512}";

		var memoryStreamManager = new RecyclableMemoryStreamManager();
		_animatedStickerProcessor = new AnimatedStickerProcessor(
			memoryStreamManager,
			new AnimatedStickerHelperData(JObject.Parse(gradientOverlay), JObject.Parse(ReferenceObject))
		);

		_imageStickerProcessor = new ImageProcessor(memoryStreamManager);
		_videoStickerProcessor = new VideoStickerProcessor(memoryStreamManager);

		var flagsContent = await File.ReadAllTextAsync("flags.json5");

		var flags = JsonConvert.DeserializeObject<Dictionary<string, IReadOnlyCollection<uint>>>(flagsContent);

		Directory.CreateDirectory("images");

		await _imageStickerProcessor.Init(flags);
		await Task.WhenAll(_animatedStickerProcessor.Init(flags), _videoStickerProcessor.Init(flags));
	}

	[Params("LGBT", "Agender", "Genderqueer")]
	public string FlagName { get; set; }

	[Benchmark]
	public Task ProcessAnimatedSticker()
	{
		_animatedStickerInput.Position = 0;

		return _animatedStickerProcessor.Process(_animatedStickerInput, FlagName, MediaType.AnimatedSticker);
	}

	[Benchmark]
	public Task ProcessImageSticker()
	{
		_imageStickerInput.Position = 0;

		return _imageStickerProcessor.Process(_imageStickerInput, FlagName, MediaType.Sticker);
	}

	[Benchmark]
	public Task ProcessVideoSticker()
	{
		_videoStickerInput.Position = 0;

		return _videoStickerProcessor.Process(_videoStickerInput, FlagName, MediaType.VideoSticker);
	}
}
