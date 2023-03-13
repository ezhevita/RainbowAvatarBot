namespace RainbowAvatarBot.Benchmarks;

public class UnclosableMemoryStream : MemoryStream
{
	protected override void Dispose(bool disposing)
	{
	}
}
