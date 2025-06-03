using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Commands;

internal interface ICommand
{
	bool CanExecute(Message message);

	Task<ResultMessage?> Execute(ITelegramBotClient botClient, Message message);
}
