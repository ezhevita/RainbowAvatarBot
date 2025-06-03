using System.Text.Json.Nodes;

namespace RainbowAvatarBot;

internal static class Extensions
{
	public static JsonArray ToJsonArray<T>(this T[] array)
	{
		var jsonArray = new JsonArray();
		foreach (var item in array)
		{
			jsonArray.Add(item);
		}

		return jsonArray;
	}

	public static bool IsSticker(this MediaType mediaType) =>
		mediaType is MediaType.Sticker or MediaType.AnimatedSticker or MediaType.VideoSticker;
}
