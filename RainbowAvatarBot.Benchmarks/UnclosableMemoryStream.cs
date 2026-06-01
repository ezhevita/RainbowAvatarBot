using System.IO;

namespace RainbowAvatarBot.Benchmarks;

internal sealed class UnclosableMemoryStream : MemoryStream
{
#pragma warning disable CA2215 // Dispose methods should call base class dispose -- intentionally disabled
	protected override void Dispose(bool disposing)
#pragma warning restore CA2215 // Dispose methods should call base class dispose
	{
	}
}
