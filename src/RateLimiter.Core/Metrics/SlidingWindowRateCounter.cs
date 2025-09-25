using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RateLimiter.Core.Metrics;

/// <summary>
/// Lock-free sliding window rate counter using a striped ring buffer.
/// </summary>
public sealed class SlidingWindowRateCounter : IDisposable
{
    private readonly TimeSpan _window;
    private readonly int _bucketCount;
    private readonly long _bucketDurationTicks;
    private readonly double[] _bucketValues;
    private readonly long[] _bucketStarts;
    private volatile bool _disposed;

    public SlidingWindowRateCounter(TimeSpan window, int bucketCount = 20)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }

        if (bucketCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketCount), "Bucket count must be positive.");
        }

        _window = window;
        _bucketCount = bucketCount;
        _bucketDurationTicks = Math.Max(1L, window.Ticks / bucketCount);
        _bucketValues = new double[bucketCount];
        _bucketStarts = new long[bucketCount];
    }

    public TimeSpan Window => _window;

    public SlidingWindowSample AddSample(long nowTicks, double value = 1d)
    {
        ThrowIfDisposed();
        UpdateBucket(nowTicks, value);
        return Snapshot(nowTicks);
    }

    public SlidingWindowSample Snapshot(long nowTicks)
    {
        ThrowIfDisposed();
        return CreateSample(nowTicks);
    }

    private void UpdateBucket(long nowTicks, double value)
    {
        var bucketIndex = GetBucketIndex(nowTicks);
        var bucketStart = AlignToBucket(nowTicks);

        var existingStart = Volatile.Read(ref _bucketStarts[bucketIndex]);
        if (existingStart != bucketStart)
        {
            Volatile.Write(ref _bucketStarts[bucketIndex], bucketStart);
            Interlocked.Exchange(ref _bucketValues[bucketIndex], 0d);
        }

        DoubleAdd(ref _bucketValues[bucketIndex], value);
    }

    private SlidingWindowSample CreateSample(long nowTicks)
    {
        var windowStart = nowTicks - _window.Ticks;
        double total = 0d;

        for (var i = 0; i < _bucketCount; i++)
        {
            var bucketStart = Volatile.Read(ref _bucketStarts[i]);
            if (bucketStart == 0 || bucketStart < windowStart)
            {
                continue;
            }

            total += Volatile.Read(ref _bucketValues[i]);
        }

        var ratePerSecond = _window.TotalSeconds <= 0d ? 0d : total / _window.TotalSeconds;
        return new SlidingWindowSample(_window, total, ratePerSecond);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetBucketIndex(long nowTicks)
    {
        var bucket = (int)((nowTicks / _bucketDurationTicks) % _bucketCount);
        return bucket < 0 ? bucket + _bucketCount : bucket;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long AlignToBucket(long nowTicks) => nowTicks - (nowTicks % _bucketDurationTicks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoubleAdd(ref double target, double value)
    {
        double initial;
        double computed;
        while (true)
        {
            initial = Volatile.Read(ref target);
            computed = initial + value;
            var original = Interlocked.CompareExchange(ref target, computed, initial);
            if (BitConverter.DoubleToInt64Bits(original) == BitConverter.DoubleToInt64Bits(initial))
            {
                break;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SlidingWindowRateCounter));
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
