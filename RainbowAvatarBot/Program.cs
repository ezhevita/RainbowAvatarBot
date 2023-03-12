using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RainbowAvatarBot;
using Telegram.Bot;
using Telegram.Bot.Polling;

var host = Host.CreateDefaultBuilder(args)
	.ConfigureServices(
		(context, services) =>
		{
			services.Configure<BotConfiguration>(context.Configuration.GetSection("Bot"));

			services.AddHttpClient("telegram_bot_client")
				.AddTypedClient<ITelegramBotClient>(
					(httpClient, sp) =>
					{
						var botConfig = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
						var options = new TelegramBotClientOptions(botConfig.Token);

						return new TelegramBotClient(options, httpClient);
					}
				);

			services.AddSingleton<BotUserData>();
			services.AddScoped<IUpdateHandler, UpdateHandler>();
			services.AddScoped<ReceiverService>();
			services.AddSingleton<Bot>();

			services.AddHostedService<PollingService>();
		}
	)
	.Build();

await host.Services.GetRequiredService<Bot>().Init();

await host.RunAsync();
