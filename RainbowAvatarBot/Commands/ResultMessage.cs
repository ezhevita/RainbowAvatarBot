using System;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace RainbowAvatarBot.Commands;

internal sealed record ResultMessage : IDisposable
{
	public ResultMessage(InputFile media, MediaType mediaType)
	{
		Media = media;
		MediaType = mediaType;
	}

	public ResultMessage(string text, bool isMarkdown = false, ReplyMarkup? markup = null)
	{
		Text = text;
		IsMarkdown = isMarkdown;
		Markup = markup;
	}

	public ReplyMarkup? Markup { get; }
	public InputFile? Media { get; }
	public MediaType? MediaType { get; }
	public string? Text { get; }
	public bool IsMarkdown { get; }

	public void Dispose()
	{
		if (Media is InputFileStream { Content: { } stream })
		{
			stream.Dispose();
		}
	}
}
