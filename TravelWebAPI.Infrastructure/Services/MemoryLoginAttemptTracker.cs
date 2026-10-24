using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>
/// In-memory counter of failed sign-ins for emails without an account (one instance of the API).
/// Same limits as real accounts: after the maximum number of failures sign-in is locked for the lockout time.
/// </summary>
public sealed class MemoryLoginAttemptTracker(IClock clock, IOptions<AuthOptions> options) : ILoginAttemptTracker
{
    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset? LockedUntil;
        public DateTimeOffset LastFailure;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public TimeSpan? GetRetryAfter(string email)
    {
        if (!_entries.TryGetValue(email, out var entry))
        {
            return null;
        }

        lock (entry)
        {
            var now = clock.UtcNow;
            if (entry.LockedUntil is { } until && until > now)
            {
                return until - now;
            }

            return null;
        }
    }

    public void RecordFailure(string email)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var window = TimeSpan.FromMinutes(settings.LockoutMinutes);

        var entry = _entries.GetOrAdd(email, _ => new Entry());
        lock (entry)
        {
            // Old failures and expired locks start from zero again.
            if (entry.LockedUntil is { } until && until <= now)
            {
                entry.LockedUntil = null;
                entry.Failures = 0;
            }

            if (now - entry.LastFailure > window)
            {
                entry.Failures = 0;
            }

            entry.LastFailure = now;
            entry.Failures++;
            if (entry.Failures >= settings.MaxFailedAttempts)
            {
                entry.LockedUntil = now + window;
                entry.Failures = 0;
            }
        }

        Trim(now, window);
    }

    public void Reset(string email) => _entries.TryRemove(email, out _);

    /// <summary>Drops entries that can no longer matter, so guessing many emails cannot grow memory forever.</summary>
    private void Trim(DateTimeOffset now, TimeSpan window)
    {
        if (_entries.Count < 10_000)
        {
            return;
        }

        foreach (var (key, entry) in _entries)
        {
            if (now - entry.LastFailure > window && (entry.LockedUntil is null || entry.LockedUntil <= now))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }
}
