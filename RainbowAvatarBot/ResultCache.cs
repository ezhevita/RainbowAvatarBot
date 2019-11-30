using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace RainbowAvatarBot {
    internal class ResultCache {
        private ConcurrentDictionary<(string fileID, string overlayName), string> dictionary = new ConcurrentDictionary<(string fileID, string overlayName), string>();

        public bool TryAdd(string sourceId, string overlayName, string resultId) {
            return dictionary.TryAdd((sourceId, overlayName), resultId);
        }
        
        public bool TryGetValue(string sourceId, string overlayName, out string resultId) {
            return dictionary.TryGetValue((sourceId, overlayName), out resultId);
        }
    }
}