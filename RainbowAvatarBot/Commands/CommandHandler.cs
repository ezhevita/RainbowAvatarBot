using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RainbowAvatarBot.Commands;

internal sealed partial class CommandHandler
{
	private readonly ITelegramBotClient _botClient;
	private readonly IReadOnlyCollection<ICommand> _commands;
	private readonly ILogger<CommandHandler> _logger;

	public CommandHandler(ITelegramBotClient botClient, IEnumerable<ICommand> commands, ILogger<CommandHandler> logger)
	{
		_botClient = botClient;
		_commands = [.. commands];
		_logger = logger;
	}

	public async Task Execute(Message message)
	{
		var command = _commands.FirstOrDefault(cmd => cmd.CanExecute(message));
		if (command == null)
		{
			return;
		}

		ResultMessage? result;
		try
		{
			result = await command.Execute(_botClient, message);
		}
		catch (Exception e)
		{
			LogCommandExecutionError(e);
			throw new CommandExecutionException("Command execution failed.", e);
		}

		if (result == null)
		{
			return;
		}

		var replyParameters = new ReplyParameters { MessageId = message.Id };
		Task<Message?> task = (result switch
		{
			{ Text: { } text } => _botClient.SendMessage(message.Chat, text, ParseMode.MarkdownV2, replyParameters, result.Markup),
			{ Media: { } media, MediaType: MediaType.Picture } => _botClient.SendPhoto(
				message.Chat, media, replyParameters: replyParameters),
			{ Media: { } media, MediaType: { } mediaType } when mediaType.IsSticker() => _botClient.SendSticker(
				message.Chat, media, replyParameters),
			_ => Task.FromResult<Message?>(null)!
		})!;

		var response = await task;
		if (response == null)
		{
			return;
		}

		// Sometimes Telegram doesn't consider sent sticker a valid one (e.g. exceeding size limit).
		// The only way to detect is to check response message whether it has a sticker set.
		if (result is { MediaType: { } type } && type.IsSticker() && (response.Sticker == null))
		{
			await _botClient.DeleteMessage(response.Chat, response.MessageId);
			await _botClient.SendMessage(message.Chat.Id, Localization.UnableToSend, ParseMode.MarkdownV2, replyParameters);
		}
	}

	[LoggerMessage(LogLevel.Error, "An error occurred while executing a command.")]
	private partial void LogCommandExecutionError(Exception ex);
}
