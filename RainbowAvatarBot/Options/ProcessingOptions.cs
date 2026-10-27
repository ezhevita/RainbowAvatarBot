using System.Collections.Generic;
using FluentValidation;

namespace RainbowAvatarBot.Configuration;

internal sealed record ProcessingOptions
{
	public required IReadOnlyDictionary<string, IReadOnlyList<uint>> Flags { get; init; }
	public required string GradientOverlay { get; init; }
	public required string ReferenceObject { get; init; }
}

internal sealed class ProcessingOptionsValidator : AbstractValidator<ProcessingOptions>
{
	public ProcessingOptionsValidator()
	{
		RuleFor(o => o.Flags)
			.NotEmpty()
			.ForEach(fr =>
				fr.ChildRules(fv =>
				{
					fv.RuleFor(f => f.Key).NotEmpty();
					fv.RuleFor(f => f.Value)
						.NotEmpty()
						.ForEach(c => c.LessThanOrEqualTo(0xFF_FF_FFU));
				}));

		RuleFor(o => o.GradientOverlay).NonNullJsonString();
		RuleFor(o => o.ReferenceObject).NonNullJsonString();
	}
}
