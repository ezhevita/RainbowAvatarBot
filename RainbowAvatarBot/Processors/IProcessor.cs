using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

internal interface IProcessor
{
	IEnumerable<MediaType> SupportedMediaTypes { get; }
	Task<InputFileStream> Process(Stream input, string overlayName, bool isSticker);
}
