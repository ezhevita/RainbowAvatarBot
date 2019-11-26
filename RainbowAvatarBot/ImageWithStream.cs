using System;
using System.Drawing;
using System.IO;

namespace RainbowAvatarBot {
	internal class ImageWithStream : IDisposable {
	#if SYSTEMDRAWING
		internal ImageWithStream(MemoryStream stream) {
			Image = Image.FromStream(stream);
			Stream = stream;
		}

		internal Image Image { get; }
	#else
		internal ImageWithStream(MemoryStream stream) {
			Image = Image.Load<Rgba32>(stream);
			Stream = stream;
		}

		internal Image<Rgba32> Image { get; }
	#endif

		private MemoryStream Stream { get; }

		public void Dispose() {
			Image.Dispose();
			Stream.Dispose();
		}
	}
}
