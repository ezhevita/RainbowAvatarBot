using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace RainbowAvatarBot.Processors;

public interface IProcessor
{
	public bool CanProcessMediaType(MediaType mediaType);
	public Task<InputMedia> Process(Stream input, string overlayName, MediaType mediaType);
	public Task Init(IReadOnlyDictionary<string, IReadOnlyCollection<uint>> flagsData);
}
