using System;

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
}
