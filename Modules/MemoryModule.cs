using Lychee.Core;
using Lychee.Platform;

namespace Lychee.Modules;

public sealed class MemoryModule : InfoModuleBase
{
    public override string Id => "memory";
    public override string DisplayName => "Memory Usage";
    public override string Icon => "🧠";

    private PeriodicTimer? _timer;
    private Task? _loopTask;
    private CancellationTokenSource? _cts;

    public MemoryModule()
    {
        CurrentValue = "RAM  —%";
    }

    public override void Start()
    {
        if (_timer != null) return;
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
            if (!SystemMemory.TryRead(out var totalBytes, out var usedBytes, out var availableBytes))
                return;

            var load = totalBytes > 0 ? (int)(usedBytes * 100.0 / totalBytes + 0.5) : 0;

            CurrentValue = $"RAM  {load}%\n{FormatBytes(usedBytes)} / {FormatBytes(totalBytes)}";
            Detail = $"Free {FormatBytes(availableBytes)}";
        }
        catch (Exception ex)
        {
            AppLog.Error("memory", ex);
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024UL * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024UL * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
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
