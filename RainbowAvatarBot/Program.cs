using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imazen.WebP;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

namespace RainbowAvatarBot {
	internal static class Program {
		private const int AdminID = 204723509;
		private static readonly Dictionary<string, Image> Images = new Dictionary<string, Image>();

		private static readonly ResultCache ResultCache = new ResultCache();

		private static readonly Timer ClearUsersTimer = new Timer(e => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
		private static readonly ConcurrentDictionary<int, DateTime> LastUserImageGenerations = new ConcurrentDictionary<int, DateTime>();
		private static readonly Timer ResetTimer = new Timer(e => ResultCache.Reset(), null, TimeSpan.FromDays(1), TimeSpan.FromDays(1));
		private static readonly SemaphoreSlim ShutdownSemaphore = new SemaphoreSlim(0, 1);
		private static readonly DateTime StartedTime = DateTime.UtcNow;

		private static TelegramBotClient BotClient;
		private static ConcurrentDictionary<int, string> UserSettings = new ConcurrentDictionary<int, string>();

		private static async void BotOnCallbackQuery(object sender, CallbackQueryEventArgs e) {
			if (e?.CallbackQuery == null) {
				return;
			}

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
						await BotClient.AnswerCallbackQueryAsync(callbackID, "Invalid name of the flag!");
						return;
					}

					if (!UserSettings.TryGetValue(senderID, out _)) {
						UserSettings.TryAdd(senderID, name);
					} else {
						UserSettings[senderID] = name;
					}

					await File.WriteAllTextAsync("config.json", JsonConvert.SerializeObject(UserSettings));
					await BotClient.EditMessageTextAsync(message.Chat.Id, message.MessageId, "Changed to " + name + " flag", replyMarkup: InlineKeyboardMarkup.Empty());
					await BotClient.AnswerCallbackQueryAsync(callbackID, "Success!");
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

			string[] args = {""};
			string textMessage = e.Message.Text ?? e.Message.Caption;
			if (!string.IsNullOrEmpty(textMessage)) {
				if (string.IsNullOrEmpty(textMessage) || (textMessage[0] != '/')) {
					return;
				}

				string argumentToProcess = textMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
				if (argumentToProcess.Contains('@')) {
					if (argumentToProcess.Substring(argumentToProcess.IndexOf('@') + 1) != nameof(RainbowAvatarBot)) {
						return;
					}

					argumentToProcess = argumentToProcess.Split('@')[0];
				}

				args = argumentToProcess.Split('_', ',');
				args[0] = args[0].Substring(1);
			}

			try {
				switch (e.Message.Type) {
					case MessageType.Sticker when e.Message.Chat.Type == ChatType.Private:
					case MessageType.Photo when (e.Message.Chat.Type == ChatType.Private) || (args[0].ToUpperInvariant() == "COLORIZE"): {
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
						break;
					}

					case MessageType.Text: {
						Log(senderID + "|" + nameof(MessageType.Text) + "|" + e.Message.Text);
						string command = args[0].ToUpperInvariant();
						switch (command) {
							case "START": {
								await BotClient.SendTextMessageAsync(chatID, "Welcome to the Avatar Rainbowifier bot 🏳️‍🌈! It can put a " +
								                                             "LGBT flag (or any other which is in our database) on your profile picture in a few seconds, to do it just " +
								                                             "send /avatar. Also you can send your own photo to make it rainbow 🌈. To set another flag for overlay, " +
								                                             "enter /settings.\n" +
								                                             "Developer of the bot - @Vital_7", replyToMessageId: e.Message.MessageId);
								break;
							}

							case "OFF" when senderID == AdminID: {
								ShutdownSemaphore.Release();
								break;
							}

							case "AVATAR": {
								try {
									if (!UserSettings.TryGetValue(senderID, out string overlayName)) {
										overlayName = "LGBT";
									}

									UserProfilePhotos avatars;
									if (e.Message.ReplyToMessage != null) {
										avatars = await BotClient.GetUserProfilePhotosAsync(e.Message.ReplyToMessage.From.Id, limit: 1);
										if (avatars.Photos.Length == 0) {
											await BotClient.SendTextMessageAsync(senderID, "I can't find profile pictures!", replyToMessageId: e.Message.MessageId);
											return;
										}
									} else {
										avatars = await BotClient.GetUserProfilePhotosAsync(senderID, limit: 1);
										if (avatars.Photos.Length == 0) {
											await BotClient.SendTextMessageAsync(senderID, "I can't find your profile picture! Please set it or change your privacy settings so I would be able to see it.", replyToMessageId: e.Message.MessageId);
											return;
										}
									}

									PhotoSize sourceImage = avatars.Photos[0].OrderByDescending(photo => photo.Height).First();
									await ProcessAndSend(sourceImage.FileId, sourceImage.FileUniqueId, overlayName, e.Message);
								} catch (Exception ex) {
									Log(ex.ToString());
									try {
										await BotClient.SendTextMessageAsync(chatID, "Some unknown error occured! Please try again or contact developer.", replyToMessageId: e.Message.MessageId);
									} catch (Exception) {
										// ignored
									}
								}

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
								await BotClient.SendTextMessageAsync(chatID, "Settings command must be sent to the private chat!", replyToMessageId: e.Message.MessageId);
								break;
							}

							case "SETTINGS" when e.Message.Chat.Type == ChatType.Private: {
								await BotClient.SendTextMessageAsync(chatID, "Select flag:", replyMarkup: BuildKeyboard(3, Images.Keys.Select(name => new InlineKeyboardButton {
									CallbackData = "SETTINGS_" + name,
									Text = name + " flag"
								}).OrderBy(button => button.Text)), replyToMessageId: e.Message.MessageId);
								break;
							}

							default: {
								if (e.Message.Chat.Type == ChatType.Private) {
									await BotClient.SendTextMessageAsync(chatID, "Unknown command!", replyToMessageId: e.Message.MessageId);
								}

								break;
							}
						}


						break;
					}
				}
			} catch (Exception ex) {
				Log("Exception has been thrown!");
				Log(ex.ToString());
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

		private static async Task<Image> DownloadImageByFileID(string fileID) {
			MemoryStream stream = new MemoryStream();
			await BotClient.GetInfoAndDownloadFileAsync(fileID, stream);
			stream.Position = 0;
			// WebP check
			byte[] buffer = new byte[4];
			await stream.ReadAsync(buffer, 0, 4).ConfigureAwait(false);
			if (buffer.SequenceEqual(new byte[] {82, 73, 70, 70})) {
				// RIFF
				await stream.ReadAsync(new byte[4], 0, 4).ConfigureAwait(false);
				await stream.ReadAsync(buffer, 0, 4).ConfigureAwait(false);
				if (buffer.SequenceEqual(new byte[] {87, 69, 66, 80})) {
					// WEBP
					stream.Position = 0;
					byte[] content = new byte[stream.Length];
					await stream.ReadAsync(content, 0, (int) stream.Length);
					SimpleDecoder decoder = new SimpleDecoder();
					return decoder.DecodeFromBytes(content, content.Length);
				}
			}

			return Image.FromStream(stream);
		}

		private static void GenerateImages(Dictionary<string, uint[]> flags) {
			foreach ((string name, uint[] rgbValues) in flags) {
				using Bitmap image = new Bitmap(1, rgbValues.Length);
				using Graphics graphics = Graphics.FromImage(image);

				byte index = 0;
				foreach (uint rgbValue in rgbValues) {
					using SolidBrush brush = new SolidBrush(Color.FromArgb((byte) (rgbValue >> 16), (byte) ((rgbValue >> 8) & 0xFF), (byte) (rgbValue & 0xFF)));
					graphics.FillRectangle(brush, new RectangleF(0, index, 1, 1));
					index++;
				}

				image.Save(Path.Join("images", $"{name}.png"), ImageFormat.Png);
			}
		}

		private static void LoadImages() {
			foreach (string file in Directory.EnumerateFiles("images")) {
				string name = Path.GetFileNameWithoutExtension(file);
				Images.Add(name, Image.FromFile(file));
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
			string token = File.ReadAllText("token.txt");
			BotClient = new TelegramBotClient(token);
			if (!await BotClient.TestApiAsync()) {
				Log("Error when starting bot!");
				return;
			}

			if (!Directory.Exists("images")) {
				Directory.CreateDirectory("images");
			}

			Dictionary<string, uint[]> flags = JsonConvert.DeserializeObject<Dictionary<string, uint[]>>(File.ReadAllText("flags.json")).Where(name => !File.Exists(Path.Join("images", name + ".png"))).ToDictionary(x => x.Key, y => y.Value);
			IEnumerable<string> existFiles = Directory.EnumerateFiles("images", "*.png").Select(Path.GetFileNameWithoutExtension);
			if (flags.Keys.Any(name => !existFiles.Contains(name))) {
				GenerateImages(flags);
			}

			LoadImages();

			BotClient.OnMessage += BotOnMessage;
			BotClient.OnCallbackQuery += BotOnCallbackQuery;
			BotClient.StartReceiving(new[] {UpdateType.Message, UpdateType.CallbackQuery});

			Log("Started!");
			await ShutdownSemaphore.WaitAsync();
			ClearUsersTimer.Dispose();
			ResetTimer.Dispose();
			foreach ((_, Image image) in Images) {
				image.Dispose();
			}

			BotClient.StopReceiving();
		}

		private static async Task ProcessAndSend(string imageID, string imageUniqueID, string overlayName, Message message) {
			Log(message.From.Id + "|" + nameof(MessageType.Photo) + "|" + imageID);

			bool isSticker = message.Type == MessageType.Sticker;
			if (isSticker && message.Sticker.IsAnimated) {
				await BotClient.SendTextMessageAsync(message.Chat.Id, "Animated stickers are not supported yet.", replyToMessageId: message.MessageId);
				return;
			}

			bool isCached;
			long elapsedMs = 0;
			InputMedia resultImage;
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

				MemoryStream processedImage;
				(processedImage, elapsedMs) = await ProcessImage(imageID, overlayName);
				resultImage = new InputMedia(processedImage, "image.png");
			}

			if (isSticker) {
				Message resultMessage = await BotClient.SendStickerAsync(message.Chat.Id, resultImage).ConfigureAwait(false);
				await BotClient.SendTextMessageAsync(message.Chat.Id, $"Here it is! I hope you like the result :D (generated in {elapsedMs} ms)", replyToMessageId: message.ReplyToMessage?.MessageId ?? message.MessageId);
				if (!isCached) {
					ResultCache.TryAdd(imageUniqueID, overlayName, resultMessage.Sticker.FileId);
				}
			} else {
				Message resultMessage = await BotClient.SendPhotoAsync(message.Chat.Id, resultImage, $"Here it is! I hope you like the result :D (generated in {elapsedMs} ms)", replyToMessageId: message.ReplyToMessage?.MessageId ?? message.MessageId);
				if (!isCached) {
					ResultCache.TryAdd(imageUniqueID, overlayName, resultMessage.Photo.OrderByDescending(photo => photo.Height).First().FileId);
				}
			}
		}

		private static async Task<(MemoryStream Image, long Milliseconds)> ProcessImage(string fileId, string overlayName) {
			using Image image = await DownloadImageByFileID(fileId);
			Stopwatch sw = Stopwatch.StartNew();
			image.Overlay(Images[overlayName]);
			sw.Stop();
			return (image.SaveToPng(), sw.ElapsedMilliseconds);
		}
	}
}
