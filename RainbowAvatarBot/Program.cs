using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RainbowAvatarBot.Processors;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace RainbowAvatarBot;

internal static class Program
{
	private const long adminID = 204723509;

	private static readonly ResultCache ResultCache = new();
	private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();
	private static readonly SemaphoreSlim FileSemaphore = new(1, 1);

	private static readonly Timer ClearUsersTimer = new(
		_ => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)
	);

	private static readonly ConcurrentDictionary<long, DateTime> LastUserImageGenerations = new();
	private static readonly Timer ResetTimer = new(_ => ResultCache.Reset(), null, TimeSpan.FromDays(7), TimeSpan.FromDays(7));
	private static readonly SemaphoreSlim ShutdownSemaphore = new(0, 1);
	private static readonly DateTime StartedTime = DateTime.UtcNow;

	private static readonly HashSet<MessageType> SupportedTypes = new(2)
	{
		MessageType.Photo,
		MessageType.Sticker
	};

	private static TelegramBotClient _botClient;
	private static string _botUsername;
	private static IProcessor[] _processors;
	private static ConcurrentDictionary<long, string> _userSettings = new();
	private static Dictionary<string, uint[]> _flags;
	private static HttpClient _httpClient;

	private static async Task BotOnCallbackQuery(CallbackQuery callbackQuery)
	{
		if (callbackQuery == null)
		{
			return;
		}

		SetThreadLocale(callbackQuery.From.LanguageCode);

		var callbackID = callbackQuery.Id;
		var senderID = callbackQuery.From.Id;
		var args = callbackQuery.Data.Split('_', StringSplitOptions.RemoveEmptyEntries);
		if (args.Length < 1)
		{
			return;
		}

		var message = callbackQuery.Message;
		switch (args[0])
		{
			case "SETTINGS":
			{
				var name = args[1];
				if (!_flags.ContainsKey(name))
				{
					await _botClient.AnswerCallbackQueryAsync(callbackID, Localization.InvalidFlagName).ConfigureAwait(false);

					return;
				}

				if (!_userSettings.TryGetValue(senderID, out _))
				{
					_userSettings.TryAdd(senderID, name);
				} else
				{
					_userSettings[senderID] = name;
				}

				await FileSemaphore.WaitAsync().ConfigureAwait(false);
				try
				{
					var settings = JsonSerializer.Serialize(_userSettings);
					await File.WriteAllTextAsync("data/config.json", settings);
					await _botClient.EditMessageTextAsync(
						message.Chat.Id, message.MessageId, Localization.ChangedSuccessfully,
						replyMarkup: InlineKeyboardMarkup.Empty()
					).ConfigureAwait(false);
					await _botClient.AnswerCallbackQueryAsync(callbackID, Localization.Success).ConfigureAwait(false);
				} finally
				{
					FileSemaphore.Release();
				}

				break;
			}
		}
	}

	private static async Task BotOnMessage(Message message)
	{
		if ((message == null) || (message.Date < StartedTime))
		{
			return;
		}

		var senderID = message.From.Id;
		var chatID = message.Chat.Id;

		SetThreadLocale(message.From.LanguageCode);

		string[] args = {""};
		var textMessage = message.Text ?? message.Caption;

		if (!string.IsNullOrEmpty(textMessage) && (textMessage[0] == '/'))
		{
			var argumentToProcess = textMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
			var indexOfAt = argumentToProcess.IndexOf('@');
			if (indexOfAt > 0)
			{
				if (argumentToProcess[(indexOfAt + 1)..] != _botUsername)
				{
					return;
				}

				argumentToProcess = argumentToProcess[..indexOfAt];
			}

			args = argumentToProcess.Split('_', ',');
			args[0] = args[0][1..];
		} else if (message.Chat.Type != ChatType.Private)
		{
			return;
		}

		try
		{
			if (message.Type == MessageType.Text)
			{
				var command = args[0].ToUpperInvariant();
				switch (command)
				{
					case "OFF" when senderID == adminID:
					{
						ShutdownSemaphore.Release();

						break;
					}

					case "AVATAR":
					{
						if (!_userSettings.TryGetValue(senderID, out var overlayName))
						{
							overlayName = "LGBT";
						}

						UserProfilePhotos avatars;
						if (message.ReplyToMessage != null)
						{
							avatars = await _botClient.GetUserProfilePhotosAsync(message.ReplyToMessage.From.Id, limit: 1)
								.ConfigureAwait(false);
							if (avatars.Photos.Length == 0)
							{
								await _botClient.SendTextMessageAsync(
									senderID, Localization.RepliedUserProfilePictureNotFound, replyToMessageId: message.MessageId
								).ConfigureAwait(false);

								return;
							}
						} else
						{
							avatars = await _botClient.GetUserProfilePhotosAsync(senderID, limit: 1).ConfigureAwait(false);
							if (avatars.Photos.Length == 0)
							{
								await _botClient.SendTextMessageAsync(
									senderID, Localization.UserProfilePictureNotFound, replyToMessageId: message.MessageId
								).ConfigureAwait(false);

								return;
							}
						}

						var sourceImage = avatars.Photos[0].OrderByDescending(photo => photo.Height).First();
						await ProcessAndSend(sourceImage.FileId, sourceImage.FileUniqueId, overlayName, message)
							.ConfigureAwait(false);

						break;
					}

					case "COLORIZE" when message.ReplyToMessage?.Type is MessageType.Photo or MessageType.Sticker:
					{
						if (!_userSettings.TryGetValue(senderID, out var overlayName))
						{
							overlayName = "LGBT";
						}

						var targetMessage = message.ReplyToMessage;
						string fileID;
						string uniqueFileID;
						if (targetMessage.Type == MessageType.Photo)
						{
							var photo = targetMessage.Photo.OrderByDescending(size => size.Height).First();
							fileID = photo.FileId;
							uniqueFileID = photo.FileUniqueId;
						} else
						{
							var sticker = targetMessage.Sticker;
							fileID = sticker.FileId;
							uniqueFileID = sticker.FileUniqueId;
						}

						await ProcessAndSend(fileID, uniqueFileID, overlayName, message.ReplyToMessage).ConfigureAwait(false);

						break;
					}

					case "SETTINGS" when message.Chat.Type is ChatType.Group or ChatType.Supergroup:
					{
						await _botClient.SendTextMessageAsync(
							chatID, Localization.SettingsSentToChat, replyToMessageId: message.MessageId
						).ConfigureAwait(false);

						break;
					}

					case "SETTINGS" when message.Chat.Type == ChatType.Private:
					{
						await _botClient.SendTextMessageAsync(
							chatID, Localization.SelectFlag, replyMarkup: BuildKeyboard(
								3, _flags.Keys.Select(
									name => new InlineKeyboardButton(Localization.ResourceManager.GetString(name))
									{
										CallbackData = "SETTINGS_" + name
									}
								).OrderBy(button => button.Text)
							), replyToMessageId: message.MessageId
						).ConfigureAwait(false);

						break;
					}

					default:
					{
						if (message.Chat.Type == ChatType.Private)
						{
							await _botClient.SendTextMessageAsync(
								chatID, Localization.StartMessage, replyToMessageId: message.MessageId,
								parseMode: ParseMode.MarkdownV2
							).ConfigureAwait(false);
						}

						break;
					}
				}
			} else if (SupportedTypes.Contains(message.Type) && ((message.Chat.Type == ChatType.Private) ||
				           ((args.Length > 0) && (args[0].ToUpperInvariant() == "COLORIZE"))))
			{
				if (!_userSettings.TryGetValue(senderID, out var overlayName))
				{
					overlayName = "LGBT";
				}

				string fileID;
				string uniqueFileID;
				if (message.Type == MessageType.Photo)
				{
					var photo = message.Photo.OrderByDescending(size => size.Height).First();
					fileID = photo.FileId;
					uniqueFileID = photo.FileUniqueId;
				} else
				{
					var sticker = message.Sticker;
					fileID = sticker.FileId;
					uniqueFileID = sticker.FileUniqueId;
				}

				await ProcessAndSend(fileID, uniqueFileID, overlayName, message).ConfigureAwait(false);
			}
		} catch (Exception ex)
		{
			Log("Exception has been thrown!");
			Log(ex.ToString());
			try
			{
				await _botClient.SendTextMessageAsync(chatID, Localization.ErrorOccured, replyToMessageId: message.MessageId)
					.ConfigureAwait(false);
			} catch
			{
				// ignored
			}
		}
	}

	private static InlineKeyboardMarkup BuildKeyboard(byte width, IEnumerable<InlineKeyboardButton> buttons)
	{
		var inlineKeyboardButtons = buttons.ToArray();
		var buttonCount = inlineKeyboardButtons.Length;
		var rowAmount = (uint) Math.Ceiling(buttonCount / (double) width);
		var remainder = buttonCount % width;
		var buttonRows = new InlineKeyboardButton[rowAmount][];
		for (var row = 0; row < rowAmount; row++)
		{
			var columnAmount = (rowAmount == row + 1) && (remainder > 0) ? remainder : width;
			var rowButtons = new InlineKeyboardButton[columnAmount];
			for (var column = 0; column < columnAmount; column++)
			{
				rowButtons[column] = inlineKeyboardButtons[row * width + column];
			}

			buttonRows[row] = rowButtons;
		}

		return new InlineKeyboardMarkup(buttonRows);
	}

	private static void ClearUsers()
	{
		foreach (var (userID, _) in LastUserImageGenerations.Where(x => x.Value.AddSeconds(3) < DateTime.UtcNow))
		{
			LastUserImageGenerations.TryRemove(userID, out _);
		}
	}

	private static async Task<Stream> DownloadFile(string fileID)
	{
		var file = await _botClient.GetFileAsync(fileID).ConfigureAwait(false);
		var responseMessage =
			await _httpClient.GetAsync(file.FilePath, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

		responseMessage.EnsureSuccessStatusCode();

		return await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);
	}

	private static Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) =>
		Task.CompletedTask;

	private static Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
	{
		return update.Type switch
		{
			UpdateType.Message => BotOnMessage(update.Message!),
			UpdateType.CallbackQuery => BotOnCallbackQuery(update.CallbackQuery),
			_ => Task.CompletedTask
		};
	}

	private static async Task Init()
	{
		if (File.Exists("data/config.json"))
		{
			await using var configFile = File.OpenRead("data/config.json");
			_userSettings = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<long, string>>(configFile)
				.ConfigureAwait(false);
		}

		Log("Starting " + nameof(RainbowAvatarBot));
		var token = (await File.ReadAllTextAsync("data/token.txt").ConfigureAwait(false)).Trim();
		_botClient = new TelegramBotClient(token);
		if (!await _botClient.TestApiAsync().ConfigureAwait(false))
		{
			Log("Error when starting bot!");

			return;
		}

		_botUsername = (await _botClient.GetMeAsync().ConfigureAwait(false)).Username;

		_httpClient = new HttpClient(
			new HttpClientHandler
			{
				AllowAutoRedirect = false,
				AutomaticDecompression = DecompressionMethods.All,
				UseCookies = false,
				UseProxy = false,
				MaxConnectionsPerServer = 255
			}
		)
		{
			BaseAddress = new Uri($"https://api.telegram.org/file/bot{token}/"),
			DefaultRequestVersion = HttpVersion.Version20,
			Timeout = TimeSpan.FromSeconds(10)
		};

		JObject gradientOverlay;
		using (var gradientFile = File.OpenText("gradientOverlay.json"))
		{
			using JsonTextReader gradientJsonReader = new(gradientFile);
			gradientOverlay = await JObject.LoadAsync(gradientJsonReader).ConfigureAwait(false);
		}

		var referenceObject = new JObject
		{
			["ind"] = 1,
			["ty"] = 0,
			["refId"] = "_",
			["sr"] = 1,
			["ks"] = new JObject
			{
				["o"] = new JObject
				{
					["a"] = 0,
					["k"] = 100
				},
				["r"] = new JObject
				{
					["a"] = 0,
					["k"] = 0
				},
				["p"] = new JObject
				{
					["k"] = new JArray(256, 256, 0)
				},
				["a"] = new JObject
				{
					["k"] = new JArray(256, 256, 0)
				}
			},
			["w"] = 512,
			["h"] = 512
		};

		if (!Directory.Exists("images"))
		{
			Directory.CreateDirectory("images");
		}

		// Using Newtonsoft.Json because System.Text.Json doesn't support JSON5 format (and hexadicimal numbers)
		_flags = JsonConvert
			.DeserializeObject<Dictionary<string, uint[]>>(await File.ReadAllTextAsync("flags.json5").ConfigureAwait(false))
			.Where(name => !File.Exists(Path.Join("images", name + ".png")))
			.ToDictionary(x => x.Key, y => y.Value);

		_processors = new IProcessor[]
		{
			new ImageProcessor(MemoryStreamManager),
			new AnimatedStickerProcessor(MemoryStreamManager, new AnimatedStickerHelperData(gradientOverlay, referenceObject))
		};

		await Task.WhenAll(
			_processors.Select(x => x.Init(_flags.ToDictionary(y => y.Key, y => (IReadOnlyCollection<uint>) y.Value)))
		);

		var updateHandler = new DefaultUpdateHandler(HandleUpdate, HandleError);
		_botClient.StartReceiving(updateHandler);

		Log($"Started {_botUsername}!");
		await ShutdownSemaphore.WaitAsync().ConfigureAwait(false);
		await ClearUsersTimer.DisposeAsync().ConfigureAwait(false);
		await ResetTimer.DisposeAsync().ConfigureAwait(false);
	}

	private static void Log(string strToLog)
	{
		var result = $"{DateTime.UtcNow}|{strToLog}";
		Console.WriteLine(result);
	}

	private static async Task Main()
	{
		try
		{
			await Init();
		} catch (Exception e)
		{
			Console.WriteLine(e);

			throw;
		}
	}

	private static IProcessor GetBestProcessor(MediaType mediaType) =>
		_processors.FirstOrDefault(x => x.CanProcessMediaType(mediaType));

	private static async Task ProcessAndSend(string imageID, string imageUniqueID, string overlayName, Message message)
	{
		var mediaType = message.Type switch
		{
			MessageType.Sticker when message.Sticker.IsAnimated => MediaType.AnimatedSticker,
			MessageType.Sticker when message.Sticker.IsVideo => MediaType.VideoSticker,
			MessageType.Sticker => MediaType.Sticker,
			_ => MediaType.Picture
		};

		Log(message.From.Id + "|" + mediaType + "|" + imageID);

		if (mediaType == MediaType.VideoSticker)
		{
			await _botClient.SendTextMessageAsync(
				message.Chat.Id, Localization.VideoStickersNotSupported, replyToMessageId: message.MessageId
			);

			return;
		}

		var isSticker = message.Type == MessageType.Sticker;

		Stopwatch sw = null;
		Message processMessage = null;

		bool isCached;
		InputMedia resultImage;
		// ReSharper disable once AssignmentInConditionalExpression
		if (isCached = ResultCache.TryGetValue(imageUniqueID, overlayName, out var cachedResultImageID))
		{
			resultImage = cachedResultImageID;
		} else
		{
			var senderID = message.From.Id;
			if (LastUserImageGenerations.TryGetValue(senderID, out var time))
			{
				LastUserImageGenerations[senderID] = DateTime.UtcNow;
				if (time.AddSeconds(1) > DateTime.UtcNow)
				{
					return;
				}
			} else
			{
				LastUserImageGenerations.TryAdd(senderID, DateTime.UtcNow);
			}

			processMessage = await _botClient.SendTextMessageAsync(message.Chat.Id, Localization.Processing).ConfigureAwait(false);
			sw = Stopwatch.StartNew();

			await using var file = await DownloadFile(imageID).ConfigureAwait(false);
			var processor = GetBestProcessor(mediaType);

			resultImage = await processor.Process(file, overlayName, mediaType);
		}

		var resultMessage = isSticker
			? await _botClient.SendStickerAsync(message.Chat.Id, resultImage, replyToMessageId: message.MessageId)
				.ConfigureAwait(false)
			: await _botClient.SendPhotoAsync(message.Chat.Id, resultImage, replyToMessageId: message.MessageId)
				.ConfigureAwait(false);

		if (sw != null)
		{
			sw.Stop();
			Log($"Processed {mediaType} in {sw.ElapsedMilliseconds}ms");
		}

		if (processMessage != null)
		{
#pragma warning disable 4014
			_botClient.DeleteMessageAsync(processMessage.Chat.Id, processMessage.MessageId);
#pragma warning restore 4014
		}

		if (isSticker && (resultMessage.Sticker == null))
		{
			await _botClient.DeleteMessageAsync(resultMessage.Chat, resultMessage.MessageId).ConfigureAwait(false);
			await _botClient.SendTextMessageAsync(message.Chat.Id, Localization.UnableToSend).ConfigureAwait(false);

			return;
		}

		if (!isCached)
		{
			ResultCache.TryAdd(
				imageUniqueID, overlayName,
				isSticker
					? resultMessage.Sticker.FileId
					: resultMessage.Photo.OrderByDescending(photo => photo.Height).First().FileId
			);
		}
	}

	private static void SetThreadLocale(string languageCode)
	{
		Thread.CurrentThread.CurrentUICulture = languageCode switch
		{
			"be" or "uk" or "ru" or "" or null => CultureInfo.GetCultureInfoByIetfLanguageTag("ru-RU"),
			_ => CultureInfo.GetCultureInfoByIetfLanguageTag(languageCode)
		};
	}
}
