using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types.ReplyMarkups;

namespace RainbowAvatarBot;

internal static class Utilities
{
	public static bool IsMatchingCommand(string text, string command)
	{
		var textSpan = text.AsSpan();
		var index = textSpan.IndexOfAny(['@', ' ']);
		var inputCommand = index == -1 ? textSpan : textSpan[..index];

		if (!command.StartsWith('/'))
		{
			if (textSpan[0] != '/')
			{
				return false;
			}

			inputCommand = inputCommand[1..];
		}

		return inputCommand.Equals(command, StringComparison.OrdinalIgnoreCase);
	}

	public static InlineKeyboardMarkup BuildKeyboard(byte width, IEnumerable<InlineKeyboardButton> buttons,
		InlineKeyboardButton? buttonForLastRow = null)
	{
		var inlineKeyboardButtons = buttons.ToArray();
		var buttonCount = inlineKeyboardButtons.Length;
		var rowAmount = (uint) Math.Ceiling(buttonCount / (double) width);
		var remainder = buttonCount % width;
		var buttonRows = new InlineKeyboardButton[rowAmount + (buttonForLastRow != null ? 1 : 0)][];
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

		if (buttonForLastRow != null)
		{
			buttonRows[^1] = [buttonForLastRow];
		}

		return new InlineKeyboardMarkup(buttonRows);
	}

	public static InlineKeyboardMarkup BuildSettingsKeyboard(bool enableBlendModes)
	{
		var buttons = new List<InlineKeyboardButton>(
		[
			new InlineKeyboardButton(Localization.FlagSettings, "SETTINGS_FLAG"),
			new InlineKeyboardButton(Localization.OpacitySettings, "SETTINGS_OPACITY")
		]);

		if (enableBlendModes)
		{
			buttons.Add(new InlineKeyboardButton(Localization.BlendModeSettings, "SETTINGS_BLENDMODE"));
		}

		return new InlineKeyboardMarkup(buttons);
	}
}
