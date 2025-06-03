using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using SixLabors.ImageSharp;

namespace RainbowAvatarBot.Services;

internal sealed class FlagImageService : IDisposable
{
	private readonly Lazy<FrozenDictionary<string, Image>> _images;

	public FlagImageService(Dictionary<string, Image> images)
	{
		_images = new Lazy<FrozenDictionary<string, Image>>(() => images.ToFrozenDictionary());
	}

	public IEnumerable<string> GetFlagNames() => _images.Value.Keys;

	public bool IsValidFlagName(string flagName) => _images.Value.ContainsKey(flagName);

	public Image GetFlag(string flagName) => _images.Value[flagName];

	public void Dispose()
	{
		foreach (var image in _images.Value.Values)
		{
			image.Dispose();
		}
	}
}
