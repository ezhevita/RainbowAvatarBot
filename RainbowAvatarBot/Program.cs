using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

namespace RainbowAvatarBot {
	internal static class Program {
		private const int AdminID = 204723509;

		private static readonly Dictionary<string, Image<Rgba32>> Images = new Dictionary<string, Image<Rgba32>>();
		private static readonly ConcurrentDictionary<int, DateTime> LastUserMessages = new ConcurrentDictionary<int, DateTime>();
		private static readonly SemaphoreSlim ShutdownSemaphore = new SemaphoreSlim(0, 1);
		private static readonly DateTime StartedTime = DateTime.UtcNow;

		private static TelegramBotClient BotClient;
		private static ConcurrentDictionary<int, string> UserSettings = new ConcurrentDictionary<int, string>();

		private static async Task Main() {
			if (File.Exists("config.json")) {
				UserSettings = JsonConvert.DeserializeObject<ConcurrentDictionary<int, string>>(File.ReadAllText("config.json"));
			}

			Log("Starting " + nameof(RainbowAvatarBot));
			string token = File.ReadAllText("token.txt");
			BotClient = new TelegramBotClient(token);
			if (!await BotClient.TestApiAsync().ConfigureAwait(false)) {
				Log("Error when starting bot!");
				return;
			}

			if (!Directory.Exists("images")) {
				Directory.CreateDirectory("images");
			}

			Dictionary<string, List<uint>> flags = JsonConvert.DeserializeObject<Dictionary<string, List<uint>>>(File.ReadAllText("flags.json")).Where(name => !File.Exists(Path.Join("images", name + ".png"))).ToDictionary(x => x.Key, y => y.Value);
			IEnumerable<string> existFiles = Directory.EnumerateFiles("images", "*.png").Select(Path.GetFileNameWithoutExtension);
			if (flags.Keys.Any(name => !existFiles.Contains(name))) {
				GenerateImages(flags);
			}

			LoadImages();

			BotClient.OnMessage += BotOnMessage;
			BotClient.OnCallbackQuery += BotOnCallbackQuery;
			BotClient.StartReceiving(new[] {UpdateType.Message, UpdateType.CallbackQuery});

			Log("Started!");
			Timer clearTimer = new Timer(e => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
			await ShutdownSemaphore.WaitAsync().ConfigureAwait(false);
			clearTimer.Dispose();
			foreach ((_, Image<Rgba32> image) in Images) {
				image.Dispose();
			}

			File.WriteAllText("config.json", JsonConvert.SerializeObject(UserSettings));
			BotClient.StopReceiving();
		}

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
						await BotClient.AnswerCallbackQueryAsync(callbackID, "Invalid name of the flag!").ConfigureAwait(false);
						return;
					}

					if (!UserSettings.TryGetValue(senderID, out _)) {
						UserSettings.TryAdd(senderID, name);
					} else {
						UserSettings[senderID] = name;
					}

					await BotClient.EditMessageTextAsync(message.Chat.Id, message.MessageId, "Changed to " + name + " flag", replyMarkup: InlineKeyboardMarkup.Empty()).ConfigureAwait(false);
					await BotClient.AnswerCallbackQueryAsync(callbackID, "Success!").ConfigureAwait(false);
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
			if (LastUserMessages.TryGetValue(senderID, out DateTime time)) {
				LastUserMessages[senderID] = DateTime.UtcNow;
				if (time.AddSeconds(3) > DateTime.UtcNow) {
					return;
				}
			} else {
				LastUserMessages.TryAdd(senderID, DateTime.UtcNow);
			}

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

