using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using RainbowAvatarBot.Processors;
using RainbowAvatarBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot.Commands;

internal sealed partial class AvatarCommand : ICommand
{
	private readonly UserSettingsService _userSettingsService;
	private readonly RateLimitingService _rateLimitingService;
	private readonly ProcessorHandler _processorHandler;
	private readonly IMemoryCache _memoryCache;
	private readonly RecyclableMemoryStreamManager _streamManager;
	private readonly ILogger<AvatarCommand> _logger;

	public AvatarCommand(UserSettingsService userSettingsService, RateLimitingService rateLimitingService,
		ProcessorHandler processorHandler, IMemoryCache memoryCache, RecyclableMemoryStreamManager streamManager, ILogger<AvatarCommand> logger)
	{
		_userSettingsService = userSettingsService;
		_rateLimitingService = rateLimitingService;
		_processorHandler = processorHandler;
		_memoryCache = memoryCache;
		_streamManager = streamManager;
		_logger = logger;
	}

	public bool CanExecute(Message message)
	{
		return message is { Type: MessageType.Text, Text: { } text } && Utilities.IsMatchingCommand(text, "/avatar");
	}

	public async Task<ResultMessage?> Execute(ITelegramBotClient botClient, Message message)
	{
		if (message is not { From.Id: var senderID })
		{
			return null;
		}

		var overlayName = _userSettingsService.GetFlagForUser(senderID);

		bool isReplied;
		long userIdForAvatars;
		if (message is { ReplyToMessage.From.Id: var repliedId })
		{
			isReplied = true;
			userIdForAvatars = repliedId;
		}
		else
		{
			isReplied = false;
			userIdForAvatars = senderID;
		}

		var avatars = await botClient.GetUserProfilePhotos(userIdForAvatars, limit: 1);
		if (avatars.Photos.Length == 0)
		{
			return new ResultMessage(
				isReplied ? Localization.RepliedUserProfilePictureNotFound : Localization.UserProfilePictureNotFound);
		}

		var sourceImage = avatars.Photos.Single().MaxBy(photo => photo.Height)!;
		if (_memoryCache.TryGetValue<string>(new { sourceImage.FileUniqueId, overlayName }, out var fileId) &&
			!string.IsNullOrEmpty(fileId))
		{
			return new ResultMessage(new InputFileId(fileId), MediaType.Picture);
		}

		var processor = _processorHandler.GetProcessor(MediaType.Picture);
		using var rateLimit = _rateLimitingService.TryEnter(senderID);
		if (rateLimit == null)
		{
			return null;
		}

		await botClient.SendChatAction(message.Chat, ChatAction.UploadPhoto);
		var file = await botClient.GetFile(sourceImage.FileId);
		await using var stream = _streamManager.GetStream(nameof(AvatarCommand), file.FileSize ?? 256 * 1024);
		await botClient.DownloadFile(file, stream);
		stream.Position = 0;

		var sw = Stopwatch.StartNew();
		var result = await processor.Process(stream, overlayName, false);

		sw.Stop();
		LogProcessed(sw.ElapsedMilliseconds);

		return new ResultMessage(result, MediaType.Picture);
	}

	[LoggerMessage(LogLevel.Information, "Processed avatar in {ElapsedMilliseconds}ms")]
	private partial void LogProcessed(long elapsedMilliseconds);
}
