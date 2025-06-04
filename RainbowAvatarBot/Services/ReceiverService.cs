using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot.Services;

internal class ReceiverService
{
	private readonly ITelegramBotClient _botClient;
	private readonly IUpdateHandler _updateHandler;

	public ReceiverService(ITelegramBotClient botClient, IUpdateHandler updateHandler)
	{
		_botClient = botClient;
		_updateHandler = updateHandler;
	}

	public async Task ReceiveAsync(CancellationToken stoppingToken)
	{
		var receiverOptions = new ReceiverOptions
		{
			AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
			DropPendingUpdates = true
		};

		await _botClient.ReceiveAsync(_updateHandler, receiverOptions, stoppingToken);
	}
}
