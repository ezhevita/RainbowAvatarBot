using RainbowAvatarBot.Configuration;

namespace RainbowAvatarBot.Benchmarks;

internal sealed record Configuration
{
	public required ProcessingConfiguration Processing { get; init; }
}
