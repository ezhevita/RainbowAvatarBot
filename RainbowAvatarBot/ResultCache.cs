using System.Collections.Concurrent;

namespace RainbowAvatarBot;

internal class ResultCache
{
	private readonly ConcurrentDictionary<(string fileID, string overlayName), string> _dictionary = new();

	public void Reset()
	{
		_dictionary.Clear();
	}

	public void TryAdd(string sourceId, string overlayName, string resultId)
	{
		_dictionary.TryAdd((sourceId, overlayName), resultId);
	}

	public bool TryGetValue(string sourceId, string overlayName, out string resultId) =>
		_dictionary.TryGetValue((sourceId, overlayName), out resultId);
}
