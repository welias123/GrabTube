using System.Diagnostics;

namespace GrabTube.Core;

/// <summary>
/// Turns a stream of byte counts into a speed and an ETA.
/// </summary>
/// <remarks>
/// Raw instantaneous speed jitters badly enough to make a label unreadable, so
/// samples are smoothed with an exponential moving average.
/// </remarks>
internal sealed class SpeedMeter
{
    private const double Smoothing = 0.2;
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(250);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastSample = TimeSpan.Zero;
    private long _lastBytes;
    private double _speed;

    public (double BytesPerSecond, TimeSpan Eta) Sample(long bytesReceived, long totalBytes)
    {
        var now = _clock.Elapsed;
        var interval = now - _lastSample;

        if (interval >= MinimumInterval)
        {
            var delta = bytesReceived - _lastBytes;
            if (delta > 0)
            {
                var current = delta / interval.TotalSeconds;
                _speed = _speed <= 0 ? current : (_speed * (1 - Smoothing)) + (current * Smoothing);
            }

            _lastSample = now;
            _lastBytes = bytesReceived;
        }

        if (_speed <= 0 || totalBytes <= 0)
            return (0, TimeSpan.Zero);

        var remaining = Math.Max(0, totalBytes - bytesReceived);
        var seconds = remaining / _speed;

        // Anything past an hour is a guess dressed up as a number.
        var eta = seconds > 3600 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
        return (_speed, eta);
    }
}
