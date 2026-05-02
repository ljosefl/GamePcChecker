using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GamePcChecker.App.Models;
using GamePcChecker.App.Services;

namespace GamePcChecker.App.StressTest;

public partial class StressTestWindow : Window
{
    private DirectXStressRenderer? _renderer;
    private CpuStressRunner? _cpu;
    private LibreHardwareSensorService? _sensorService;
    private readonly DispatcherTimer _sensorTimer;
    private SensorSnapshot _lastSensors;
    private bool _running;
    private readonly Stopwatch _session = new();
    private long _frameCount;
    private long _lastStatsTick;
    private long _lastDropEventMs;
    private TimeSpan? _lastRenderPass;
    private double _minFps = double.MaxValue;
    private double _maxFps;
    private double _emaFps;
    private int _fpsDropEvents;
    private readonly List<(double sec, double fps)> _fpsSamples = new();
    private readonly List<(double sec, double? cpu, double? gpu)> _tempSamples = new();

    public StressTestWindow()
    {
        _sensorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sensorTimer.Tick += OnSensorTick;

        InitializeComponent();
        IntensitySlider.ValueChanged += (_, _) =>
        {
            IntensityLabel.Text = ((int)IntensitySlider.Value).ToString();
            _renderer?.SetIntensityPercent((int)IntensitySlider.Value);
        };
        IntensityLabel.Text = ((int)IntensitySlider.Value).ToString();

        Loaded += OnLoaded;
        Closed += (_, _) => ShutdownStress();
        VsyncCheck.Checked += (_, _) => { if (_renderer != null) _renderer.Vsync = VsyncCheck.IsChecked == true; };
        VsyncCheck.Unchecked += (_, _) => { if (_renderer != null) _renderer.Vsync = VsyncCheck.IsChecked == true; };
        DxPanel.HandleCreated += (_, _) => TryInitRenderer();
        DxPanel.Resize += (_, _) =>
        {
            if (_renderer != null && DxPanel.ClientSize.Width > 0 && DxPanel.ClientSize.Height > 0)
                _renderer.Resize(DxPanel.ClientSize.Width, DxPanel.ClientSize.Height);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TryInitRenderer();
        TryInitSensors();
    }

    private void TryInitSensors()
    {
        try
        {
            _sensorService = new LibreHardwareSensorService();
            _sensorService.Refresh();
            _lastSensors = _sensorService.ReadSnapshot();
            SensorLineText.Text = FormatSensorLine(_lastSensors, running: false, _fpsDropEvents);
            _sensorTimer.Start();
        }
        catch (Exception ex)
        {
            SensorLineText.Text =
                $"Датчики недоступны ({ex.Message}). График °C будет пустым; FPS по-прежнему строится.";
        }
    }

    private void OnSensorTick(object? sender, EventArgs e)
    {
        if (_sensorService == null)
            return;

        try
        {
            _sensorService.Refresh();
            _lastSensors = _sensorService.ReadSnapshot();
            SensorLineText.Text = FormatSensorLine(_lastSensors, _running, _fpsDropEvents);

            if (_running)
            {
                _tempSamples.Add((_session.Elapsed.TotalSeconds, _lastSensors.CpuCelsius, _lastSensors.GpuCelsius));
                TrimSeries();
                RedrawTempChart();
            }
        }
        catch
        {
            // не блокируем стресс-тест из-за сбоя опроса датчиков
        }
    }

    private void OnChartCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawFpsChart();
        RedrawTempChart();
    }

    private static string FormatSensorLine(SensorSnapshot s, bool running, int drops)
    {
        var cpu = s.CpuCelsius is { } c ? $"{c:F0}°C" : "—";
        var gpu = s.GpuCelsius is { } g ? $"{g:F0}°C" : "—";
        var p = s.GpuPowerWatts is { } w ? $"{w:F0} Вт (GPU)" : "—";
        var stab = running && drops > 0 ? $" · события просадки FPS: {drops}" : "";
        return $"Датчики: CPU {cpu} · GPU {gpu} · мощность {p}{stab}";
    }

    private void TrimSeries()
    {
        const int maxPoints = 140;
        while (_fpsSamples.Count > maxPoints)
            _fpsSamples.RemoveAt(0);
        while (_tempSamples.Count > maxPoints)
            _tempSamples.RemoveAt(0);
    }

    private double ChartTMax()
    {
        var a = _fpsSamples.Count > 0 ? _fpsSamples[^1].sec : 0;
        var b = _tempSamples.Count > 0 ? _tempSamples[^1].sec : 0;
        return Math.Max(Math.Max(a, b), 0.001);
    }

    private double ChartTMin()
    {
        return Math.Max(0, ChartTMax() - 90);
    }

