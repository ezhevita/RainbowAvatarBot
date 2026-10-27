using FluentValidation;

namespace RainbowAvatarBot.Configuration;

internal sealed class BotOptions
{
	public required string Token { get; init; }
	public required long OwnerId { get; init; }
	public required string PackNamePrefix { get; init; }
}

internal sealed class BotOptionsValidator : AbstractValidator<BotOptions>
{
	public BotOptionsValidator()
	{
		RuleFor(o => o.Token).TelegramBotToken();
		RuleFor(o => o.OwnerId).TelegramUserId();
		RuleFor(o => o.PackNamePrefix).NotEmpty();
	}
}
