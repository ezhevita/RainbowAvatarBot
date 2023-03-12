using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot;

public class ReceiverService
{
	private readonly ITelegramBotClient _botClient;
	private readonly IUpdateHandler _updateHandlers;

	public ReceiverService(
		ITelegramBotClient botClient,
		IUpdateHandler updateHandler)
	{
		_botClient = botClient;
		_updateHandlers = updateHandler;
	}

	public async Task ReceiveAsync(CancellationToken stoppingToken)
	{
		var receiverOptions = new ReceiverOptions
		{
			AllowedUpdates = new[] {UpdateType.Message, UpdateType.CallbackQuery},
			ThrowPendingUpdates = true
		};

		await _botClient.ReceiveAsync(
			_updateHandlers,
			receiverOptions,
			stoppingToken
		).ConfigureAwait(false);
	}
}
