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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RainbowAvatarBot.Processors;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace RainbowAvatarBot;

public sealed class Bot : IDisposable
{
	private readonly BotConfiguration _botConfiguration;
	private readonly BotUserData _botUserData;
	private readonly Timer _clearUsersTimer;
	private readonly SemaphoreSlim _fileSemaphore = new(1, 1);
	private readonly ConcurrentDictionary<long, DateTime> _lastUserImageGenerations = new();
	private readonly ILogger<Bot> _logger;
	private readonly RecyclableMemoryStreamManager _memoryStreamManager = new();
	private readonly Timer _resetTimer;
	private readonly ResultCache _resultCache = new();

	private readonly HashSet<MessageType> _supportedTypes = new(2)
	{
		MessageType.Photo,
		MessageType.Sticker
	};

	private readonly ITelegramBotClient _telegramBotClient;
	private Dictionary<string, uint[]> _flags = null!;
	private HttpClient _httpClient = null!;
	private IProcessor[] _processors = null!;
	private ConcurrentDictionary<long, string> _userSettings = new();

	public Bot(ITelegramBotClient telegramBotClient, IOptions<BotConfiguration> botConfiguration, ILogger<Bot> logger,
		BotUserData botUserData)
	{
		_telegramBotClient = telegramBotClient;
		_logger = logger;
		_botUserData = botUserData;
		_botConfiguration = botConfiguration.Value;
		_clearUsersTimer = new Timer(_ => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
		_resetTimer = new Timer(_ => _resultCache.Reset(), null, TimeSpan.FromDays(7), TimeSpan.FromDays(7));
	}

	public void Dispose()
	{
		_fileSemaphore.Dispose();
		_clearUsersTimer.Dispose();
		_resetTimer.Dispose();
		_httpClient.Dispose();
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

	private void ClearUsers()
	{
		foreach (var (userID, _) in _lastUserImageGenerations.Where(x => x.Value.AddSeconds(3) < DateTime.UtcNow))
		{
			_lastUserImageGenerations.TryRemove(userID, out _);
		}
	}

	private async Task<Stream> DownloadFile(string fileID)
	{
		var file = await _telegramBotClient.GetFileAsync(fileID).ConfigureAwait(false);
		var responseMessage =
			await _httpClient.GetAsync(new Uri(file.FilePath, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

		responseMessage.EnsureSuccessStatusCode();

		return await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);
	}

	private IProcessor? GetBestProcessor(MediaType mediaType) =>
		_processors.FirstOrDefault(x => x.CanProcessMediaType(mediaType));

	public async Task Init()
	{
		if (!Directory.Exists("data"))
		{
			Directory.CreateDirectory("data");
		}

		var configLocation = Path.Join("data", "config.json");
		if (File.Exists(configLocation))
		{
			await using var configFile = File.OpenRead(configLocation);
			_userSettings = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<long, string>>(configFile)
				.ConfigureAwait(false) ?? throw new Exception("Settings are null");
		}

		if (!await _telegramBotClient.TestApiAsync().ConfigureAwait(false))
		{
			throw new Exception("Error when starting bot!");
		}

		var me = await _telegramBotClient.GetMeAsync().ConfigureAwait(false);

		_botUserData.Username = me.Username!;

		_httpClient = new HttpClient(
#pragma warning disable CA2000
			new HttpClientHandler
			{
				AllowAutoRedirect = false,
				AutomaticDecompression = DecompressionMethods.All,
				UseCookies = false,
				UseProxy = false,
				MaxConnectionsPerServer = 255
			}
#pragma warning restore CA2000
		)
		{
			BaseAddress = new Uri($"https://api.telegram.org/file/bot{_botConfiguration.Token}/"),
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

		// Using Newtonsoft.Json because System.Text.Json doesn't support JSON5 format (and hexadecimal numbers)
		_flags = JsonConvert
			.DeserializeObject<Dictionary<string, uint[]>>(await File.ReadAllTextAsync("flags.json5").ConfigureAwait(false))
			.Where(name => !File.Exists(Path.Join("images", name + ".png")))
			.ToDictionary(x => x.Key, y => y.Value);

		_processors = new IProcessor[]
		{
			new ImageProcessor(_memoryStreamManager),
			new AnimatedStickerProcessor(_memoryStreamManager, new AnimatedStickerHelperData(gradientOverlay, referenceObject)),
			new VideoStickerProcessor(_memoryStreamManager)
		};

		await Task.WhenAll(
			_processors.Select(x => x.Init(_flags.ToDictionary(y => y.Key, y => (IReadOnlyCollection<uint>) y.Value)))
		).ConfigureAwait(false);

		try
		{
			await using var placeholder = File.OpenRead("placeholder.webm");
			await _telegramBotClient.CreateNewVideoStickerSetAsync(
				_botConfiguration.OwnerId,
				_botConfiguration.PackNamePrefix + _botUserData.Username,
				"Created by @" + _botUserData.Username,
				new InputFileStream(placeholder, "placeholder.webm"),
				"🌈🏳️‍🌈"
			);
		} catch
		{
			// ignored
		}
	}

	public async Task OnCallbackQuery(CallbackQuery callbackQuery)
	{
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
					await _telegramBotClient.AnswerCallbackQueryAsync(callbackID, Localization.InvalidFlagName)
						.ConfigureAwait(false);

					return;
				}

				if (!_userSettings.TryGetValue(senderID, out _))
				{
					_userSettings.TryAdd(senderID, name);
				} else
				{
					_userSettings[senderID] = name;
				}

				await _fileSemaphore.WaitAsync().ConfigureAwait(false);
				try
				{
					var settings = JsonSerializer.Serialize(_userSettings);
					await File.WriteAllTextAsync(Path.Join("data", "config.json"), settings);
					await _telegramBotClient.EditMessageTextAsync(
						message.Chat.Id, message.MessageId, Localization.ChangedSuccessfully,
						replyMarkup: InlineKeyboardMarkup.Empty()
					).ConfigureAwait(false);
					await _telegramBotClient.AnswerCallbackQueryAsync(callbackID, Localization.Success).ConfigureAwait(false);
				} catch
				{
					// ignored
				} finally
				{
					_fileSemaphore.Release();
				}

				break;
			}
		}
	}

	public async Task OnMessage(Message message)
	{
		var senderID = message.From.Id;
		var chatID = message.Chat.Id;

		SetThreadLocale(message.From.LanguageCode);

		string[] args = {""};
		var textMessage = message.Text ?? message.Caption;

		if (!string.IsNullOrEmpty(textMessage) && (textMessage[0] == '/'))
		{
			var argumentToProcess = textMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
			var indexOfAt = argumentToProcess.IndexOf('@', StringComparison.Ordinal);
			if (indexOfAt > 0)
			{
				if (argumentToProcess[(indexOfAt + 1)..] != _botUserData.Username)
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
					case "AVATAR":
					{
						if (!_userSettings.TryGetValue(senderID, out var overlayName))
						{
							overlayName = "LGBT";
						}

						UserProfilePhotos avatars;
						if (message.ReplyToMessage != null)
						{
							avatars = await _telegramBotClient.GetUserProfilePhotosAsync(message.ReplyToMessage.From.Id, limit: 1)
								.ConfigureAwait(false);
							if (avatars.Photos.Length == 0)
							{
								await _telegramBotClient.SendTextMessageAsync(
									senderID, Localization.RepliedUserProfilePictureNotFound, replyToMessageId: message.MessageId
								).ConfigureAwait(false);

								return;
							}
						} else
						{
							avatars = await _telegramBotClient.GetUserProfilePhotosAsync(senderID, limit: 1)
								.ConfigureAwait(false);
							if (avatars.Photos.Length == 0)
							{
								await _telegramBotClient.SendTextMessageAsync(
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
						await _telegramBotClient.SendTextMessageAsync(
							chatID, Localization.SettingsSentToChat, replyToMessageId: message.MessageId
						).ConfigureAwait(false);

						break;
					}

					case "SETTINGS" when message.Chat.Type == ChatType.Private:
					{
						await _telegramBotClient.SendTextMessageAsync(
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
							await _telegramBotClient.SendTextMessageAsync(
								chatID, Localization.StartMessage, replyToMessageId: message.MessageId,
								parseMode: ParseMode.MarkdownV2
							).ConfigureAwait(false);
						}

						break;
					}
				}
			} else if (_supportedTypes.Contains(message.Type) && ((message.Chat.Type == ChatType.Private) ||
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
			_logger.LogError(ex, "An exception occurred while handling the message!");
			try
			{
				await _telegramBotClient.SendTextMessageAsync(
						chatID, Localization.ErrorOccured, replyToMessageId: message.MessageId
					)
					.ConfigureAwait(false);
			} catch
			{
				// ignored
			}
		}
	}

	private async Task ProcessAndSend(string imageID, string imageUniqueID, string overlayName, Message message)
	{
		var mediaType = message.Type switch
		{
			MessageType.Sticker when message.Sticker.IsAnimated => MediaType.AnimatedSticker,
			MessageType.Sticker when message.Sticker.IsVideo => MediaType.VideoSticker,
			MessageType.Sticker => MediaType.Sticker,
			_ => MediaType.Picture
		};

		_logger.LogDebug(
			"Message from {UserId}, type {MediaType}, image ID {ImageId}", message.From.Id, mediaType, imageID
		);

		var isSticker = message.Type == MessageType.Sticker;

		Stopwatch? sw = null;
		Message? processMessage = null;

		bool isCached;
		InputMedia resultImage;
		// ReSharper disable once AssignmentInConditionalExpression
		if (isCached = _resultCache.TryGetValue(imageUniqueID, overlayName, out var cachedResultImageID))
		{
			resultImage = cachedResultImageID;
		} else
		{
			var senderID = message.From.Id;
			if (_lastUserImageGenerations.TryGetValue(senderID, out var time))
			{
				_lastUserImageGenerations[senderID] = DateTime.UtcNow;
				if (time.AddSeconds(1) > DateTime.UtcNow)
				{
					return;
				}
			} else
			{
				_lastUserImageGenerations.TryAdd(senderID, DateTime.UtcNow);
			}

			processMessage = await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, Localization.Processing)
				.ConfigureAwait(false);
			sw = Stopwatch.StartNew();

			await using var file = await DownloadFile(imageID).ConfigureAwait(false);
			var processor = GetBestProcessor(mediaType);

			resultImage = await processor.Process(file, overlayName, mediaType).ConfigureAwait(false);
		}

		if (mediaType == MediaType.VideoSticker && !isCached)
		{
			var stickerPackName = _botConfiguration.PackNamePrefix + _botUserData.Username;

			await _telegramBotClient.AddVideoStickerToSetAsync(_botConfiguration.OwnerId, stickerPackName, resultImage, "📹");

			await resultImage.Content!.DisposeAsync();

			var stickerSet = await _telegramBotClient.GetStickerSetAsync(stickerPackName);

			resultImage = new InputMedia(stickerSet.Stickers.Last().FileId);

			await _telegramBotClient.DeleteStickerFromSetAsync(resultImage.FileId);
		}

		var resultMessage = isSticker
			? await _telegramBotClient.SendStickerAsync(message.Chat.Id, resultImage, replyToMessageId: message.MessageId)
				.ConfigureAwait(false)
			: await _telegramBotClient.SendPhotoAsync(message.Chat.Id, resultImage, replyToMessageId: message.MessageId)
				.ConfigureAwait(false);

		if (sw != null)
		{
			sw.Stop();
			_logger.LogInformation("Processed {MediaType} in {ElapsedMilliseconds}ms", mediaType, sw.ElapsedMilliseconds);
		}

		if (processMessage != null)
		{
#pragma warning disable 4014
			_telegramBotClient.DeleteMessageAsync(processMessage.Chat.Id, processMessage.MessageId);
#pragma warning restore 4014
		}

		if (isSticker && (resultMessage.Sticker == null))
		{
			await _telegramBotClient.DeleteMessageAsync(resultMessage.Chat, resultMessage.MessageId).ConfigureAwait(false);
			await _telegramBotClient.SendTextMessageAsync(message.Chat.Id, Localization.UnableToSend).ConfigureAwait(false);

			return;
		}

		if (!isCached)
		{
			_resultCache.TryAdd(
				imageUniqueID, overlayName,
				isSticker
					? resultMessage.Sticker.FileId
					: resultMessage.Photo.OrderByDescending(photo => photo.Height).First().FileId
			);
		}
	}

	private static void SetThreadLocale(string? languageCode)
	{
		Thread.CurrentThread.CurrentUICulture = languageCode switch
		{
			"be" or "uk" or "ru" or "" or null => CultureInfo.GetCultureInfoByIetfLanguageTag("ru-RU"),
			_ => CultureInfo.GetCultureInfoByIetfLanguageTag(languageCode)
		};
	}
}