			switch (e.Message.Type) {
				case MessageType.Photo when (e.Message.Chat.Type == ChatType.Private) || (args[0].ToUpperInvariant() == "PROCESS"): {
					if (!UserSettings.TryGetValue(senderID, out string imageName)) {
						imageName = "LGBT";
					}

					PhotoSize picture = e.Message.Photo.OrderByDescending(photo => photo.Height).First();
					Log(senderID + "|" + nameof(MessageType.Photo) + "|" + picture.FileId);
					Image<Rgba32> pictureImage;
					using (MemoryStream stream = new MemoryStream()) {
						await BotClient.GetInfoAndDownloadFileAsync(picture.FileId, stream).ConfigureAwait(false);
						pictureImage = Image.Load(stream, new JpegDecoder());
					}

					pictureImage.Overlay(Images[imageName]);
					using (MemoryStream stream = new MemoryStream()) {
						pictureImage.Save(stream, new PngEncoder());
						stream.Position = 0;
						await BotClient.SendPhotoAsync(chatID, new InputMedia(stream, "image.png"), "Here it is! I hope you like the result :D", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
					}

					pictureImage.Dispose();
					break;
				}

				case MessageType.Text: {
					Log(senderID + "|" + nameof(MessageType.Text) + "|" + e.Message.Text);
					string command = args[0].ToUpperInvariant();
					switch (command) {
						case "START": {
							await BotClient.SendTextMessageAsync(chatID, "Welcome to the Avatar Rainbowifier bot 🏳️‍🌈! It can put a" +
							                                             " LGBT flag (or any other which is in our database) on your profile picture in a few seconds, to do it just" +
							                                             " send /avatar. Also you can send your own photo to make it rainbow 🌈. To set another flag for overlay," +
							                                             " enter /settings.\n" +
							                                             " Developer of the bot - @Vital_7", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
							break;
						}

						case "OFF" when senderID == AdminID: {
							ShutdownSemaphore.Release();
							break;
						}

						case "AVATAR": {
							try {
								if (!UserSettings.TryGetValue(senderID, out string imageName)) {
									imageName = "LGBT";
								}

								UserProfilePhotos avatars;
								if (e.Message.ReplyToMessage != null) {
									avatars = await BotClient.GetUserProfilePhotosAsync(e.Message.ReplyToMessage.From.Id, limit: 1).ConfigureAwait(false);
									if (avatars.Photos.Length == 0) {
										await BotClient.SendTextMessageAsync(senderID, "I can't find profile pictures!", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
										return;
									}
								} else {
									avatars = await BotClient.GetUserProfilePhotosAsync(senderID, limit: 1).ConfigureAwait(false);
									if (avatars.Photos.Length == 0) {
										await BotClient.SendTextMessageAsync(senderID, "I can't find your profile picture! Please set it or change your privacy settings so we would be able to see it.", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
										return;
									}
								}

								PhotoSize avatar = avatars.Photos[0].OrderByDescending(photo => photo.Height).First();
								Image<Rgba32> avatarImage;
								using (MemoryStream stream = new MemoryStream()) {
									await BotClient.GetInfoAndDownloadFileAsync(avatar.FileId, stream).ConfigureAwait(false);
									avatarImage = Image.Load(stream, new JpegDecoder());
								}

								avatarImage.Overlay(Images[imageName]);
								using (MemoryStream stream = new MemoryStream()) {
									avatarImage.Save(stream, new PngEncoder());
									stream.Position = 0;
									await BotClient.SendPhotoAsync(chatID, new InputMedia(stream, "avatar.png"), "Here it is! I hope you like the result :D", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
								}

								avatarImage.Dispose();
							} catch (Exception ex) {
								Log(ex.ToString());
								try {
									await BotClient.SendTextMessageAsync(chatID, "Some unknown error occured! Please try again or contact developer.", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
								} catch (Exception) {
									// ignored
								}
							}

							break;
						}

						case "SETTINGS" when (e.Message.Chat.Type == ChatType.Group) || (e.Message.Chat.Type == ChatType.Supergroup): {
							await BotClient.SendTextMessageAsync(chatID, "Settings command must be sent to the private chat!", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
							break;
						}

						case "SETTINGS" when e.Message.Chat.Type == ChatType.Private: {
							await BotClient.SendTextMessageAsync(chatID, "Select flag:", replyMarkup: BuildKeyboard(3, Images.Keys.Select(name => new InlineKeyboardButton {
								CallbackData = "SETTINGS_" + name,
								Text = name + " flag"
							}).OrderBy(button => button.Text)), replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
							break;
						}

						default: {
							if (e.Message.Chat.Type == ChatType.Private) {
								await BotClient.SendTextMessageAsync(chatID, "Unknown command!", replyToMessageId: e.Message.MessageId).ConfigureAwait(false);
							}

							break;
						}
					}


					break;
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
			foreach ((int userID, _) in LastUserMessages.Where(x => x.Value.AddSeconds(3) < DateTime.UtcNow)) {
				LastUserMessages.TryRemove(userID, out _);
			}
		}

		private static void GenerateImages(Dictionary<string, List<uint>> flags) {
			const int flagSize = 1024;
			foreach ((string name, List<uint> rgbValues) in flags) {
				Image<Rgba32> image = new Image<Rgba32>(flagSize, flagSize);
				for (int i = 0; i < rgbValues.Count; i++) {
					int index = i;
					uint rgbValue = rgbValues.ElementAt(index);
					byte r = (byte) (rgbValue / (256 * 256));
					byte g = (byte) (rgbValue / 256 % 256);
					byte b = (byte) (rgbValue % 256);
					image.Mutate(img => img.Fill(new Rgba32(r, g, b), new RectangleF(0, (int) Math.Round((float) flagSize / rgbValues.Count * index), flagSize, (int) Math.Round((float) flagSize / rgbValues.Count * (index + 1)))));
				}

				image.Save(Path.Join("images", $"{name}.png"), new PngEncoder());
				image.Dispose();
			}
		}

		private static void LoadImages() {
			foreach (string file in Directory.EnumerateFiles("images")) {
				string name = Path.GetFileNameWithoutExtension(file);
				Images.Add(name, Image.Load<Rgba32>(Configuration.Default, file));
			}
		}

		private static void Log(string strToLog) {
			string result = $"{DateTime.UtcNow}|{strToLog}";
			Console.WriteLine(result);
			File.AppendAllText("log.txt", result + Environment.NewLine);
		}
	}
}
