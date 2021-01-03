using System.Collections.Concurrent;

namespace RainbowAvatarBot {
	internal class ResultCache {
		private readonly ConcurrentDictionary<(string fileID, string overlayName), string> Dictionary = new();

		public void Reset() {
			Dictionary.Clear();
		}

		public void TryAdd(string sourceId, string overlayName, string resultId) {
			Dictionary.TryAdd((sourceId, overlayName), resultId);
		}

		public bool TryGetValue(string sourceId, string overlayName, out string resultId) {
			resultId = null;
			return false;

			//return Dictionary.TryGetValue((sourceId, overlayName), out resultId);
		}
	}
}
