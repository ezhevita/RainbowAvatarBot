using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace RainbowAvatarBot.Services;

internal sealed partial class RateLimitingService
{
	private readonly TimeSpan _interval = TimeSpan.FromSeconds(3);
	private readonly Dictionary<long, (bool Executing, long Timestamp)> _userLastRequest = new();
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<RateLimitingService> _logger;

	public RateLimitingService(TimeProvider timeProvider, ILogger<RateLimitingService> logger)
	{
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public IDisposable? TryEnter(long userId)
	{
		lock (_userLastRequest)
		{
			if (_userLastRequest.TryGetValue(userId, out var record))
			{
				if (record.Executing)
				{
					LogConcurrentRequest(userId);
					return null;
				}

				var elapsed = _timeProvider.GetElapsedTime(record.Timestamp);
				if (elapsed <= _interval)
				{
					LogConcurrentRequest(userId);
					return null;
				}
			}

			_userLastRequest[userId] = (true, _timeProvider.GetTimestamp());
		}

		return new FinalizingDisposable(this, userId);
	}

	[LoggerMessage(LogLevel.Information, "User {UserId} attempted to execute a concurrent request.")]
	private partial void LogConcurrentRequest(long userId);

	[LoggerMessage(LogLevel.Information, "User {UserId} attempted to exceed rate limit ({Elapsed} elapsed).")]
	private partial void LogRateLimited(long userId, TimeSpan elapsed);

	private sealed class FinalizingDisposable : IDisposable
	{
		private readonly RateLimitingService _service;
		private readonly long _userId;

		public FinalizingDisposable(RateLimitingService service, long userId)
		{
			_service = service;
			_userId = userId;
		}

		public void Dispose()
		{
			var dict = _service._userLastRequest;
			lock (dict)
			{
				if (dict.TryGetValue(_userId, out var record) && record.Executing)
				{
					dict[_userId] = (false, record.Timestamp);
				}
			}
		}
	}
}
