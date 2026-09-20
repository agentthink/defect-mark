using System.Collections.Concurrent;

namespace MaskFlow.Api.Infrastructure.Security;

public sealed class LoginRateLimiter
{
    readonly ConcurrentDictionary<string, AttemptWindow> windows = new();

    static int MaxAttempts =>
        int.TryParse(Environment.GetEnvironmentVariable("MASKFLOW_LOGIN_MAX_ATTEMPTS"), out var value) && value > 0
            ? value
            : 5;

    static TimeSpan LockoutDuration
    {
        get
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("MASKFLOW_LOGIN_LOCKOUT_MINUTES"), out var minutes) && minutes > 0)
            {
                return TimeSpan.FromMinutes(minutes);
            }

            return TimeSpan.FromMinutes(15);
        }
    }

    public bool IsLocked(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!windows.TryGetValue(key, out var window))
        {
            return false;
        }

        if (window.LockedUntil is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((lockedUntil - DateTimeOffset.UtcNow).TotalSeconds));
            return true;
        }

        if (window.LockedUntil is not null && window.LockedUntil <= DateTimeOffset.UtcNow)
        {
            windows.TryRemove(key, out _);
        }

        return false;
    }

    public void RecordFailure(string key)
    {
        var now = DateTimeOffset.UtcNow;
        windows.AddOrUpdate(
            key,
            _ => new AttemptWindow(1, now, null),
            (_, current) =>
            {
                var failures = current.FirstFailureAt + LockoutDuration < now ? 1 : current.Failures + 1;
                var firstFailureAt = current.FirstFailureAt + LockoutDuration < now ? now : current.FirstFailureAt;
                var lockedUntil = failures >= MaxAttempts ? now + LockoutDuration : (DateTimeOffset?)null;
                return new AttemptWindow(failures, firstFailureAt, lockedUntil);
            });
    }

    public void RecordSuccess(string key) => windows.TryRemove(key, out _);

    sealed record AttemptWindow(int Failures, DateTimeOffset FirstFailureAt, DateTimeOffset? LockedUntil);
}
