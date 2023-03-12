using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

public class AnimatedStickerProcessor : IProcessor
{
	private static JObject _gradientOverlay;
	private static JObject _referenceObject;
	private static IReadOnlyDictionary<string, float[]> _flagGradients;
	private static IReadOnlyDictionary<string, int> _colorsCount;

	private readonly RecyclableMemoryStreamManager _memoryStreamManager;

	public AnimatedStickerProcessor(RecyclableMemoryStreamManager memoryStreamManager, AnimatedStickerHelperData helperData)
	{
		_memoryStreamManager = memoryStreamManager;
		_gradientOverlay = helperData.GradientOverlay;
		_referenceObject = helperData.ReferenceObject;
	}

	public bool CanProcessMediaType(MediaType mediaType) => mediaType == MediaType.AnimatedSticker;

	public async Task<InputMedia> Process(Stream input, string overlayName, MediaType mediaType)
	{
		JObject stickerObject;
		await using (GZipStream gzStream = new(input, CompressionMode.Decompress))
		{
			using StreamReader reader = new(gzStream);
			using JsonTextReader jsonReader = new(reader);

			stickerObject = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
		}

		var processedAnimation = ProcessLottieAnimation(stickerObject, overlayName);

		var result = await PackAnimatedSticker(processedAnimation).ConfigureAwait(false);

		return new InputMedia(result, "sticker.tgs");
	}

	public Task Init(IReadOnlyDictionary<string, IReadOnlyCollection<uint>> flagsData)
	{
		_flagGradients = flagsData.ToDictionary(x => x.Key, x => GenerateGradient(x.Value));
		_colorsCount = flagsData.ToDictionary(x => x.Key, x => x.Value.Count);

		return Task.CompletedTask;
	}

	private static float[] GenerateGradient(IReadOnlyCollection<uint> rgbValues)
	{
		var gradientProps = new float[(rgbValues.Count * 2 - 2) * 4];
		byte index = 0;
		foreach (var rgbValue in rgbValues)
		{
			void FillLine(byte startIndex, byte indexInc)
			{
				gradientProps[startIndex] = (float) Math.Round((float) (index + indexInc) / rgbValues.Count, 3);
				gradientProps[startIndex + 1] = (float) Math.Round((rgbValue >> 16) / 255.0, 3);
				gradientProps[startIndex + 2] = (float) Math.Round(((rgbValue >> 8) & 0xFF) / 255.0, 3);
				gradientProps[startIndex + 3] = (float) Math.Round((rgbValue & 0xFF) / 255.0, 3);
			}

			if (index > 0)
			{
				FillLine((byte) ((index << 3) - 4), 0);
			}

			if (index < rgbValues.Count - 1)
			{
				FillLine((byte) (index << 3), 1);
			}

			index++;
		}

		return gradientProps;
	}

	private async Task<Stream> PackAnimatedSticker(JToken content)
	{
		await using var memoryStream = _memoryStreamManager.GetStream("IntermediateForAnimatedSticker", 640 * 1024);
		var resultStream = _memoryStreamManager.GetStream("ResultForAnimatedSticker", 64 * 1024);

		await using (StreamWriter streamWriter = new(memoryStream, leaveOpen: true))
		using (JsonTextWriter jsonTextWriter = new(streamWriter)
		       {
			       Formatting = Formatting.None,
			       AutoCompleteOnClose = true,
			       CloseOutput = false
		       })
		{
			await content.WriteToAsync(jsonTextWriter).ConfigureAwait(false);
		}

		memoryStream.Position = 0;

		await using (GZipOutputStream gzipOutput = new(resultStream))
		{
			gzipOutput.SetLevel(9);
			gzipOutput.IsStreamOwner = false;
			await memoryStream.CopyToAsync(gzipOutput).ConfigureAwait(false);
		}

		resultStream.Position = 0;

		return resultStream;
	}

	private static JObject ProcessLottieAnimation(JObject tokenizedSticker, string overlayName)
	{
		var layersToken = (JArray) tokenizedSticker["layers"];
		var assetsToken = (JArray) tokenizedSticker["assets"];

		var rgbValuesLength = _colorsCount[overlayName];

		// Packing main animation to asset
		JObject assetToken = new()
		{
			["id"] = "_",
			["layers"] = layersToken
		};

		// Getting last frame
		var lastFrame = tokenizedSticker["op"].Value<ushort>();

		layersToken.RemoveAll();
		assetsToken.Add(assetToken);

		// Layer that reference main animation from assets
		var clonedReferenceLayerObject = _referenceObject.DeepClone();
		clonedReferenceLayerObject["op"] = lastFrame;
		layersToken.Add(clonedReferenceLayerObject);

		// Overlaying gradient
		var gradientOverlayObject = (JObject) _gradientOverlay.DeepClone();
		gradientOverlayObject["op"] = lastFrame;
		var gFillObject = (JObject) gradientOverlayObject["shapes"][0]["it"][2]["g"];
		gFillObject["p"] = rgbValuesLength * 2 - 2;

		var gradientProps = _flagGradients[overlayName];

		gFillObject["k"]["k"] = new JArray(gradientProps);
		layersToken.Add(gradientOverlayObject);

		var clonedReferenceObject = clonedReferenceLayerObject.DeepClone();
		clonedReferenceObject["ind"] = 3;
		layersToken.Add(clonedReferenceObject);

		return tokenizedSticker;
	}
}
