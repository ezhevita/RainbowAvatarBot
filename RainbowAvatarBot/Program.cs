using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using RainbowAvatarBot;
using RainbowAvatarBot.Commands;
using RainbowAvatarBot.Configuration;
using RainbowAvatarBot.Database;
using RainbowAvatarBot.Processors;
using RainbowAvatarBot.Services;
using SixLabors.ImageSharp;
using Telegram.Bot;
using Telegram.Bot.Polling;

using var host = Host.CreateDefaultBuilder(args)
	.ConfigureServices((context, services) =>
		{
			services.AddValidatableOptions<BotOptions, BotOptionsValidator>();
			services.AddValidatableOptions<ProcessingOptions, ProcessingOptionsValidator>();

			services.AddHttpClient("telegram_bot_client")
				.AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
					{
						var botConfig = sp.GetRequiredService<IOptions<BotOptions>>().Value;
						var options = new TelegramBotClientOptions(botConfig.Token);

						return new TelegramBotClient(options, httpClient);
					}
				);

			services.AddMemoryCache(options => options.SizeLimit = 1000);
			services.AddSingleton(TimeProvider.System);
			services.AddSingleton<Dictionary<string, Image>>();
			services.AddSingleton<FlagImageService>();
			services.AddPooledDbContextFactory<UserSettingsDbContext>(options =>
			{
				var connectionString = context.Configuration.GetConnectionString("PostgreSQL");
				if (string.IsNullOrWhiteSpace(connectionString))
				{
					throw new InvalidOperationException("Configure ConnectionStrings:PostgreSQL before starting the bot.");
				}

				options.UseNpgsql(connectionString);
			});
			services.AddSingleton<UserSettingsService>();
			services.AddSingleton<RateLimitingService>();
			services.AddSingleton<RecyclableMemoryStreamManager>();

			services.AddSingleton<BotUserData>();
			services.AddScoped<IUpdateHandler, UpdateHandler>();
			services.AddScoped<ReceiverService>();
			services.AddSingleton<IProcessor, AnimatedStickerProcessor>();
			services.AddSingleton<IProcessor, ImageProcessor>();
			services.AddSingleton<IProcessor, VideoStickerProcessor>();
			services.AddSingleton<ProcessorHandler>();
			services.AddSingleton<ICommand, AvatarCommand>();
			services.AddSingleton<ICommand, ColorizeCommand>();
			services.AddSingleton<ICommand, SettingsCommand>();
			services.AddSingleton<ICommand, StartCommand>();
			services.AddSingleton<CommandHandler>();
			services.AddSingleton<Bot>();

			services.AddHostedService<InitializationHostedService>();
			services.AddHostedService<PollingService>();
		}
	)
	.Build();

await host.Services.GetRequiredService<Bot>().Init();

using (var scope = host.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
	await scope.ServiceProvider.GetRequiredService<UserSettingsDbContext>().Database.MigrateAsync();
}

await host.RunAsync();
