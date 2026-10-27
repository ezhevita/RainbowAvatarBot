using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RainbowAvatarBot.Commands;

internal sealed class SettingsCommand : ICommand
{
	private readonly InlineKeyboardMarkup _keyboard = Utilities.BuildSettingsKeyboard();

	public bool CanExecute(Message message)
	{
		return message is
				{Chat.Type: ChatType.Private or ChatType.Group or ChatType.Supergroup, Type: MessageType.Text, Text: { } text} &&
			Utilities.IsMatchingCommand(text, "/settings");
	}

	public Task<ResultMessage?> Execute(ITelegramBotClient botClient, Message message)
	{
		if (message.Chat.Type is ChatType.Group or ChatType.Supergroup)
		{
			return Task.FromResult(new ResultMessage(Localization.SettingsSentToChat))!;
		}

		return Task.FromResult(new ResultMessage(Localization.SelectSettingToChange, markup: _keyboard))!;
	}
}
