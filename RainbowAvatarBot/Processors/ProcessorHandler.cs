using System.Collections.Frozen;
using System.Collections.Generic;

namespace RainbowAvatarBot.Processors;

internal sealed class ProcessorHandler
{
	private readonly FrozenDictionary<MediaType, IProcessor> _processors;

	public ProcessorHandler(IEnumerable<IProcessor> processors)
	{
		var dictionary = new Dictionary<MediaType, IProcessor>();
		foreach (var processor in processors)
		{
			foreach (var mediaType in processor.SupportedMediaTypes)
			{
				dictionary.Add(mediaType, processor);
			}
		}

		_processors = dictionary.ToFrozenDictionary();
	}

	public IProcessor GetProcessor(MediaType mediaType) => _processors[mediaType];
}
