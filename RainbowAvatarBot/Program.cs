using System;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

namespace RainbowAvatarBot {
	internal static class Program {
		private const int AdminID = 204723509;
		private static readonly Dictionary<string, Image> Images = new();

		private static readonly ResultCache ResultCache = new();

		private static readonly Timer ClearUsersTimer = new(_ => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
		private static readonly ConcurrentDictionary<int, DateTime> LastUserImageGenerations = new();
		private static readonly Timer ResetTimer = new(_ => ResultCache.Reset(), null, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
		private static readonly SemaphoreSlim ShutdownSemaphore = new(0, 1);
		private static readonly DateTime StartedTime = DateTime.UtcNow;

		private static readonly HashSet<MessageType> SupportedTypes = new(3) {
			MessageType.Photo,
			MessageType.Sticker
		};

		private static TelegramBotClient BotClient;
		private static string BotUsername;
		private static ConcurrentDictionary<int, string> UserSettings = new();
		private static JObject GradientOverlay;
		private static JObject ReferenceObject;
		private static Dictionary<string, uint[]> Flags;
		private static Dictionary<string, float[]> FlagGradients;
		private static HttpClient HttpClient;

		private static async void BotOnCallbackQuery(object sender, CallbackQueryEventArgs e) {
			if (e?.CallbackQuery == null) {
				return;
			}

			SetThreadLocale(e.CallbackQuery.From.LanguageCode);

			string callbackID = e.CallbackQuery.Id;
			int senderID = e.CallbackQuery.From.Id;
			string[] args = e.CallbackQuery.Data.Split('_', StringSplitOptions.RemoveEmptyEntries);
			if (args.Length < 1) {
				return;
			}

			Message message = e.CallbackQuery.Message;
			switch (args[0]) {
				case "SETTINGS": {
					string name = args[1];
					if (!Images.ContainsKey(name)) {
						await BotClient.AnswerCallbackQueryAsync(callbackID, Localization.InvalidFlagName);
						return;
					}

					if (!UserSettings.TryGetValue(senderID, out _)) {
						UserSettings.TryAdd(senderID, name);
					} else {
						UserSettings[senderID] = name;
					}

					await File.WriteAllTextAsync("config.json", JsonConvert.SerializeObject(UserSettings));
					await BotClient.EditMessageTextAsync(message.Chat.Id, message.MessageId, Localization.ChangedSuccessfully, replyMarkup: InlineKeyboardMarkup.Empty());
					await BotClient.AnswerCallbackQueryAsync(callbackID, Localization.Success);
					break;
				}
			}
		}

		private static async void BotOnMessage(object sender, MessageEventArgs e) {
			if ((e?.Message == null) || (e.Message.Date < StartedTime)) {
				return;
			}

			int senderID = e.Message.From.Id;
			long chatID = e.Message.Chat.Id;

			SetThreadLocale(e.Message.From.LanguageCode);

			string[] args = {""};
			string textMessage = e.Message.Text ?? e.Message.Caption;

			if (!string.IsNullOrEmpty(textMessage) && (textMessage[0] == '/')) {
				string argumentToProcess = textMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
				if (argumentToProcess.Contains('@')) {
					if (argumentToProcess.Substring(argumentToProcess.IndexOf('@') + 1) != BotUsername) {
						return;
					}

					argumentToProcess = argumentToProcess.Split('@')[0];
				}

				args = argumentToProcess.Split('_', ',');
				args[0] = args[0].Substring(1);
			} else if (e.Message.Chat.Type != ChatType.Private) {
				return;
			}

			try {
				if (e.Message.Type == MessageType.Text) {
					string command = args[0].ToUpperInvariant();
					switch (command) {
						case "OFF" when senderID == AdminID: {
							ShutdownSemaphore.Release();
							break;
						}

						case "AVATAR": {
							if (!UserSettings.TryGetValue(senderID, out string overlayName)) {
								overlayName = "LGBT";
							}

							UserProfilePhotos avatars;
							if (e.Message.ReplyToMessage != null) {
								avatars = await BotClient.GetUserProfilePhotosAsync(e.Message.ReplyToMessage.From.Id, limit: 1);
								if (avatars.Photos.Length == 0) {
									await BotClient.SendTextMessageAsync(senderID, Localization.RepliedUserProfilePictureNotFound, replyToMessageId: e.Message.MessageId);
									return;
								}
							} else {
								avatars = await BotClient.GetUserProfilePhotosAsync(senderID, limit: 1);
								if (avatars.Photos.Length == 0) {
									await BotClient.SendTextMessageAsync(senderID, Localization.UserProfilePictureNotFound, replyToMessageId: e.Message.MessageId);
									return;
								}
							}

							PhotoSize sourceImage = avatars.Photos[0].OrderByDescending(photo => photo.Height).First();
							await ProcessAndSend(sourceImage.FileId, sourceImage.FileUniqueId, overlayName, e.Message);
							break;
						}

						case "COLORIZE" when (e.Message.ReplyToMessage?.Type == MessageType.Photo) || (e.Message.ReplyToMessage?.Type == MessageType.Sticker): {
							if (!UserSettings.TryGetValue(senderID, out string overlayName)) {
								overlayName = "LGBT";
							}

							Message targetMessage = e.Message.ReplyToMessage;
							string fileID;
							string uniqueFileID;
							if (targetMessage.Type == MessageType.Photo) {
								PhotoSize photo = targetMessage.Photo.OrderByDescending(size => size.Height).First();
								fileID = photo.FileId;
								uniqueFileID = photo.FileUniqueId;
							} else {
								Sticker sticker = targetMessage.Sticker;
								fileID = sticker.FileId;
								uniqueFileID = sticker.FileUniqueId;
							}

							await ProcessAndSend(fileID, uniqueFileID, overlayName, e.Message.ReplyToMessage);
							break;
						}

						case "SETTINGS" when (e.Message.Chat.Type == ChatType.Group) || (e.Message.Chat.Type == ChatType.Supergroup): {
							await BotClient.SendTextMessageAsync(chatID, Localization.SettingsSentToChat, replyToMessageId: e.Message.MessageId);
							break;
						}

						case "SETTINGS" when e.Message.Chat.Type == ChatType.Private: {
							await BotClient.SendTextMessageAsync(chatID, Localization.SelectFlag, replyMarkup: BuildKeyboard(3, Images.Keys.Select(name => new InlineKeyboardButton {
								CallbackData = "SETTINGS_" + name,
								Text = Localization.ResourceManager.GetString(name)
							}).OrderBy(button => button.Text)), replyToMessageId: e.Message.MessageId);
							break;
						}

						default: {
							if (e.Message.Chat.Type == ChatType.Private) {
								await BotClient.SendTextMessageAsync(chatID, Localization.StartMessage, replyToMessageId: e.Message.MessageId, parseMode: ParseMode.MarkdownV2);
							}

							break;
						}
					}
				} else if (SupportedTypes.Contains(e.Message.Type) && ((e.Message.Chat.Type == ChatType.Private) || ((args.Length > 0) && (args[0].ToUpperInvariant() == "COLORIZE")))) {
					if (!UserSettings.TryGetValue(senderID, out string overlayName)) {
						overlayName = "LGBT";
					}

					string fileID;
					string uniqueFileID;
					if (e.Message.Type == MessageType.Photo) {
						PhotoSize photo = e.Message.Photo.OrderByDescending(size => size.Height).First();
						fileID = photo.FileId;
						uniqueFileID = photo.FileUniqueId;
					} else {
						Sticker sticker = e.Message.Sticker;
						fileID = sticker.FileId;
						uniqueFileID = sticker.FileUniqueId;
					}

					await ProcessAndSend(fileID, uniqueFileID, overlayName, e.Message);
				}
			} catch (Exception ex) {
				Log("Exception has been thrown!");
				Log(ex.ToString());
				try {
					await BotClient.SendTextMessageAsync(chatID, Localization.ErrorOccured, replyToMessageId: e.Message.MessageId);
				} catch (Exception) {
					// ignored
				}
			}
		}

		private static InlineKeyboardMarkup BuildKeyboard(byte width, IEnumerable<InlineKeyboardButton> buttons) {
			InlineKeyboardButton[] inlineKeyboardButtons = buttons.ToArray();
			int buttonCount = inlineKeyboardButtons.Length;
			uint rowAmount = (uint) Math.Ceiling(buttonCount / (double) width);
			int remainder = buttonCount % width;
			InlineKeyboardButton[][] buttonRows = new InlineKeyboardButton[rowAmount][];
			for (int row = 0; row < rowAmount; row++) {
				int columnAmount = (rowAmount == row + 1) && (remainder > 0) ? remainder : width;
				InlineKeyboardButton[] rowButtons = new InlineKeyboardButton[columnAmount];
				for (int column = 0; column < columnAmount; column++) {
					rowButtons[column] = inlineKeyboardButtons[row * width + column];
				}

				buttonRows[row] = rowButtons;
			}

			return new InlineKeyboardMarkup(buttonRows);
		}

		private static void ClearUsers() {
			foreach ((int userID, _) in LastUserImageGenerations.Where(x => x.Value.AddSeconds(3) < DateTime.UtcNow)) {
				LastUserImageGenerations.TryRemove(userID, out _);
			}
		}

		private static async Task<(Stream Result, long Length)> DownloadFile(string fileID) {
			Telegram.Bot.Types.File file = await BotClient.GetFileAsync(fileID);
			HttpResponseMessage responseMessage = await HttpClient.GetAsync(file.FilePath, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

			responseMessage.EnsureSuccessStatusCode();
			return (await responseMessage.Content.ReadAsStreamAsync(), responseMessage.Content.Headers.ContentLength.GetValueOrDefault(0));
		}

		private static float[] GenerateGradient(IReadOnlyCollection<uint> rgbValues) {
			float[] gradientProps = new float[(rgbValues.Count * 2 - 2) * 4];
			byte index = 0;
			foreach (uint rgbValue in rgbValues) {
				void FillLine(byte startIndex, byte indexInc) {
					gradientProps[startIndex] = (float) Math.Round((float) (index + indexInc) / rgbValues.Count, 3);
					gradientProps[startIndex + 1] = (float) Math.Round((rgbValue >> 16) / 255.0, 3);
					gradientProps[startIndex + 2] = (float) Math.Round(((rgbValue >> 8) & 0xFF) / 255.0, 3);
					gradientProps[startIndex + 3] = (float) Math.Round((rgbValue & 0xFF) / 255.0, 3);
				}

				if (index > 0) {
					FillLine((byte) ((index * 2 - 1) * 4), 0);
				}

				if (index < rgbValues.Count - 1) {
					FillLine((byte) (index * 2 * 4), 1);
				}

				index++;
			}

			return gradientProps;
		}

		private static void GenerateImages(Dictionary<string, uint[]> flags) {
			foreach ((string name, uint[] rgbValues) in flags) {
				using Image<Rgba32> image = new(1, rgbValues.Length);
				byte index = 0;
				foreach (uint rgbValue in rgbValues) {
					byte indexCopy = index;

					image.Mutate(img => img.Fill(new Argb32((byte) (rgbValue >> 16), (byte) ((rgbValue >> 8) & 0xFF), (byte) (rgbValue & 0xFF)), new RectangleF(0, indexCopy, 1, 1)));
					index++;
				}

				image.Save(Path.Join("images", $"{name}.png"), new PngEncoder());
			}
		}

		private static async Task LoadImages() {
			foreach (string file in Directory.EnumerateFiles("images")) {
				string name = Path.GetFileNameWithoutExtension(file);
				Images.Add(name, await Image.LoadAsync(file));
			}
		}

		private static void Log(string strToLog) {
			string result = $"{DateTime.UtcNow}|{strToLog}";
			Console.WriteLine(result);
			File.AppendAllText("log.txt", result + Environment.NewLine);
		}

		private static async Task Main() {
			if (File.Exists("config.json")) {
				UserSettings = JsonConvert.DeserializeObject<ConcurrentDictionary<int, string>>(File.ReadAllText("config.json"));
			}

			Log("Starting " + nameof(RainbowAvatarBot));
			string token = await File.ReadAllTextAsync("token.txt");
			BotClient = new TelegramBotClient(token);
			if (!await BotClient.TestApiAsync()) {
				Log("Error when starting bot!");
				return;
			}

			BotUsername = (await BotClient.GetMeAsync()).Username;

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

			GradientOverlay = JObject.Parse(await File.ReadAllTextAsync("gradientOverlay.json"));
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

			Flags = JsonConvert.DeserializeObject<Dictionary<string, uint[]>>(await File.ReadAllTextAsync("flags.json")).Where(name => !File.Exists(Path.Join("images", name + ".png"))).ToDictionary(x => x.Key, y => y.Value);
			IEnumerable<string> existFiles = Directory.EnumerateFiles("images", "*.png").Select(Path.GetFileNameWithoutExtension);
			if (Flags.Keys.Any(name => !existFiles.Contains(name))) {
				GenerateImages(Flags);
			}

			await LoadImages();

			FlagGradients = new Dictionary<string, float[]>(Flags.Count);
			foreach ((string flagName, uint[] rgbValues) in Flags) {
				FlagGradients[flagName] = GenerateGradient(rgbValues);
			}

			BotClient.OnMessage += BotOnMessage;
			BotClient.OnCallbackQuery += BotOnCallbackQuery;
			BotClient.StartReceiving(new[] {UpdateType.Message, UpdateType.CallbackQuery});

			Log($"Started {BotUsername}!");
			await ShutdownSemaphore.WaitAsync();
			await ClearUsersTimer.DisposeAsync();
			await ResetTimer.DisposeAsync();
			foreach ((_, Image image) in Images) {
				image.Dispose();
			}

			BotClient.StopReceiving();
		}

		private static async Task<Stream> PackAnimatedSticker(JToken content) {
			await using MemoryStream memoryStream = new();
			MemoryStream resultStream = new();

			await using (StreamWriter streamWriter = new(memoryStream, leaveOpen: true))
			using (JsonTextWriter jsonTextWriter = new(streamWriter) {
				Formatting = Formatting.None,
				AutoCompleteOnClose = true,
				CloseOutput = false
			}) {
				await content.WriteToAsync(jsonTextWriter);
			}

			memoryStream.Position = 0;

			GZip.Compress(memoryStream, resultStream, false, 4096, 9);
			resultStream.Position = 0;

			return resultStream;
		}

		private static async Task ProcessAndSend(string imageID, string imageUniqueID, string overlayName, Message message) {
			MediaType mediaType = message.Type == MessageType.Sticker ? message.Sticker.IsAnimated ? MediaType.AnimatedSticker : MediaType.Sticker : MediaType.Picture;
			Log(message.From.Id + "|" + mediaType + "|" + imageID);

			bool isSticker = message.Type == MessageType.Sticker;

			Stopwatch sw = null;
			bool isCached;
			InputMedia resultImage;
			Message processMessage = null;
			// ReSharper disable once AssignmentInConditionalExpression
			if (isCached = ResultCache.TryGetValue(imageUniqueID, overlayName, out string cachedResultImageID)) {
				resultImage = cachedResultImageID;
			} else {
				int senderID = message.From.Id;
				if (LastUserImageGenerations.TryGetValue(senderID, out DateTime time)) {
					LastUserImageGenerations[senderID] = DateTime.UtcNow;
					if (time.AddSeconds(1) > DateTime.UtcNow) {
						return;
					}
				} else {
					LastUserImageGenerations.TryAdd(senderID, DateTime.UtcNow);
				}

				processMessage = await BotClient.SendTextMessageAsync(message.Chat.Id, Localization.Processing);
				sw = Stopwatch.StartNew();
				resultImage = await ProcessImage(imageID, overlayName, mediaType);
			}

			Message resultMessage = isSticker ? await BotClient.SendStickerAsync(message.Chat.Id, resultImage) : 
				await BotClient.SendPhotoAsync(message.Chat.Id, resultImage, replyToMessageId: message.ReplyToMessage?.MessageId ?? message.MessageId);

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
				await BotClient.DeleteMessageAsync(resultMessage.Chat, resultMessage.MessageId);
				await BotClient.SendTextMessageAsync(message.Chat.Id, Localization.UnableToSend);
				return;
			}

			if (!isCached) {
				ResultCache.TryAdd(imageUniqueID, overlayName, isSticker ? resultMessage.Sticker.FileId : 
					resultMessage.Photo.OrderByDescending(photo => photo.Height).First().FileId);
			}
		}

		private static async Task<InputMedia> ProcessImage(string fileId, string overlayName, MediaType mediaType) {
			(Stream Result, long Length) fileData = await DownloadFile(fileId);
			await using Stream file = fileData.Result;

			if (mediaType == MediaType.AnimatedSticker) {
				JObject stickerObject = await UnpackAnimatedSticker(file);
				JObject processedAnimation = ProcessLottieAnimation(stickerObject, overlayName);
				InputMedia inputMediaAnimated = new(await PackAnimatedSticker(processedAnimation), "sticker.tgs");
				return inputMediaAnimated;
			}

			Image image;
			if (mediaType == MediaType.Sticker) {
				MemoryStream memoryStream = new((int) fileData.Length);
				await file.CopyToAsync(memoryStream);

				byte[] fileArray = memoryStream.ToArray();
				(int width, int height) = WebPDecoder.GetWebPInfo(fileArray);

				image = Image.WrapMemory<Argb32>(WebPDecoder.DecodeFromBytes(memoryStream.ToArray(), width, height), width, height);
			} else {
				image = await Image.LoadAsync(file);
			}

			InputMedia inputMedia = new(await image.SaveToPng(), "image.png");
			return inputMedia;
		}

		private static JObject ProcessLottieAnimation(JObject tokenizedSticker, string overlayName) {
			JArray layersToken = (JArray) tokenizedSticker["layers"];
			JArray assetsToken = (JArray) tokenizedSticker["assets"];

			int rgbValuesLength = Flags[overlayName].Length;

			// Packing main animation to asset
			JObject assetToken = new() {
				["id"] = "_",
				["layers"] = layersToken
			};

			// Getting last frame
			ushort lastFrame = tokenizedSticker["op"].Value<ushort>();

			layersToken.RemoveAll();
			assetsToken.Add(assetToken);

			// Layer that reference main animation from assets
			JToken clonedReferenceLayerObject = ReferenceObject.DeepClone();
			clonedReferenceLayerObject["op"] = lastFrame;
			layersToken.Add(clonedReferenceLayerObject);

			// Overlaying gradient
			JObject gradientOverlayObject = (JObject) GradientOverlay.DeepClone();
			gradientOverlayObject["op"] = lastFrame;
			JObject gFillObject = (JObject) gradientOverlayObject["shapes"][0]["it"][2]["g"];
			gFillObject["p"] = rgbValuesLength * 2 - 2;

			float[] gradientProps = FlagGradients[overlayName];

			gFillObject["k"]["k"] = new JArray(gradientProps);
			layersToken.Add(gradientOverlayObject);

			JToken clonedReferenceObject = clonedReferenceLayerObject.DeepClone();
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
			return await JObject.LoadAsync(new JsonTextReader(reader));
		}
	}
}
