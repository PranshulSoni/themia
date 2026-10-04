using Microsoft.UI.Dispatching;
using Themia.Core.Diagnostics;
using Themia.Core.Time;

namespace Themia.App.Services;

/// <summary>
/// One timer for the whole desktop.
/// <para>
/// A widget layer is mostly idle, so what actually costs power is how often the process wakes.
/// Every clock shares this ticker rather than owning a timer: the cadence is the finest any
/// subscriber asked for, it drops to once a minute the moment nothing needs seconds, it stops
/// entirely when nothing is subscribed or the surface is hidden, and each wake is re-aligned to the
/// next real boundary so the displayed time never drifts behind the system clock.
/// </para>
/// </summary>
public sealed class Ticker : IDisposable
{
    private const string Scope = "ticker";

    private readonly DispatcherQueueTimer _timer;
    private readonly List<Subscription> _subscriptions = [];

    private bool _paused;
    private bool _disposed;

    public Ticker(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += OnTick;
    }

    /// <summary>The cadence the timer is currently running at. Exposed for diagnostics.</summary>
    public TimeSpan CurrentCadence { get; private set; } = TimeSpan.FromMinutes(1);

    /// <summary>Subscribe at a cadence. Dispose the result to unsubscribe; the timer stops when the last one goes.</summary>
    public IDisposable Subscribe(TimeSpan interval, Action onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscription = new Subscription(this, TickSchedule.Normalise(interval), onTick);
        _subscriptions.Add(subscription);
        subscription.Fire();
        Reschedule();
        return subscription;
    }

    /// <summary>
    /// Stop waking while the surface cannot be seen (every widget window hidden, or the session
    /// locked). Resuming ticks immediately so nothing shows a stale time for up to a minute.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused || _disposed)
        {
            return;
        }

        _paused = paused;

        if (paused)
        {
            _timer.Stop();
            return;
        }

        foreach (var subscription in _subscriptions.ToArray())
        {
            subscription.Fire();
        }

        Reschedule();
    }

    private void Remove(Subscription subscription)
    {
        _subscriptions.Remove(subscription);
        Reschedule();
    }

    private void Reschedule()
    {
        if (_disposed || _paused)
        {
            return;
        }

        if (_subscriptions.Count == 0)
        {
            _timer.Stop();
            return;
        }

        var finest = TimeSpan.FromMinutes(1);
        foreach (var subscription in _subscriptions)
        {
            if (subscription.Interval < finest)
            {
                finest = subscription.Interval;
            }
        }

        CurrentCadence = finest;
        _timer.Interval = TickSchedule.DelayToNextBoundary(DateTimeOffset.Now, finest);
        _timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || _paused)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        foreach (var subscription in _subscriptions.ToArray())
        {
            // A minute subscriber sharing a one-second timer must still only run on the minute.
            if (subscription.ShouldFire(now))
            {
                subscription.Fire();
            }
        }

        Reschedule();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Tick -= OnTick;
        _timer.Stop();
        _subscriptions.Clear();
    }

    private sealed class Subscription(Ticker owner, TimeSpan interval, Action onTick) : IDisposable
    {
        private long _lastPeriod = -1;
        private bool _disposed;

        public TimeSpan Interval { get; } = interval;

        public bool ShouldFire(DateTimeOffset now)
        {
            var period = TickSchedule.PeriodIndex(now, Interval);
            if (period == _lastPeriod)
            {
                return false;
            }

            _lastPeriod = period;
            return true;
        }

        public void Fire()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                onTick();
            }
            catch (Exception ex)
            {
                // One misbehaving widget must not stop the clock for every other widget.
                Log.Error(Scope, "a tick subscriber threw", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Remove(this);
        }
    }
}
