using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot;

public partial class UpdateHandler : IUpdateHandler
{
	private readonly Bot _bot;
	private readonly ILogger<UpdateHandler> _logger;

	public UpdateHandler(ILogger<UpdateHandler> logger, Bot bot)
	{
		_logger = logger;
		_bot = bot;
	}

	public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
	{
		return update.Type switch
		{
			UpdateType.Message => _bot.OnMessage(update.Message!),
			UpdateType.CallbackQuery => _bot.OnCallbackQuery(update.CallbackQuery!),
			_ => Task.CompletedTask
		};
	}

	public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
	{
		LogPollingError(exception);

		return Task.CompletedTask;
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Polling error occured with an exception")]
	private partial void LogPollingError(Exception ex);
}
