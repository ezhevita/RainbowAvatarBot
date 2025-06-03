using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RainbowAvatarBot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RainbowAvatarBot.Commands;

internal sealed class SettingsCommand : ICommand
{
	private readonly FlagImageService _flagImageService;

	public SettingsCommand(FlagImageService flagImageService)
	{
		_flagImageService = flagImageService;
	}

	public bool CanExecute(Message message)
	{
		return message is
		{ Chat.Type: ChatType.Private or ChatType.Group or ChatType.Supergroup, Type: MessageType.Text, Text: { } text } &&
			Utilities.IsMatchingCommand(text, "/settings");
	}

	public Task<ResultMessage?> Execute(ITelegramBotClient botClient, Message message)
	{
		if (message.Chat.Type is ChatType.Group or ChatType.Supergroup)
		{
			return Task.FromResult(new ResultMessage(Localization.SettingsSentToChat))!;
		}

		var markup = BuildKeyboard(
			3,
			_flagImageService.GetFlagNames()
#pragma warning disable CA1304 // Specify CultureInfo -- it is set per thread
				.Select(name => new InlineKeyboardButton(Localization.ResourceManager.GetString(name)!)
#pragma warning restore CA1304 // Specify CultureInfo
				{ CallbackData = "SETTINGS_" + name })
				.OrderBy(button => button.Text));

		return Task.FromResult(new ResultMessage(Localization.SelectFlag, markup))!;
	}

	private static InlineKeyboardMarkup BuildKeyboard(byte width, IEnumerable<InlineKeyboardButton> buttons)
	{
		var inlineKeyboardButtons = buttons.ToArray();
		var buttonCount = inlineKeyboardButtons.Length;
		var rowAmount = (uint)Math.Ceiling(buttonCount / (double)width);
		var remainder = buttonCount % width;
		var buttonRows = new InlineKeyboardButton[rowAmount][];
		for (var row = 0; row < rowAmount; row++)
		{
			var columnAmount = (rowAmount == row + 1) && (remainder > 0) ? remainder : width;
			var rowButtons = new InlineKeyboardButton[columnAmount];
			for (var column = 0; column < columnAmount; column++)
			{
				rowButtons[column] = inlineKeyboardButtons[row * width + column];
			}

			buttonRows[row] = rowButtons;
		}

		return new InlineKeyboardMarkup(buttonRows);
	}
}
