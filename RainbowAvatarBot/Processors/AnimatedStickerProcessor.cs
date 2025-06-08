using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using RainbowAvatarBot.Configuration;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

internal class AnimatedStickerProcessor : IProcessor
{
	private readonly JsonObject _gradientOverlay;
	private readonly JsonObject _referenceObject;
	private readonly RecyclableMemoryStreamManager _memoryStreamManager;
	private readonly FrozenDictionary<string, float[]> _flagGradients;
	private readonly FrozenDictionary<string, int> _colorsCount;

	public AnimatedStickerProcessor(IOptions<ProcessingConfiguration> options, RecyclableMemoryStreamManager memoryStreamManager)
	{
		var gradientOverlay = JsonNode.Parse(options.Value.GradientOverlay);
		if (gradientOverlay is null)
		{
			throw new ArgumentException("Gradient overlay is not valid.", nameof(options));
		}

		var referenceObject = JsonNode.Parse(options.Value.ReferenceObject);
		if (referenceObject is null)
		{
			throw new ArgumentException("Reference object is not valid.", nameof(options));
		}

		_gradientOverlay = gradientOverlay.AsObject();
		_referenceObject = referenceObject.AsObject();
		_memoryStreamManager = memoryStreamManager;

		var flags = options.Value.Flags;
		var flagGradients = flags.ToFrozenDictionary(x => x.Key, x => GenerateGradient(x.Value));
		var colorsCount = flags.ToFrozenDictionary(x => x.Key, x => x.Value.Count);

		_flagGradients = flagGradients;
		_colorsCount = colorsCount;
	}

	public IEnumerable<MediaType> SupportedMediaTypes => [MediaType.AnimatedSticker];

	public async Task<InputFileStream> Process(Stream input, UserSettings settings, bool isSticker)
	{
		if (!isSticker)
		{
			throw new ArgumentException("Expected input to be a sticker.", nameof(isSticker));
		}

		JsonNode? stickerObject;
		await using (GZipStream gzStream = new(input, CompressionMode.Decompress, true))
		{
			stickerObject = await JsonNode.ParseAsync(gzStream);
		}

		if (stickerObject == null)
		{
			throw new ArgumentException("Invalid sticker object.", nameof(input));
		}

		var processedAnimation = ProcessLottieAnimation(stickerObject.AsObject(), settings);

		var result = await PackAnimatedSticker(processedAnimation, input.Length);

		return new InputFileStream(result, "sticker.tgs");
	}

	private static float[] GenerateGradient(IReadOnlyCollection<uint> rgbValues)
	{
		var gradientProps = new float[(rgbValues.Count * 2 - 2) * 4];
		byte index = 0;
		foreach (var rgbValue in rgbValues)
		{
			if (index > 0)
			{
				FillLine((byte)((index << 3) - 4), 0);
			}

			if (index < rgbValues.Count - 1)
			{
				FillLine((byte)(index << 3), 1);
			}

			index++;

			continue;

			void FillLine(byte startIndex, byte indexInc)
			{
				gradientProps[startIndex] = (float)Math.Round((float)(index + indexInc) / rgbValues.Count, 3);
				gradientProps[startIndex + 1] = (float)Math.Round((rgbValue >> 16) / 255.0, 3);
				gradientProps[startIndex + 2] = (float)Math.Round(((rgbValue >> 8) & 0xFF) / 255.0, 3);
				gradientProps[startIndex + 3] = (float)Math.Round((rgbValue & 0xFF) / 255.0, 3);
			}
		}

		return gradientProps;
	}

	private async Task<Stream> PackAnimatedSticker(JsonObject content, long length)
	{
		var resultStream = _memoryStreamManager.GetStream(nameof(AnimatedStickerProcessor), length);

		// SharpZipLib is used instead of native GZipStream because it provides better compression
		await using (GZipOutputStream gzipOutput = new(resultStream))
		{
			gzipOutput.SetLevel(9);
			gzipOutput.IsStreamOwner = false;
			await JsonSerializer.SerializeAsync(gzipOutput, content);
		}

		resultStream.Position = 0;
		return resultStream;
	}

	private JsonObject ProcessLottieAnimation(JsonObject tokenizedSticker, UserSettings settings)
	{
		var layersToken = tokenizedSticker["layers"]?.AsArray() ?? throw new InvalidOperationException("Missing layers");
		var assetsToken = tokenizedSticker["assets"]?.AsArray() ?? throw new InvalidOperationException("Missing assets");

		var rgbValuesLength = _colorsCount[settings.FlagName];

		// Packing main animation to asset
		JsonNode assetToken = new JsonObject
		{
			["id"] = "_",
			["layers"] = layersToken.DeepClone()
		};

		// Getting last frame
		var lastFrame = (tokenizedSticker["op"] ?? throw new InvalidOperationException("Missing frame count")).GetValue<ushort>();

		layersToken.Clear();
		assetsToken.Add(assetToken);

		// Layer that reference main animation from assets
		var clonedReferenceLayerObject = _referenceObject.DeepClone();
		clonedReferenceLayerObject["op"] = lastFrame;
		layersToken.Add(clonedReferenceLayerObject);

		// Overlaying gradient
		var gradientOverlayObject = _gradientOverlay.DeepClone();
		gradientOverlayObject["op"] = lastFrame;
		gradientOverlayObject["ks"]!["o"]!["k"] = settings.Opacity;

		var gFillObject = (gradientOverlayObject["shapes"]?[0]?["it"]?[2]?["g"] ??
			throw new InvalidOperationException("Missing fill object")).AsObject();

		gFillObject["p"] = rgbValuesLength * 2 - 2;

		var gradientProps = _flagGradients[settings.FlagName];

		gFillObject["k"]!["k"] = gradientProps.ToJsonArray();
		layersToken.Add(gradientOverlayObject);

		var clonedReferenceObject = clonedReferenceLayerObject.DeepClone();
		clonedReferenceObject["ind"] = 3;
		layersToken.Add(clonedReferenceObject);

		return tokenizedSticker;
	}
}