    private void RedrawFpsChart()
    {
        var w = FpsChartCanvas.ActualWidth;
        var h = FpsChartCanvas.ActualHeight;
        if (w <= 1 || h <= 1 || _fpsSamples.Count < 2)
        {
            FpsPolyline.Points = new PointCollection();
            return;
        }

        var tMin = ChartTMin();
        var span = Math.Max(ChartTMax() - tMin, 0.25);
        var maxF = Math.Max(_fpsSamples.Max(s => s.fps) * 1.08, 24);
        var pc = new PointCollection();
        foreach (var s in _fpsSamples)
        {
            if (s.sec < tMin - 0.02)
                continue;
            var x = (s.sec - tMin) / span * w;
            var y = h - Math.Clamp(s.fps / maxF, 0, 1) * h;
            pc.Add(new System.Windows.Point(x, y));
        }

        FpsPolyline.Points = pc;
    }

    private void RedrawTempChart()
    {
        var w = TempChartCanvas.ActualWidth;
        var h = TempChartCanvas.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            CpuTempPolyline.Points = new PointCollection();
            GpuTempPolyline.Points = new PointCollection();
            return;
        }

        var tMin = ChartTMin();
        var span = Math.Max(ChartTMax() - tMin, 0.25);
        const double maxTemp = 100;

        var cpuPc = new PointCollection();
        foreach (var s in _tempSamples)
        {
            if (s.cpu is not { } v)
                continue;
            var x = (s.sec - tMin) / span * w;
            var y = h - Math.Clamp(v / maxTemp, 0, 1) * h;
            cpuPc.Add(new System.Windows.Point(x, y));
        }

        CpuTempPolyline.Points = cpuPc;

        var gpuPc = new PointCollection();
        foreach (var s in _tempSamples)
        {
            if (s.gpu is not { } v)
                continue;
            var x = (s.sec - tMin) / span * w;
            var y = h - Math.Clamp(v / maxTemp, 0, 1) * h;
            gpuPc.Add(new System.Windows.Point(x, y));
        }

