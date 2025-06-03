using System.IO;

namespace RainbowAvatarBot.Benchmarks;

internal class UnclosableMemoryStream : MemoryStream
{
	protected override void Dispose(bool disposing)
	{
	}
}
