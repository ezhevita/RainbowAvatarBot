using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using RainbowAvatarBot.Configuration;
using RainbowAvatarBot.Processors;
using RainbowAvatarBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot.Commands;

internal sealed partial class ColorizeCommand : ICommand
{
	private readonly UserSettingsService _userSettingsService;
	private readonly RateLimitingService _rateLimitingService;
	private readonly ProcessorHandler _processorHandler;
	private readonly IMemoryCache _memoryCache;
	private readonly RecyclableMemoryStreamManager _streamManager;
	private readonly IOptions<BotConfiguration> _botOptions;
	private readonly BotUserData _botUserData;
	private readonly ILogger<ColorizeCommand> _logger;

	public ColorizeCommand(UserSettingsService userSettingsService, RateLimitingService rateLimitingService,
		ProcessorHandler processorHandler, IMemoryCache memoryCache, RecyclableMemoryStreamManager streamManager,
		IOptions<BotConfiguration> botOptions, BotUserData botUserData, ILogger<ColorizeCommand> logger)
	{
		_userSettingsService = userSettingsService;
		_rateLimitingService = rateLimitingService;
		_processorHandler = processorHandler;
		_memoryCache = memoryCache;
		_streamManager = streamManager;
		_botOptions = botOptions;
		_botUserData = botUserData;
		_logger = logger;
	}

	public bool CanExecute(Message message)
	{
		const string Command = "/colorize";

		return message switch
		{
			{Type: MessageType.Text, ReplyToMessage.Type: MessageType.Photo or MessageType.Sticker, Text: { } text}
				when Utilities.IsMatchingCommand(text, Command) => true,
			{Type: MessageType.Photo or MessageType.Sticker, Caption: { } caption}
				when Utilities.IsMatchingCommand(caption, Command) => true,
			{Type: MessageType.Photo or MessageType.Sticker, Chat.Type: ChatType.Private} => true,
			_ => false
		};
	}

	public async Task<ResultMessage?> Execute(ITelegramBotClient botClient, Message message)
	{
		if (message is not {From.Id: var senderID})
		{
			return null;
		}

		var settings = _userSettingsService.GetSettingsForUser(senderID);
		var targetMessage = message switch
		{
			{Type: MessageType.Text, ReplyToMessage: {Type: MessageType.Photo or MessageType.Sticker} reply} => reply,
			{Type: MessageType.Photo or MessageType.Sticker} => message,
			_ => null
		};

		if (targetMessage is null)
		{
			return null;
		}

		FileBase? image = targetMessage switch
		{
			{Type: MessageType.Photo, Photo: {Length: > 0} photoSizes} =>
				photoSizes.MaxBy(size => size.Height)!,
			{Type: MessageType.Sticker, Sticker: { } sticker} => sticker,
			_ => null
		};

		if (image is null)
		{
			return null;
		}

		var mediaType = image switch
		{
			Sticker {IsAnimated: true} => MediaType.AnimatedSticker,
			Sticker {IsVideo: true} => MediaType.VideoSticker,
			Sticker => MediaType.Sticker,
			PhotoSize => MediaType.Picture,
			_ => throw new InvalidOperationException()
		};

		var cacheKey = new {image.FileUniqueId, settings};
		if (_memoryCache.TryGetValue<string>(cacheKey, out var fileId) && !string.IsNullOrEmpty(fileId))
		{
			return new ResultMessage(new InputFileId(fileId), mediaType);
		}

		var processor = _processorHandler.GetProcessor(mediaType);
		using var rateLimit = _rateLimitingService.TryEnter(senderID);
		if (rateLimit == null)
		{
			return null;
		}

		await botClient.SendChatAction(
			message.Chat,
			mediaType switch
			{
				MediaType.Picture => ChatAction.UploadPhoto, { } when mediaType.IsSticker() => ChatAction.ChooseSticker,
				_ => throw new InvalidOperationException()
			});

		var file = await botClient.GetFile(image.FileId);
		await using var stream = _streamManager.GetStream(nameof(ColorizeCommand), file.FileSize ?? 256 * 1024);
		await botClient.DownloadFile(file, stream);
		stream.Position = 0;

		var sw = Stopwatch.StartNew();
		var result = await processor.Process(stream, settings, mediaType.IsSticker());
		sw.Stop();
		LogProcessed(mediaType, sw.ElapsedMilliseconds, senderID, message.Chat.Id);

		return new ResultMessage(
			mediaType == MediaType.VideoSticker ? await ReplaceWithAddedSticker(botClient, result) : result, mediaType);
	}

	[LoggerMessage(LogLevel.Information, "Processed {MediaType} in {ElapsedMilliseconds}ms (sent by {UserId} in {ChatId})")]
	private partial void LogProcessed(MediaType mediaType, long elapsedMilliseconds, long userId, long chatId);

	// This is necessary in order to properly show video stickers on Telegram Desktop, otherwise it is displayed as a file.
	private async Task<InputFile> ReplaceWithAddedSticker(ITelegramBotClient botClient, InputFileStream file)
	{
		var stickerPackName = _botOptions.Value.PackNamePrefix + _botUserData.Username;

		await using (file.Content)
		{
			await botClient.AddStickerToSet(
				_botOptions.Value.OwnerId, stickerPackName, new InputSticker(file, StickerFormat.Video, ["📹"]));
		}

		var stickerSet = await botClient.GetStickerSet(stickerPackName);
		var newFileId = stickerSet.Stickers.Last().FileId;
		_ = Task.Run(() => DeleteStickerLater(botClient, newFileId));

		return new InputFileId(newFileId);
	}

	private static async Task DeleteStickerLater(ITelegramBotClient botClient, string fileId)
	{
		await Task.Delay(1000);
		await botClient.DeleteStickerFromSet(fileId);
	}
}