        GpuTempPolyline.Points = gpuPc;
    }

    private void RecordFpsSample(double elapsedSec, double fps)
    {
        _fpsSamples.Add((elapsedSec, fps));
        TrimSeries();
        RedrawFpsChart();
    }

    private void MaybeRecordFpsDrop(double fps, long nowMs)
    {
        if (!_running || fps <= 0)
            return;
        var elapsedSec = _session.Elapsed.TotalSeconds;
        if (elapsedSec < 4)
            return;

        _emaFps = _emaFps <= 0 ? fps : _emaFps * 0.9 + fps * 0.1;
        if (_emaFps <= 12)
            return;

        if (fps >= _emaFps * 0.42)
            return;

        if (nowMs - _lastDropEventMs < 2500)
            return;

        _lastDropEventMs = nowMs;
        _fpsDropEvents++;
        SensorLineText.Text = FormatSensorLine(_lastSensors, true, _fpsDropEvents);
    }

    private void TryInitRenderer()
    {
        if (_renderer != null || DxPanel.Handle == IntPtr.Zero)
            return;

        _renderer = new DirectXStressRenderer(DxPanel.Handle, DxPanel.ClientSize.Width, DxPanel.ClientSize.Height);
        _renderer.SetIntensityPercent((int)IntensitySlider.Value);
        _renderer.Vsync = VsyncCheck.IsChecked == true;

        if (!_renderer.IsReady)
        {
            StatsText.Text = _renderer.LastError ?? "Не удалось создать Direct3D устройство.";
            ToggleBtn.IsEnabled = false;
            return;
        }

        StatsText.Text = "Нажмите «Старт» для нагрузки на GPU.";
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (!_running)
            StartStress();
        else
            StopStress();
    }

    private void StartStress()
    {
        if (_renderer == null || !_renderer.IsReady)
        {
            TryInitRenderer();
            if (_renderer == null || !_renderer.IsReady)
                return;
        }

        _running = true;
        _lastRenderPass = null;
        ToggleBtn.Content = "Стоп";
        _session.Restart();
        _frameCount = 0;
        _minFps = double.MaxValue;
        _maxFps = 0;
        _lastStatsTick = _session.ElapsedMilliseconds;
        _emaFps = 0;
        _fpsDropEvents = 0;
        _lastDropEventMs = 0;
        _fpsSamples.Clear();
        _tempSamples.Clear();
        FpsPolyline.Points = new PointCollection();
        CpuTempPolyline.Points = new PointCollection();
        GpuTempPolyline.Points = new PointCollection();

        _renderer.Vsync = VsyncCheck.IsChecked == true;
        _renderer.SetIntensityPercent((int)IntensitySlider.Value);

        if (CpuCheck.IsChecked == true)
        {
            _cpu ??= new CpuStressRunner();
            _cpu.Start(Environment.ProcessorCount);
        }

        CompositionTarget.Rendering += OnRendering;
    }

    private void StopStress()
    {
        var hadActiveSession = _running;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
        _cpu?.Stop();
        ToggleBtn.Content = "Старт";

        var elapsed = _session.Elapsed;
        var elapsedSec = Math.Max(1e-6, elapsed.TotalSeconds);
        var avgFps = _frameCount / elapsedSec;
        if (_minFps == double.MaxValue)
            _minFps = 0;

        if (hadActiveSession && _frameCount > 0)
        {
            StressTestSessionStore.Commit(
                new StressTestSessionRecord(
                    DateTime.UtcNow,
                    elapsed,
                    _frameCount,
                    avgFps,
                    _minFps,
                    _maxFps,
                    (int)IntensitySlider.Value,
                    VsyncCheck.IsChecked == true,
                    CpuCheck.IsChecked == true));
        }

        FpsText.Text =
            elapsed.TotalSeconds < 0.5
                ? "Сессия слишком короткая — для выводов лучше 2–5 минут под нагрузкой."
                : $"Средн. ≈ {avgFps:F0} FPS (оценка за сессию) · длительность {elapsed:mm\\:ss}";

        StatsText.Text = BuildPostSessionAdvice(elapsed, avgFps);
    }

    private void ShutdownStress()
    {
        _sensorTimer.Stop();
        StopStress();
        _cpu?.Dispose();
        _cpu = null;
        _renderer?.Dispose();
        _renderer = null;
        _sensorService?.Dispose();
        _sensorService = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running || _renderer == null || !_renderer.IsReady)
            return;

        if (e is RenderingEventArgs re)
        {
            if (_lastRenderPass == re.RenderingTime)
                return;
            _lastRenderPass = re.RenderingTime;
        }

        _renderer.RenderFrame();
        _frameCount++;

        var now = _session.ElapsedMilliseconds;
        if (now - _lastStatsTick < 500)
            return;

        _lastStatsTick = now;
        var elapsedSec = _session.Elapsed.TotalSeconds;
        var fps = elapsedSec > 0 ? _frameCount / elapsedSec : 0;
        if (fps > 0)
        {
            _minFps = Math.Min(_minFps, fps);
            _maxFps = Math.Max(_maxFps, fps);
        }

        RecordFpsSample(elapsedSec, fps);
        MaybeRecordFpsDrop(fps, now);

        FpsText.Text = $"≈ {fps:F0} FPS (min {_minFps:F0} / max {_maxFps:F0}) · {_session.Elapsed:mm\\:ss}";
        StatsText.Text =
            "Идёт нагрузка. График FPS и °C обновляются ниже; мощность — только то, что отдаёт GPU. БП с розетки программа не измеряет.";
    }

    private static string BuildPostSessionAdvice(TimeSpan elapsed, double avgFps)
    {
        if (elapsed.TotalSeconds < 0.5)
        {
            return "Остановлено. Запустите тест на 2–5 минут, чтобы оценить нагрев и стабильность.";
        }

        var lines = new List<string>
        {
            "Итог сессии (синтетика, не игра). Если уже выполняли «Сканировать», основной отчёт обновится с учётом этой сессии; иначе откройте стресс-тест после сканирования или нажмите «Сканировать» ещё раз.",
            "",
            "• Питание (БП): редкие ребуты, чёрный экран, мерцание при пике нагрузки, запах гари — повод проверить БП/кабели PCI‑E/лимиты по мощности. Ватты на розетке и полная нагрузка БП здесь не измеряются; при необходимости — внешний ваттметр / диагностика железа.",
            "• Температуры: сильный троттлинг или 90+ °C на GPU/CPU — смотрите кривые вентиляторов, пыль, корпус. В окне стресс-теста мы показываем °C и график, если ОС/драйвер отдаёт показания; на части ПК без доступа к датчикам EC строка может быть «—».",
            "• Стабильность: артефакты, TDR/драйвер сбросился — снизьте интенсивность, обновите драйвер, проверьте разгон.",
            "• Сравнение: при норме средний FPS и min/max не должны «падать в ноль» к концу сессии — это намек на троттлинг или фоновые процессы.",
        };

        if (avgFps > 0)
            lines.Insert(2, $"• Средняя отрисовка в сессии — около {avgFps:F0} FPS (косвенно: чем стабильнее, тем лучше при той же интенсивности).");

        return string.Join(Environment.NewLine, lines);
    }
}
