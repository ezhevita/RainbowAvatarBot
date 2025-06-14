using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
using Telegram.Bot.Types.ReplyMarkups;

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

	// TODO: refactor as callback handlers
	public async Task OnCallbackQuery(CallbackQuery callbackQuery)
	{
		SetThreadLocale(callbackQuery.From.LanguageCode);

		if (callbackQuery is not { Id: { } callbackId, From.Id: var senderId, Data: { } data, Message.Id: { } messageId })
		{
			return;
		}

		if (data.Equals("SETTINGS", StringComparison.OrdinalIgnoreCase))
		{
			await _telegramBotClient.EditMessageText(
				senderId, messageId, Localization.SelectSettingToChange,
				replyMarkup: Utilities.BuildSettingsKeyboard(_botConfiguration.EnableBlendModeSettings));
			await _telegramBotClient.AnswerCallbackQuery(callbackId);

			return;
		}

		var callbackArguments = data.Split('_');
		if (callbackArguments.Length < 2 || !callbackArguments[0].Equals("settings", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		var additionalArgument = callbackArguments.Length > 2 ? callbackArguments[2] : null;

		try
		{
			var processTask = callbackArguments[1].ToUpperInvariant() switch
			{
				"BLENDMODE" => ProcessBlendModeCallback(senderId, messageId, callbackId, additionalArgument),
				"FLAG" => ProcessFlagCallback(senderId, messageId, callbackId, additionalArgument),
				"OPACITY" => ProcessOpacityCallback(senderId, messageId, callbackId, additionalArgument),
				_ => _telegramBotClient.AnswerCallbackQuery(callbackId)
			};

			await processTask;
		} catch (Exception e)
		{
			LogCallbackException(e);
		}
	}

	private async Task ProcessBlendModeCallback(long userId, int messageId, string callbackId, string? argument)
	{
		if (argument != null)
		{
			if (!Enum.TryParse<BlendMode>(argument, true, out var blendMode) || !Enum.IsDefined(blendMode))
			{
				await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.ErrorOccured);
				return;
			}

			_userSettingsService.SetBlendModeForUser(userId, blendMode);
			await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.Success);
			return;
		}

		var markup = Utilities.BuildKeyboard(
			3,
			Enum.GetNames<BlendMode>()
				.Select(name => new InlineKeyboardButton(name, $"SETTINGS_BLENDMODE_{name}")),
			new InlineKeyboardButton(Localization.GoBack, "SETTINGS"));

		await _telegramBotClient.EditMessageText(userId, messageId, Localization.SelectBlendMode, replyMarkup: markup);
		await _telegramBotClient.AnswerCallbackQuery(callbackId);
	}

	private async Task ProcessFlagCallback(long userId, int messageId, string callbackId, string? argument)
	{
		if (argument != null)
		{
			if (!_flagImageService.IsValidFlagName(argument))
			{
				await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.ErrorOccured);
				return;
			}

			_userSettingsService.SetFlagForUser(userId, argument);
			await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.Success);
			return;
		}

		var markup = Utilities.BuildKeyboard(
			3,
			_flagImageService.GetFlagNames()
#pragma warning disable CA1304 // Specify CultureInfo -- it is set per-thread
				.Select(name => new InlineKeyboardButton(Localization.ResourceManager.GetString(name)!, $"SETTINGS_FLAG_{name}"))
#pragma warning restore CA1304 // Specify CultureInfo
				.OrderBy(button => button.Text),
			new InlineKeyboardButton(Localization.GoBack, "SETTINGS"));

		await _telegramBotClient.EditMessageText(userId, messageId, Localization.SelectFlag, replyMarkup: markup);
		await _telegramBotClient.AnswerCallbackQuery(callbackId);
	}

	private async Task ProcessOpacityCallback(long userId, int messageId, string callbackId, string? argument)
	{
		if (argument != null)
		{
			if (!byte.TryParse(argument, out var opacity) || opacity is >= 100 or <= 0)
			{
				await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.ErrorOccured);

				return;
			}

			_userSettingsService.SetOpacityForUser(userId, opacity);
			await _telegramBotClient.AnswerCallbackQuery(callbackId, Localization.Success);

			return;
		}

		var markup = Utilities.BuildKeyboard(
			3,
			Enumerable.Range(1, 9).Select(x => x * 10)
				.Select(opacity => new InlineKeyboardButton($"{opacity}%", $"SETTINGS_OPACITY_{opacity}")),
			new InlineKeyboardButton(Localization.GoBack, "SETTINGS"));

		await _telegramBotClient.EditMessageText(userId, messageId, Localization.SelectOpacity, replyMarkup: markup);
		await _telegramBotClient.AnswerCallbackQuery(callbackId);
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

	[LoggerMessage(LogLevel.Error, "An error occurred while handling the callback query.")]
	private partial void LogCallbackException(Exception ex);

	private static void SetThreadLocale(string? languageCode)
	{
		Thread.CurrentThread.CurrentUICulture = languageCode switch
		{
			"be" or "uk" or "ru" or "" or null => CultureInfo.GetCultureInfoByIetfLanguageTag("ru-RU"),
			_ => CultureInfo.GetCultureInfoByIetfLanguageTag(languageCode)
		};
	}
}
