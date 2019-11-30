using System.Collections.Concurrent;

namespace RainbowAvatarBot {
	internal class ResultCache {
		private readonly ConcurrentDictionary<(string fileID, string overlayName), string> dictionary = new ConcurrentDictionary<(string fileID, string overlayName), string>();

		public bool TryAdd(string sourceId, string overlayName, string resultId) => dictionary.TryAdd((sourceId, overlayName), resultId);

		public bool TryGetValue(string sourceId, string overlayName, out string resultId) => dictionary.TryGetValue((sourceId, overlayName), out resultId);
	}
}
