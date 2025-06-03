using System.Collections.Generic;

namespace RainbowAvatarBot.Configuration;

internal sealed record ProcessingConfiguration
{
	public required IReadOnlyDictionary<string, IReadOnlyList<uint>> Flags { get; init; }
	public required string GradientOverlay { get; init; }
	public required string ReferenceObject { get; init; }
}
