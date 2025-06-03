using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RainbowAvatarBot.Commands;
using RainbowAvatarBot.Configuration;
using RainbowAvatarBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot;

internal sealed partial class Bot
{
	private readonly ITelegramBotClient _telegramBotClient;
	private readonly CommandHandler _commandHandler;
	private readonly FlagImageService _flagImageService;
	private readonly UserSettingsService _userSettingsService;
	private readonly ILogger<Bot> _logger;
	private readonly BotUserData _botUserData;
	private readonly BotConfiguration _botConfiguration;

	public Bot(ITelegramBotClient telegramBotClient, CommandHandler commandHandler, FlagImageService flagImageService,
		UserSettingsService userSettingsService, IOptions<BotConfiguration> botConfiguration, ILogger<Bot> logger,
		BotUserData botUserData)
	{
		_telegramBotClient = telegramBotClient;
		_commandHandler = commandHandler;
		_flagImageService = flagImageService;
		_userSettingsService = userSettingsService;
		_logger = logger;
		_botUserData = botUserData;
		_botConfiguration = botConfiguration.Value;
	}

	public async Task Init()
	{
		if (!await _telegramBotClient.TestApi())
		{
			throw new InvalidOperationException("Error when starting bot!");
		}

		var me = await _telegramBotClient.GetMe();

		_botUserData.Username = me.Username!;

		var stickerPackName = _botConfiguration.PackNamePrefix + _botUserData.Username;
		try
		{
			await _telegramBotClient.GetStickerSet(stickerPackName);
		}
		catch (ApiRequestException)
		{
			try
			{
				using var memoryStream = new MemoryStream();
				const string EmptyImage =
					"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEh" +
					"ZcwAADsMAAA7DAcdvqGQAAAAMSURBVBhXY2BgYAAAAAQAAVzN/2kAAAAASUVORK5CYII=";

				memoryStream.Write(Convert.FromBase64String(EmptyImage));
				await _telegramBotClient.CreateNewStickerSet(
					_botConfiguration.OwnerId,
					_botConfiguration.PackNamePrefix + _botUserData.Username,
					"Created by @" + _botUserData.Username,
					[
						new InputSticker(
							new InputFileStream(memoryStream, "placeholder.png"), StickerFormat.Static, ["🌈", "🏳️‍🌈"])
					]
				);
			}
			catch
			{
				// ignored
			}
		}
	}

	public async Task OnCallbackQuery(CallbackQuery callbackQuery)
	{
		SetThreadLocale(callbackQuery.From.LanguageCode);

		if (callbackQuery is not { Id: { } callbackId, From.Id: var senderId, Data: { } data, Message: { } message })
		{
			return;
		}

		var indexOfUnderscore = data.IndexOf('_', StringComparison.Ordinal);
		if (indexOfUnderscore == -1)
		{
			return;
		}

		if (!data.AsSpan()[..indexOfUnderscore].Equals("settings", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var name = data[(indexOfUnderscore + 1)..];
		if (!_flagImageService.IsValidFlagName(name))
		{
			await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.InvalidFlagName);

			return;
		}

		_userSettingsService.SetFlagForUser(senderId, name);
		await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.Success);
	}

	public async Task OnMessage(Message message)
	{
		if (message is not { From: not null, Chat.Id: var chatID })
		{
			return;
		}

		SetThreadLocale(message.From.LanguageCode);
		try
		{
			await _commandHandler.Execute(message);
		}
		catch (Exception e)
		{
			if (e is not CommandExecutionException)
			{
				LogException(e);
			}

			try
			{
				await _telegramBotClient.SendMessage(chatID, Localization.ErrorOccured, replyParameters: new ReplyParameters { MessageId = message.Id });
			}
			catch
			{
				// ignored
			}
		}
	}

	[LoggerMessage(LogLevel.Error, "An error occurred while handling the message.")]
	private partial void LogException(Exception ex);

	private static void SetThreadLocale(string? languageCode)
	{
		Thread.CurrentThread.CurrentUICulture = languageCode switch
		{
			"be" or "uk" or "ru" or "" or null => CultureInfo.GetCultureInfoByIetfLanguageTag("ru-RU"),
			_ => CultureInfo.GetCultureInfoByIetfLanguageTag(languageCode)
		};
	}
}
