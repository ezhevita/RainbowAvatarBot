using System;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace RainbowAvatarBot.Commands;

internal sealed record ResultMessage : IDisposable
{
	public ResultMessage(string text)
	{
		Text = text;
	}

	public ResultMessage(InputFile media, MediaType mediaType)
	{
		Media = media;
		MediaType = mediaType;
	}

	public ResultMessage(string text, ReplyMarkup markup)
	{
		Markup = markup;
		Text = text;
	}

	public ReplyMarkup? Markup { get; }
	public InputFile? Media { get; }
	public MediaType? MediaType { get; }
	public string? Text { get; }

	public void Dispose()
	{
		if (Media is InputFileStream { Content: { } stream })
		{
			stream.Dispose();
		}
	}
}
