using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RainbowAvatarBot.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot.Services;

internal sealed class InitializationHostedService : IHostedService
{
	private readonly PngEncoder _pngEncoder = new()
	{
		ColorType = PngColorType.Rgb,
		CompressionLevel = PngCompressionLevel.NoCompression
	};

	private readonly IOptions<ProcessingConfiguration> _processingOptions;
	private readonly IOptions<BotConfiguration> _botOptions;
	private readonly ITelegramBotClient _botClient;
	private readonly UserSettingsService _settingsService;
	private readonly BotUserData _botUserData;
	private readonly Dictionary<string, Image> _flagImages;

	public InitializationHostedService(ITelegramBotClient botClient, UserSettingsService settingsService, BotUserData botUserData, Dictionary<string, Image> flagImages, IOptions<ProcessingConfiguration> processingOptions, IOptions<BotConfiguration> botOptions)
	{
		_botClient = botClient;
		_settingsService = settingsService;
		_botUserData = botUserData;
		_flagImages = flagImages;
		_processingOptions = processingOptions;
		_botOptions = botOptions;
	}

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (!await _botClient.TestApi(cancellationToken))
		{
			throw new InvalidOperationException("Error when starting the bot!");
		}

		_botUserData.Username = (await _botClient.GetMe(cancellationToken: cancellationToken)).Username!;

		await Task.WhenAll(
			InitializeStickerSet(cancellationToken), InitializeImages(cancellationToken), _settingsService.Initialize());
	}

	internal async Task InitializeImages(CancellationToken cancellationToken)
	{
		if (!Directory.Exists("images"))
		{
			Directory.CreateDirectory("images");
		}

		var existingFiles = Directory.EnumerateFiles("images", "*.png")
			.Select(Path.GetFileNameWithoutExtension)
			.ToHashSet();

		var flags = _processingOptions.Value.Flags;
		var generateTasks = flags.Where(x => !existingFiles.Contains(x.Key))
			.Select(x => GenerateImage(x.Key, x.Value, cancellationToken));

		await Task.WhenAll(generateTasks);

		var imageLoadTasks = flags.Keys.Select(x => Image.LoadAsync(Path.Combine("images", x + ".png"), cancellationToken));
		var images = await Task.WhenAll(imageLoadTasks);

		foreach (var (name, image) in flags.Keys.Zip(images))
		{
			_flagImages.Add(name, image);
		}
	}

	private async Task InitializeStickerSet(CancellationToken cancellationToken)
	{
		var botOptions = _botOptions.Value;
		var stickerPackName = botOptions.PackNamePrefix + _botUserData.Username;
		try
		{
			await _botClient.GetStickerSet(stickerPackName, cancellationToken: cancellationToken);
		}
		catch (ApiRequestException)
		{
			try
			{
				await using var fileStream = File.Open(
					Path.Combine("Assets", "sticker.webp"), FileMode.Open, FileAccess.Read, FileShare.Read);

				var sticker = new InputSticker(
					new InputFileStream(fileStream, "sticker.webp"), StickerFormat.Static, ["🌈", "🏳️‍🌈"]);

				await _botClient.CreateNewStickerSet(
					botOptions.OwnerId, stickerPackName, "Created by @" + _botUserData.Username, [sticker],
					cancellationToken: cancellationToken);
			}
			catch
			{
				// ignored
			}
		}
	}

	private async Task GenerateImage(string name, IReadOnlyList<uint> colors, CancellationToken cancellationToken)
	{
		using var image = new Image<Rgba32>(1, colors.Count);
		for (var i = 0; i < colors.Count; i++)
		{
			var color = colors[i];
			var argbValue = new Argb32((byte)(color >> 16), (byte)((color >> 8) & 0xFF), (byte)(color & 0xFF));
			var shape = new RectangleF(0, i, 1, 1);

			image.Mutate(img => img.Fill(argbValue, shape));
		}

		await image.SaveAsPngAsync(Path.Combine("images", name + ".png"), _pngEncoder, cancellationToken: cancellationToken);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
