namespace GamePcChecker.App.StressTest;

/// <summary>
/// Дополнительная нагрузка на CPU (числовой цикл). Не измеряет игру.
/// </summary>
internal sealed class CpuStressRunner : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task[]? _tasks;

    public void Start(int threadCount)
    {
        Stop();
        threadCount = Math.Clamp(threadCount, 1, Environment.ProcessorCount);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++)
        {
            var seed = t;
            _tasks[t] = Task.Run(
                () =>
                {
                    RunCpuLoop(seed, token);
                },
                token);
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (_tasks != null)
                Task.WaitAll(_tasks, TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        _tasks = null;
        _cts?.Dispose();
        _cts = null;
    }

    private static void RunCpuLoop(int seed, CancellationToken token)
    {
        var x = (double)seed + 1.0;
        while (!token.IsCancellationRequested)
        {
            for (var i = 0; i < 8000; i++)
                x = Math.Sin(x * 1.000001 + i * 0.00001) * Math.Cos(x - i * 0.00002);
        }
    }

    public void Dispose() => Stop();
}
