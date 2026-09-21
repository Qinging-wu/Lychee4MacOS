using System.Diagnostics;
using Lychee.Core;
using Lychee.Platform;

namespace Lychee.Modules;

public sealed class CpuModule : InfoModuleBase
{
    public override string Id => "cpu";
    public override string DisplayName => "CPU Usage";
    public override string Icon => "💻";

    // Sleep/resume guard: if the wall-clock gap between successful samples is
    // too large (system slept, process suspended), drop the differential and
    // rebuild the baseline. Monotonic clock on purpose — CPU tick counters are
    // platform-specific units and must not be used to infer elapsed time.
    private const double MaxSampleGapSeconds = 5;

    private PeriodicTimer? _timer;
    private Task? _loopTask;
    private CancellationTokenSource? _cts;
    private long _lastIdle;
    private long _lastTotal;
    private long _lastMonotonic;
    private bool _hasLastSample;

    public CpuModule()
    {
        CurrentValue = "CPU  —%";
        Detail = "Collecting…";
    }

    public override void Start()
    {
        if (_timer != null) return;
        _hasLastSample = false;
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _loopTask = Task.Run(async () =>
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_cts.Token))
                {
                    Update();
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    private void Update()
    {
        try
        {
            var nowMono = Stopwatch.GetTimestamp();
            if (!SystemCpuTimes.TryRead(out var idle, out var total))
                return;

            if (!_hasLastSample)
            {
                SetBaseline(idle, total, nowMono);
                return;
            }

            var gapSeconds = (nowMono - _lastMonotonic) / (double)Stopwatch.Frequency;
            var deltaIdle = idle - _lastIdle;
            var deltaTotal = total - _lastTotal;

            if (deltaTotal <= 0 || deltaIdle < 0 || gapSeconds > MaxSampleGapSeconds)
            {
                SetBaseline(idle, total, nowMono);
                CurrentValue = "CPU  —%";
                Detail = "Collecting…";
                return;
            }

            SetBaseline(idle, total, nowMono);

            var usage = (1.0 - (double)deltaIdle / deltaTotal) * 100;
            if (usage < 0) usage = 0;
            if (usage > 100) usage = 100;

            CurrentValue = $"CPU  {usage:F0}%";
            Detail = $"{Environment.ProcessorCount} logical cores";
        }
        catch (Exception ex)
        {
            AppLog.Error("cpu", ex);
        }
    }

    private void SetBaseline(long idle, long total, long monotonic)
    {
        _lastIdle = idle;
        _lastTotal = total;
        _lastMonotonic = monotonic;
        _hasLastSample = true;
    }

    public override void Stop()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        try { _loopTask?.Wait(500); } catch { }
        _timer = null;
        _cts = null;
        _loopTask = null;
    }
}
