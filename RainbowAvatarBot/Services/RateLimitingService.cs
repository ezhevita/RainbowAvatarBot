using System;
using System.Collections.Concurrent;

namespace RainbowAvatarBot.Services;

internal sealed class RateLimitingService
{
	private readonly TimeSpan _interval = TimeSpan.FromSeconds(3);
	private readonly ConcurrentDictionary<long, (bool Executing, long Timestamp)> _userLastRequest = new();
	private readonly TimeProvider _timeProvider;

	public RateLimitingService(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public IDisposable? TryEnter(long userId)
	{
		if (_userLastRequest.TryGetValue(userId, out var record))
		{
			if (record.Executing)
			{
				return null;
			}

			if (_timeProvider.GetElapsedTime(record.Timestamp) <= _interval)
			{
				return null;
			}
		}

		_userLastRequest[userId] = (true, _timeProvider.GetTimestamp());
		return new FinalizingDisposable(this, userId);
	}

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
			if (_service._userLastRequest.TryGetValue(_userId, out var record) && record.Executing)
			{
				_service._userLastRequest[_userId] = (false, record.Timestamp);
			}
		}
	}
}
