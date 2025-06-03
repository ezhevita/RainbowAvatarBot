using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RainbowAvatarBot.Services;

internal partial class PollingService : BackgroundService
{
	private readonly ILogger _logger;
	private readonly IServiceProvider _serviceProvider;

	public PollingService(IServiceProvider serviceProvider, ILogger<PollingService> logger)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				using var scope = _serviceProvider.CreateScope();
				var receiver = scope.ServiceProvider.GetRequiredService<ReceiverService>();

				await receiver.ReceiveAsync(stoppingToken);
			}
			catch (Exception ex)
			{
				LogHandlingError(ex);

				await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
			}
		}
	}

	[LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Handling updates failed")]
	private partial void LogHandlingError(Exception ex);
}
