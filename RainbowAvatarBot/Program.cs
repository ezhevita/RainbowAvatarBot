using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

#if SYSTEMDRAWING
using System.Drawing;
using System.Drawing.Imaging;
#else
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using RectangleF = SixLabors.Primitives.RectangleF;
#endif

namespace RainbowAvatarBot {
	internal static class Program {
		private const int AdminID = 204723509;
		private static readonly Dictionary<string, Image> Images = new Dictionary<string, Image>();

		private static readonly ResultCache ResultCache = new ResultCache();

		private static readonly ConcurrentDictionary<int, DateTime> LastUserMessages = new ConcurrentDictionary<int, DateTime>();
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
			if (LastUserMessages.TryGetValue(senderID, out DateTime time)) {
				LastUserMessages[senderID] = DateTime.UtcNow;
				if (time.AddSeconds(1) > DateTime.UtcNow) {
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
				case MessageType.Photo when (e.Message.Chat.Type == ChatType.Private) || (args[0].ToUpperInvariant() == "COLORIZE"): {
					if (!UserSettings.TryGetValue(senderID, out string overlayName)) {
						overlayName = "LGBT";
					}

					PhotoSize sourceImage = e.Message.Photo.OrderByDescending(photo => photo.Height).First();
					await ProcessAndSend(sourceImage.FileId, overlayName, e.Message);
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
								await ProcessAndSend(sourceImage.FileId, overlayName, e.Message);
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

						case "COLORIZE" when e.Message.ReplyToMessage?.Type == MessageType.Photo: {
							if (!UserSettings.TryGetValue(senderID, out string overlayName)) {
								overlayName = "LGBT";
							}

							PhotoSize sourceImage = e.Message.ReplyToMessage.Photo.OrderByDescending(photo => photo.Height).First();
							await ProcessAndSend(sourceImage.FileId, overlayName, e.Message);
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
		}
		
		private static async Task ProcessAndSend(string imageID, string overlayName, Message message)
		{
			Log(message.From.Id + "|" + nameof(MessageType.Photo) + "|" + imageID);

			InputMedia resultImage = ResultCache.TryGetValue(imageID, overlayName, out string cachedResultImageID) ? cachedResultImageID : new InputMedia(await ProcessImage(imageID, overlayName), "image.png");
			Message resultMessage = await BotClient.SendPhotoAsync(message.Chat.Id, resultImage, "Here it is! I hope you like the result :D", replyToMessageId: message.ReplyToMessage?.MessageId ?? message.MessageId);

			if (cachedResultImageID == null)
				ResultCache.TryAdd(imageID, overlayName, resultMessage.Photo.OrderByDescending(photo => photo.Height).First().FileId);
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

		private static async Task<MemoryStream> ProcessImage(string fileId, string overlayName) {
			using Image image = await DownloadImageByFileID(fileId);
					
			image.Overlay(Images[overlayName]);
			
			return image.SaveToPng();
		}

		private static void ClearUsers() {
			foreach ((int userID, _) in LastUserMessages.Where(x => x.Value.AddSeconds(3) < DateTime.UtcNow)) {
				LastUserMessages.TryRemove(userID, out _);
			}
		}

		private static async Task<Image> DownloadImageByFileID(string fileID) {
			MemoryStream stream = new MemoryStream();
			await BotClient.GetInfoAndDownloadFileAsync(fileID, stream);

		#if SYSTEMDRAWING
			return Image.FromStream(stream);
		#else
			try {
				return SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
			} finally {
				stream.Dispose();
			}
		#endif
		}

		private static void GenerateImages(Dictionary<string, uint[]> flags) {
			const int flagSize = 1024;
			foreach ((string name, uint[] rgbValues) in flags) {
			#if SYSTEMDRAWING
				using Bitmap image = new Bitmap(flagSize, flagSize);
				using Graphics graphics = Graphics.FromImage(image);

				byte index = 0;
				foreach (uint rgbValue in rgbValues) {
					using SolidBrush brush = new SolidBrush(Color.FromArgb((byte) (rgbValue >> 16), (byte) ((rgbValue >> 8) & 0xFF), (byte) (rgbValue & 0xFF)));
					graphics.FillRectangle(brush, new RectangleF(0, (int) Math.Round((float) flagSize / rgbValues.Length * index), flagSize, (int) Math.Round((float) flagSize / rgbValues.Length * (index + 1))));
					index++;
				}

				image.Save(Path.Join("images", $"{name}.png"), ImageFormat.Png);
			#else
				using Image<Rgba32> image = new Image<Rgba32>(flagSize, flagSize);
				byte index = 0;
				foreach (uint rgbValue in rgbValues) {
					byte r = (byte) (rgbValue >> 16);
					byte g = (byte) ((rgbValue >> 8) & 0xFF);
					byte b = (byte) (rgbValue & 0xFF);
					byte i = index;
					image.Mutate(img => img.Fill(new Rgba32(r, g, b), new RectangleF(0, (int) Math.Round((float) flagSize / rgbValues.Length * i), flagSize, (int) Math.Round((float) flagSize / rgbValues.Length * (i + 1)))));
					index++;
				}

				image.Save(Path.Join("images", $"{name}.png"), new PngEncoder());
			#endif
			}
		}

		private static void LoadImages() {
			foreach (string file in Directory.EnumerateFiles("images")) {
				string name = Path.GetFileNameWithoutExtension(file);
			#if SYSTEMDRAWING
				Images.Add(name, Image.FromFile(file));
			#else
				Images.Add(name, SixLabors.ImageSharp.Image.Load<Rgba32>(Configuration.Default, file));
			#endif
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
			Timer clearTimer = new Timer(e => ClearUsers(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
			await ShutdownSemaphore.WaitAsync();
			clearTimer.Dispose();
			foreach ((_, Image image) in Images) {
				image.Dispose();
			}

			File.WriteAllText("config.json", JsonConvert.SerializeObject(UserSettings));
			BotClient.StopReceiving();
		}
	}
}
