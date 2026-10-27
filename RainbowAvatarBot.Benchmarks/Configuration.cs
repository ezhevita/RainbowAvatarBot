using RainbowAvatarBot.Configuration;

namespace RainbowAvatarBot.Benchmarks;

internal sealed record Configuration
{
	public required ProcessingOptions Processing { get; init; }
}
