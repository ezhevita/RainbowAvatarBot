using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace RainbowAvatarBot {
	internal static class Program {
		private const long AdminID = 204723509;
		private static readonly Dictionary<string, Image> Images = new();

		private static readonly ResultCache ResultCache = new();
		private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();
		private static readonly SemaphoreSlim FileSemaphore = new(1, 1);
		private static readonly Timer ClearUsersTimer = new(_ => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
		private static readonly ConcurrentDictionary<long, DateTime> LastUserImageGenerations = new();
		private static readonly Timer ResetTimer = new(_ => ResultCache.Reset(), null, TimeSpan.FromDays(7), TimeSpan.FromDays(7));
		private static readonly SemaphoreSlim ShutdownSemaphore = new(0, 1);
		private static readonly DateTime StartedTime = DateTime.UtcNow;

		private static readonly HashSet<MessageType> SupportedTypes = new(2) {
			MessageType.Photo,
			MessageType.Sticker
		};

		private static TelegramBotClient BotClient;
		private static string BotUsername;
		private static ConcurrentDictionary<long, string> UserSettings = new();
		private static JObject GradientOverlay;
		private static JObject ReferenceObject;
		private static Dictionary<string, uint[]> Flags;
		private static Dictionary<string, float[]> FlagGradients;
		private static HttpClient HttpClient;

		private static async Task BotOnCallbackQuery(CallbackQuery callbackQuery) {
			if (callbackQuery == null) {
				return;
			}

			SetThreadLocale(callbackQuery.From.LanguageCode);

			var callbackID = callbackQuery.Id;
			var senderID = callbackQuery.From.Id;
			var args = callbackQuery.Data.Split('_', StringSplitOptions.RemoveEmptyEntries);
			if (args.Length < 1) {
				return;
			}

			var message = callbackQuery.Message;
			switch (args[0]) {
				case "SETTINGS": {
					var name = args[1];
					if (!Images.ContainsKey(name)) {
						await BotClient.AnswerCallbackQueryAsync(callbackID, Localization.InvalidFlagName).ConfigureAwait(false);
						return;
					}

					if (!UserSettings.TryGetValue(senderID, out _)) {
						UserSettings.TryAdd(senderID, name);
					} else {
						UserSettings[senderID] = name;
					}

					await FileSemaphore.WaitAsync().ConfigureAwait(false);
					try {
						await using var configFile = File.OpenWrite("config.json");
						await JsonSerializer.SerializeAsync(configFile, UserSettings).ConfigureAwait(false);
						await BotClient.EditMessageTextAsync(message.Chat.Id, message.MessageId, Localization.ChangedSuccessfully, replyMarkup: InlineKeyboardMarkup.Empty()).ConfigureAwait(false);
						await BotClient.AnswerCallbackQueryAsync(callbackID, Localization.Success).ConfigureAwait(false);
					} finally {
						FileSemaphore.Release();
					}

					break;
				}
			}
		}

		private static async Task BotOnMessage(Message message) {
			if ((message == null) || (message.Date < StartedTime)) {
				return;
			}

			var senderID = message.From.Id;
			var chatID = message.Chat.Id;

			SetThreadLocale(message.From.LanguageCode);

			string[] args = {""};
			var textMessage = message.Text ?? message.Caption;

			if (!string.IsNullOrEmpty(textMessage) && (textMessage[0] == '/')) {
				var argumentToProcess = textMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
				var indexOfAt = argumentToProcess.IndexOf('@');
				if (indexOfAt > 0) {
					if (argumentToProcess[(indexOfAt + 1)..] != BotUsername) {
						return;
					}

					argumentToProcess = argumentToProcess[..indexOfAt];
				}

				args = argumentToProcess.Split('_', ',');
				args[0] = args[0][1..];
			} else if (message.Chat.Type != ChatType.Private) {
				return;
			}

			try {
				if (message.Type == MessageType.Text) {
					var command = args[0].ToUpperInvariant();
					switch (command) {
						case "OFF" when senderID == AdminID: {
							ShutdownSemaphore.Release();
							break;
						}

						case "AVATAR": {
							if (!UserSettings.TryGetValue(senderID, out var overlayName)) {
								overlayName = "LGBT";
							}

							UserProfilePhotos avatars;
							if (message.ReplyToMessage != null) {
								avatars = await BotClient.GetUserProfilePhotosAsync(message.ReplyToMessage.From.Id, limit: 1).ConfigureAwait(false);
								if (avatars.Photos.Length == 0) {
									await BotClient.SendTextMessageAsync(senderID, Localization.RepliedUserProfilePictureNotFound, replyToMessageId: message.MessageId).ConfigureAwait(false);
									return;
								}
							} else {
								avatars = await BotClient.GetUserProfilePhotosAsync(senderID, limit: 1).ConfigureAwait(false);
								if (avatars.Photos.Length == 0) {
									await BotClient.SendTextMessageAsync(senderID, Localization.UserProfilePictureNotFound, replyToMessageId: message.MessageId).ConfigureAwait(false);
									return;
								}
							}

							var sourceImage = avatars.Photos[0].OrderByDescending(photo => photo.Height).First();
							await ProcessAndSend(sourceImage.FileId, sourceImage.FileUniqueId, overlayName, message).ConfigureAwait(false);
							break;
						}

						case "COLORIZE" when message.ReplyToMessage?.Type is MessageType.Photo or MessageType.Sticker: {
							if (!UserSettings.TryGetValue(senderID, out var overlayName)) {
								overlayName = "LGBT";
							}

							var targetMessage = message.ReplyToMessage;
							string fileID;
							string uniqueFileID;
							if (targetMessage.Type == MessageType.Photo) {
								var photo = targetMessage.Photo.OrderByDescending(size => size.Height).First();
								fileID = photo.FileId;
								uniqueFileID = photo.FileUniqueId;
							} else {
								var sticker = targetMessage.Sticker;
								fileID = sticker.FileId;
								uniqueFileID = sticker.FileUniqueId;
							}

							await ProcessAndSend(fileID, uniqueFileID, overlayName, message.ReplyToMessage).ConfigureAwait(false);
							break;
						}

						case "SETTINGS" when message.Chat.Type is ChatType.Group or ChatType.Supergroup: {
							await BotClient.SendTextMessageAsync(chatID, Localization.SettingsSentToChat, replyToMessageId: message.MessageId).ConfigureAwait(false);
							break;
						}

						case "SETTINGS" when message.Chat.Type == ChatType.Private: {
							await BotClient.SendTextMessageAsync(chatID, Localization.SelectFlag, replyMarkup: BuildKeyboard(3, Images.Keys.Select(name => new InlineKeyboardButton(Localization.ResourceManager.GetString(name)) {
								CallbackData = "SETTINGS_" + name
							}).OrderBy(button => button.Text)), replyToMessageId: message.MessageId).ConfigureAwait(false);
							break;
						}

						default: {
							if (message.Chat.Type == ChatType.Private) {
								await BotClient.SendTextMessageAsync(chatID, Localization.StartMessage, replyToMessageId: message.MessageId, parseMode: ParseMode.MarkdownV2).ConfigureAwait(false);
							}

							break;
						}
					}
				} else if (SupportedTypes.Contains(message.Type) && ((message.Chat.Type == ChatType.Private) || ((args.Length > 0) && (args[0].ToUpperInvariant() == "COLORIZE")))) {
					if (!UserSettings.TryGetValue(senderID, out var overlayName)) {
						overlayName = "LGBT";
					}

					string fileID;
					string uniqueFileID;
					if (message.Type == MessageType.Photo) {
						var photo = message.Photo.OrderByDescending(size => size.Height).First();
						fileID = photo.FileId;
						uniqueFileID = photo.FileUniqueId;
					} else {
						var sticker = message.Sticker;
						fileID = sticker.FileId;
						uniqueFileID = sticker.FileUniqueId;
					}

					await ProcessAndSend(fileID, uniqueFileID, overlayName, message).ConfigureAwait(false);
				}
			} catch (Exception ex) {
				Log("Exception has been thrown!");
				Log(ex.ToString());
				try {
					await BotClient.SendTextMessageAsync(chatID, Localization.ErrorOccured, replyToMessageId: message.MessageId).ConfigureAwait(false);
				} catch {
					// ignored
				}
			}
		}

		private static InlineKeyboardMarkup BuildKeyboard(byte width, IEnumerable<InlineKeyboardButton> buttons) {
			var inlineKeyboardButtons = buttons.ToArray();
			var buttonCount = inlineKeyboardButtons.Length;
			var rowAmount = (uint) Math.Ceiling(buttonCount / (double) width);
			var remainder = buttonCount % width;
			var buttonRows = new InlineKeyboardButton[rowAmount][];
			for (var row = 0; row < rowAmount; row++) {
				var columnAmount = (rowAmount == row + 1) && (remainder > 0) ? remainder : width;
				var rowButtons = new InlineKeyboardButton[columnAmount];
				for (var column = 0; column < columnAmount; column++) {
					rowButtons[column] = inlineKeyboardButtons[row * width + column];
				}

				buttonRows[row] = rowButtons;
			}

			return new InlineKeyboardMarkup(buttonRows);
		}

		private static void ClearUsers() {
			foreach (var (userID, _) in LastUserImageGenerations.Where(x => x.Value.AddSeconds(3) < DateTime.UtcNow)) {
				LastUserImageGenerations.TryRemove(userID, out _);
			}
		}

		private static async Task<(Stream Result, long Length)> DownloadFile(string fileID) {
			var file = await BotClient.GetFileAsync(fileID).ConfigureAwait(false);
			var responseMessage = await HttpClient.GetAsync(file.FilePath, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

			responseMessage.EnsureSuccessStatusCode();
			return (await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false), responseMessage.Content.Headers.ContentLength.GetValueOrDefault(0));
		}

		private static float[] GenerateGradient(IReadOnlyCollection<uint> rgbValues) {
			var gradientProps = new float[(rgbValues.Count * 2 - 2) * 4];
			byte index = 0;
			foreach (var rgbValue in rgbValues) {
				void FillLine(byte startIndex, byte indexInc) {
					gradientProps[startIndex] = (float) Math.Round((float) (index + indexInc) / rgbValues.Count, 3);
					gradientProps[startIndex + 1] = (float) Math.Round((rgbValue >> 16) / 255.0, 3);
					gradientProps[startIndex + 2] = (float) Math.Round(((rgbValue >> 8) & 0xFF) / 255.0, 3);
					gradientProps[startIndex + 3] = (float) Math.Round((rgbValue & 0xFF) / 255.0, 3);
				}

				if (index > 0) {
					FillLine((byte) ((index << 3) - 4), 0);
				}

				if (index < rgbValues.Count - 1) {
					FillLine((byte) (index << 3), 1);
				}

				index++;
			}

			return gradientProps;
		}

		private static void GenerateImages(Dictionary<string, uint[]> flags) {
			foreach (var (name, rgbValues) in flags) {
				using Image<Rgba32> image = new(1, rgbValues.Length);
				byte index = 0;
				foreach (var rgbValue in rgbValues) {
					var indexCopy = index;

					image.Mutate(img => img.Fill(new Argb32((byte) (rgbValue >> 16), (byte) ((rgbValue >> 8) & 0xFF), (byte) (rgbValue & 0xFF)), new RectangleF(0, indexCopy, 1, 1)));
					index++;
				}

				image.Save(Path.Join("images", $"{name}.png"), new PngEncoder());
			}
		}

		private static async Task LoadImages() {
			foreach (var file in Directory.EnumerateFiles("images")) {
				var name = Path.GetFileNameWithoutExtension(file);
				Images.Add(name, await Image.LoadAsync(file).ConfigureAwait(false));
			}
		}

		private static void Log(string strToLog) {
			var result = $"{DateTime.UtcNow}|{strToLog}";
			Console.WriteLine(result);
			File.AppendAllText("log.txt", result + Environment.NewLine);
		}

		private static async Task Main() {
			if (File.Exists("config.json")) {
				await using var configFile = File.OpenRead("config.json");
				UserSettings = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<long, string>>(configFile).ConfigureAwait(false);
			}

			Log("Starting " + nameof(RainbowAvatarBot));
			var token = await File.ReadAllTextAsync("token.txt").ConfigureAwait(false);
			BotClient = new TelegramBotClient(token);
			if (!await BotClient.TestApiAsync().ConfigureAwait(false)) {
				Log("Error when starting bot!");
				return;
			}

			BotUsername = (await BotClient.GetMeAsync().ConfigureAwait(false)).Username;

			HttpClient = new HttpClient(new HttpClientHandler {
				AllowAutoRedirect = false,
				AutomaticDecompression = DecompressionMethods.All,
				UseCookies = false,
				UseProxy = false,
				MaxConnectionsPerServer = 255
			}) {
				BaseAddress = new Uri($"https://api.telegram.org/file/bot{token}/"),
				DefaultRequestVersion = HttpVersion.Version20,
				Timeout = TimeSpan.FromSeconds(10)
			};

			using (var gradientFile = File.OpenText("gradientOverlay.json")) {
				using JsonTextReader gradientJsonReader = new(gradientFile);
				GradientOverlay = await JObject.LoadAsync(gradientJsonReader).ConfigureAwait(false);
			}

			ReferenceObject = new JObject {
				["ind"] = 1,
				["ty"] = 0,
				["refId"] = "_",
				["sr"] = 1,
				["ks"] = new JObject {
					["o"] = new JObject {
						["a"] = 0,
						["k"] = 100
					},
					["r"] = new JObject {
						["a"] = 0,
						["k"] = 0
					},
					["p"] = new JObject {
						["k"] = new JArray(256, 256, 0)
					},
					["a"] = new JObject {
						["k"] = new JArray(256, 256, 0)
					}
				},
				["w"] = 512,
				["h"] = 512
			};

			if (!Directory.Exists("images")) {
				Directory.CreateDirectory("images");
			}

			// Using Newtonsoft.Json because System.Text.Json doesn't support JSON5 format (and hexadicimal numbers)
			Flags = JsonConvert.DeserializeObject<Dictionary<string, uint[]>>(await File.ReadAllTextAsync("flags.json").ConfigureAwait(false))
				.Where(name => !File.Exists(Path.Join("images", name + ".png")))
				.ToDictionary(x => x.Key, y => y.Value);

			var existFiles = Directory.EnumerateFiles("images", "*.png").Select(Path.GetFileNameWithoutExtension);
			if (Flags.Keys.Any(name => !existFiles.Contains(name))) {
				GenerateImages(Flags);
			}

			await LoadImages().ConfigureAwait(false);

			FlagGradients = new Dictionary<string, float[]>(Flags.Count);
			foreach (var (flagName, rgbValues) in Flags) {
				FlagGradients[flagName] = GenerateGradient(rgbValues);
			}

			var updateHandler = new DefaultUpdateHandler(HandleUpdate, HandleError);
			BotClient.StartReceiving(updateHandler);

			Log($"Started {BotUsername}!");
			await ShutdownSemaphore.WaitAsync().ConfigureAwait(false);
			await ClearUsersTimer.DisposeAsync().ConfigureAwait(false);
			await ResetTimer.DisposeAsync().ConfigureAwait(false);
			foreach (var (_, image) in Images) {
				image.Dispose();
			}
		}

		private static Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		private static Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
		{
			return update.Type switch
			{
				UpdateType.Message => BotOnMessage(update.Message!),
				UpdateType.CallbackQuery => BotOnCallbackQuery(update.CallbackQuery),
				_ => Task.CompletedTask
			};
		}

		private static async Task<Stream> PackAnimatedSticker(JToken content) {
			await using var memoryStream = MemoryStreamManager.GetStream("IntermediateForAnimatedSticker", 640 * 1024);
			var resultStream = MemoryStreamManager.GetStream("ResultForAnimatedSticker", 64 * 1024);

			await using (StreamWriter streamWriter = new(memoryStream, leaveOpen: true))
			using (JsonTextWriter jsonTextWriter = new(streamWriter) {
				Formatting = Formatting.None,
				AutoCompleteOnClose = true,
				CloseOutput = false
			}) {
				await content.WriteToAsync(jsonTextWriter).ConfigureAwait(false);
			}

			memoryStream.Position = 0;

			await using (GZipOutputStream gzipOutput = new(resultStream)) {
				gzipOutput.SetLevel(9);
				gzipOutput.IsStreamOwner = false;
				await memoryStream.CopyToAsync(gzipOutput).ConfigureAwait(false);
			}

			resultStream.Position = 0;
			return resultStream;
		}

		private static async Task ProcessAndSend(string imageID, string imageUniqueID, string overlayName, Message message) {
			var mediaType = message.Type switch {
				MessageType.Sticker when message.Sticker.IsAnimated => MediaType.AnimatedSticker,
				MessageType.Sticker => MediaType.Sticker,
				_ => MediaType.Picture
			};

			Log(message.From.Id + "|" + mediaType + "|" + imageID);

			var isSticker = message.Type == MessageType.Sticker;

			Stopwatch sw = null;
			Stream resultStream = null;
			Message processMessage = null;

			bool isCached;
			InputMedia resultImage;
			// ReSharper disable once AssignmentInConditionalExpression
			if (isCached = ResultCache.TryGetValue(imageUniqueID, overlayName, out var cachedResultImageID)) {
				resultImage = cachedResultImageID;
			} else {
				var senderID = message.From.Id;
				if (LastUserImageGenerations.TryGetValue(senderID, out var time)) {
					LastUserImageGenerations[senderID] = DateTime.UtcNow;
					if (time.AddSeconds(1) > DateTime.UtcNow) {
						return;
					}
				} else {
					LastUserImageGenerations.TryAdd(senderID, DateTime.UtcNow);
				}

				processMessage = await BotClient.SendTextMessageAsync(message.Chat.Id, Localization.Processing).ConfigureAwait(false);
				sw = Stopwatch.StartNew();
				resultStream = await ProcessImage(imageID, overlayName, mediaType).ConfigureAwait(false);
				resultImage = new InputMedia(resultStream, mediaType switch {
					MediaType.Picture => "picture.png",
					MediaType.Sticker => "sticker.webp",
					MediaType.AnimatedSticker => "sticker.tgs",
					_ => throw new ArgumentOutOfRangeException(nameof(message))
				});
			}

			Message resultMessage;
			try {
				resultMessage = isSticker ? await BotClient.SendStickerAsync(message.Chat.Id, resultImage, replyToMessageId: message.MessageId).ConfigureAwait(false) : await BotClient.SendPhotoAsync(message.Chat.Id, resultImage, replyToMessageId: message.MessageId).ConfigureAwait(false);
			} finally {
				if (resultStream != null) {
					await resultStream.DisposeAsync().ConfigureAwait(false);
				}
			}

			if (sw != null) {
				sw.Stop();
				Log($"Processed {mediaType} in {sw.ElapsedMilliseconds}ms");
			}

			if (processMessage != null) {
				#pragma warning disable 4014
				BotClient.DeleteMessageAsync(processMessage.Chat.Id, processMessage.MessageId);
				#pragma warning restore 4014
			}

			if (isSticker && (resultMessage.Sticker == null)) {
				await BotClient.DeleteMessageAsync(resultMessage.Chat, resultMessage.MessageId).ConfigureAwait(false);
				await BotClient.SendTextMessageAsync(message.Chat.Id, Localization.UnableToSend).ConfigureAwait(false);
				return;
			}

			if (!isCached) {
				ResultCache.TryAdd(imageUniqueID, overlayName, isSticker ? resultMessage.Sticker.FileId : resultMessage.Photo.OrderByDescending(photo => photo.Height).First().FileId);
			}
		}

		private static async Task<Stream> ProcessImage(string fileId, string overlayName, MediaType mediaType) {
			var fileData = await DownloadFile(fileId).ConfigureAwait(false);
			await using var file = fileData.Result;

			if (mediaType == MediaType.AnimatedSticker) {
				var stickerObject = await UnpackAnimatedSticker(file).ConfigureAwait(false);
				var processedAnimation = ProcessLottieAnimation(stickerObject, overlayName);
				return await PackAnimatedSticker(processedAnimation).ConfigureAwait(false);
			}

			var image = await Image.LoadAsync(file).ConfigureAwait(false);
			image.Overlay(Images[overlayName]);

			var result = MemoryStreamManager.GetStream("ResultPictureStream", 1 * 1024 * 1024);
			await image.SaveToPng(result).ConfigureAwait(false);

			return result;
		}

		private static JObject ProcessLottieAnimation(JObject tokenizedSticker, string overlayName) {
			var layersToken = (JArray) tokenizedSticker["layers"];
			var assetsToken = (JArray) tokenizedSticker["assets"];

			var rgbValuesLength = Flags[overlayName].Length;

			// Packing main animation to asset
			JObject assetToken = new() {
				["id"] = "_",
				["layers"] = layersToken
			};

			// Getting last frame
			var lastFrame = tokenizedSticker["op"].Value<ushort>();

			layersToken.RemoveAll();
			assetsToken.Add(assetToken);

			// Layer that reference main animation from assets
			var clonedReferenceLayerObject = ReferenceObject.DeepClone();
			clonedReferenceLayerObject["op"] = lastFrame;
			layersToken.Add(clonedReferenceLayerObject);

			// Overlaying gradient
			var gradientOverlayObject = (JObject) GradientOverlay.DeepClone();
			gradientOverlayObject["op"] = lastFrame;
			var gFillObject = (JObject) gradientOverlayObject["shapes"][0]["it"][2]["g"];
			gFillObject["p"] = rgbValuesLength * 2 - 2;

			var gradientProps = FlagGradients[overlayName];

			gFillObject["k"]["k"] = new JArray(gradientProps);
			layersToken.Add(gradientOverlayObject);

			var clonedReferenceObject = clonedReferenceLayerObject.DeepClone();
			clonedReferenceObject["ind"] = 3;
			layersToken.Add(clonedReferenceObject);

			return tokenizedSticker;
		}

		private static void SetThreadLocale(string languageCode) {
			switch (languageCode) {
				case null:
				case "":
				case "ru":
				case "uk":
				case "be":
					Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfoByIetfLanguageTag("ru-RU");
					break;
				default:
					Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfoByIetfLanguageTag(languageCode);
					break;
			}
		}

		private static async Task<JObject> UnpackAnimatedSticker(Stream content) {
			await using GZipStream gzStream = new(content, CompressionMode.Decompress);

			using StreamReader reader = new(gzStream);
			return await JObject.LoadAsync(new JsonTextReader(reader)).ConfigureAwait(false);
		}
	}
}
