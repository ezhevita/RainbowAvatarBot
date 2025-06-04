using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using RainbowAvatarBot;
using RainbowAvatarBot.Commands;
using RainbowAvatarBot.Configuration;
using RainbowAvatarBot.Processors;
using RainbowAvatarBot.Services;
using SixLabors.ImageSharp;
using Telegram.Bot;
using Telegram.Bot.Polling;

var host = Host.CreateDefaultBuilder(args)
	.ConfigureServices(
		(context, services) =>
		{
			services.Configure<BotConfiguration>(context.Configuration.GetSection("Bot"));
			services.Configure<ProcessingConfiguration>(context.Configuration.GetSection("Processing"));

			services.AddHttpClient("telegram_bot_client")
				.AddTypedClient<ITelegramBotClient>(
					(httpClient, sp) =>
					{
						var botConfig = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
						var options = new TelegramBotClientOptions(botConfig.Token);

						return new TelegramBotClient(options, httpClient);
					}
				);

			services.AddMemoryCache(options => options.SizeLimit = 1000);
			services.AddSingleton(TimeProvider.System);
			services.AddSingleton<Dictionary<string, Image>>();
			services.AddSingleton<FlagImageService>();
			services.AddSingleton<UserSettingsService>();
			services.AddSingleton<RateLimitingService>();
			services.AddSingleton<RecyclableMemoryStreamManager>();

			services.AddScoped<BotUserData>();
			services.AddScoped<IUpdateHandler, UpdateHandler>();
			services.AddScoped<ReceiverService>();
			services.AddScoped<IProcessor, AnimatedStickerProcessor>();
			services.AddScoped<IProcessor, ImageProcessor>();
			services.AddScoped<IProcessor, VideoStickerProcessor>();
			services.AddScoped<ProcessorHandler>();
			services.AddScoped<ICommand, AvatarCommand>();
			services.AddScoped<ICommand, ColorizeCommand>();
			services.AddScoped<ICommand, SettingsCommand>();
			services.AddScoped<ICommand, StartCommand>();
			services.AddScoped<CommandHandler>();
			services.AddSingleton<Bot>();

			services.AddHostedService<InitializationHostedService>();
			services.AddHostedService<PollingService>();
		}
	)
	.Build();

await host.Services.GetRequiredService<Bot>().Init();

await host.RunAsync();
