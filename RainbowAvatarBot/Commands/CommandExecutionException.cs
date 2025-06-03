using System;
using System.Diagnostics.CodeAnalysis;

namespace RainbowAvatarBot.Commands;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors")]
internal sealed class CommandExecutionException : Exception
{
	public CommandExecutionException(string message, Exception innerException) : base(message, innerException)
	{
	}
}
