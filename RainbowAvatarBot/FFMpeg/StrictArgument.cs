using FFMpegCore.Arguments;

namespace RainbowAvatarBot.FFMpeg;

internal sealed class StrictArgument : IArgument
{
	public StrictArgument(StrictMode mode)
	{
		Text = "-strict " + (int)mode;
	}

	public string Text { get; }
}
