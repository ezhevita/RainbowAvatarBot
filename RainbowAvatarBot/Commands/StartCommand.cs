using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot.Commands;

internal sealed class StartCommand : ICommand
{
	public bool CanExecute(Message message) => message is {Chat.Type: ChatType.Private, Text: { } text} &&
		Utilities.IsMatchingCommand(text, "/start");

	public Task<ResultMessage?> Execute(ITelegramBotClient botClient, Message message) =>
		Task.FromResult(new ResultMessage(Localization.StartMessage, true))!;
}
