using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GamePcChecker.App.Models;
using GamePcChecker.App.Services;

namespace GamePcChecker.App.StressTest;

/// <summary>Трёхфазный стресс при сканировании: мин → сред → макс, GPU+CPU, графики °C/мощности.</summary>
public partial class ScanStressWindow : Window
{
    private const double PhaseDurationSec = 25.0;
    private const int StatsIntervalMs = 500;

    /// <summary>Первые секунды фазы не учитываются в min/max FPS — старт шейдера и смена нагрузки дают ложные единичные значения.</summary>
    private const double PhaseWarmupSec = 3.0;

    private static readonly (string Label, int Intensity)[] PhaseDefinitions =
    [
        ("Минимальная нагрузка", 20),
        ("Средняя нагрузка", 80),
        ("Максимальная нагрузка", 200),
    ];

    private DirectXStressRenderer? _renderer;
    private CpuStressRunner? _cpu;
    private LibreHardwareSensorService? _sensorService;
    private readonly DispatcherTimer _sensorTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SensorSnapshot _lastSensors;

    private readonly Stopwatch _phaseWatch = new();
    private readonly Stopwatch _suiteSession = new();

    private int _phaseIndex;
    private bool _running;
    private bool _suiteFinished;
    private long _frameCount;
    private long _lastStatsTick;
    private double _minFps = double.MaxValue;
    private double _maxFps;
    private TimeSpan? _lastRenderPass;
    private readonly List<ScanStressPhaseMetrics> _phaseResults = new();
    private string? _cancelReason;

    private readonly List<(double sec, double fps)> _fpsSamples = new();
    private readonly List<(double sec, double? cpu, double? gpu)> _tempSamples = new();

    public QuickGpuStressMetrics? ResultMetrics { get; private set; }

    public ScanStressWindow()
    {
        InitializeComponent();
        _sensorTimer.Tick += OnSensorTick;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        DxPanel.HandleCreated += (_, _) => TryInitRenderer();
        DxPanel.Resize += (_, _) =>
        {
            if (_renderer != null && DxPanel.ClientSize.Width > 0 && DxPanel.ClientSize.Height > 0)
                _renderer.Resize(DxPanel.ClientSize.Width, DxPanel.ClientSize.Height);
        };
    }

    public static QuickGpuStressMetrics ShowModal(Window owner)
    {
        var w = new ScanStressWindow { Owner = owner };
        w.ShowDialog();
        if (w.ResultMetrics is { Success: true } m)
            return m;

        return QuickGpuStressMetrics.Failed(w._cancelReason ?? "Стресс-тест не завершён");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TryInitSensors();
        TryInitRenderer();
    }

    private void TryInitSensors()
    {
        try
        {
            _sensorService = new LibreHardwareSensorService();
            _sensorService.Refresh();
            _lastSensors = _sensorService.ReadSnapshot();
            SensorLineText.Text = FormatSensorLine(_lastSensors);
            _sensorTimer.Start();
        }
        catch (Exception ex)
        {
            SensorLineText.Text =
                $"Датчики недоступны ({ex.Message}). График °C будет пустым; FPS по-прежнему строится.";
        }
    }

    private static string FormatSensorLine(SensorSnapshot s)
    {
        var cpu = s.CpuCelsius is { } c ? $"{c:F0}°C" : "—";
        var gpu = s.GpuCelsius is { } g ? $"{g:F0}°C" : "—";
        var p = s.GpuPowerWatts is { } w ? $"{w:F0} Вт (GPU)" : "—";
        return $"Датчики: CPU {cpu} · GPU {gpu} · мощность {p}";
    }

    private void OnSensorTick(object? sender, EventArgs e)
    {
        if (_sensorService == null)
            return;

        try
        {
            _sensorService.Refresh();
            _lastSensors = _sensorService.ReadSnapshot();
            SensorLineText.Text = FormatSensorLine(_lastSensors);

            if (_running)
            {
                _tempSamples.Add((_suiteSession.Elapsed.TotalSeconds, _lastSensors.CpuCelsius, _lastSensors.GpuCelsius));
                TrimSeries();
                RedrawTempChart();
            }
        }
        catch
        {
            // не блокируем тест
        }
    }

    private void TrimSeries()
    {
        const int maxPoints = 200;
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

    private double ChartTMin() => Math.Max(0, ChartTMax() - 95);

    private void OnChartCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawFpsChart();
        RedrawTempChart();
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

    private void RecordFpsSample(double elapsedSecSuite, double fps)
    {
        _fpsSamples.Add((elapsedSecSuite, fps));
        TrimSeries();
        RedrawFpsChart();
    }

    private void TryInitRenderer()
    {
        if (_renderer != null || DxPanel.Handle == IntPtr.Zero)
            return;

        _renderer = new DirectXStressRenderer(DxPanel.Handle, DxPanel.ClientSize.Width, DxPanel.ClientSize.Height);
        _renderer.Vsync = false;

        if (!_renderer.IsReady)
        {
            PhaseTitleText.Text = "Ошибка Direct3D";
            HintLineText.Text = _renderer.LastError ?? "Не удалось создать устройство.";
            _cancelReason = _renderer.LastError ?? "Direct3D недоступен";
            CancelBtn.Content = "Закрыть";
            OverallProgress.IsEnabled = false;
            return;
        }

        StartSuite();
    }

    private void StartSuite()
    {
        if (_renderer == null || !_renderer.IsReady)
            return;

        _cpu ??= new CpuStressRunner();
        _cpu.Start(Environment.ProcessorCount);

        _running = true;
        _suiteFinished = false;
        _phaseIndex = 0;
        _phaseResults.Clear();
        _cancelReason = null;
        _fpsSamples.Clear();
        _tempSamples.Clear();
        _suiteSession.Restart();

        CompositionTarget.Rendering += OnRendering;
        BeginPhase(0);
    }

    private void BeginPhase(int index)
    {
        _phaseIndex = index;
        var (label, intensity) = PhaseDefinitions[index];
        _renderer!.SetIntensityPercent(intensity);
        _phaseWatch.Restart();
        _frameCount = 0;
        _minFps = double.MaxValue;
        _maxFps = 0;
        _lastStatsTick = 0;

        PhaseTitleText.Text = $"Этап {index + 1} из 3: {label} (~{intensity}%)";
        HintLineText.Text =
            $"Фаза {PhaseDurationSec:F0} с · нагрузка на GPU (шейдер) и CPU (все ядра). Смотрите графики °C и мощность.";
        UpdateProgressUi();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running || _suiteFinished || _renderer == null || !_renderer.IsReady)
            return;

        if (e is RenderingEventArgs re)
        {
            if (_lastRenderPass == re.RenderingTime)
                return;
            _lastRenderPass = re.RenderingTime;
        }

        _renderer.RenderFrame();
        _frameCount++;

        var elapsedSec = _phaseWatch.Elapsed.TotalSeconds;
        if (elapsedSec >= PhaseDurationSec)
        {
            CompleteCurrentPhase();
            return;
        }

        var nowMs = _phaseWatch.ElapsedMilliseconds;
        if (nowMs - _lastStatsTick < StatsIntervalMs)
            return;

        _lastStatsTick = nowMs;
        var fps = elapsedSec > 0 ? _frameCount / elapsedSec : 0;
        if (fps > 0 && elapsedSec >= PhaseWarmupSec)
        {
            _minFps = Math.Min(_minFps, fps);
            _maxFps = Math.Max(_maxFps, fps);
        }

        RecordFpsSample(_suiteSession.Elapsed.TotalSeconds, fps);

        var (_, intensity) = PhaseDefinitions[_phaseIndex];
        FpsLineText.Text =
            $"≈ {fps:F0} FPS (мин {_minFps:F0} / макс {_maxFps:F0}) · GPU {intensity}% · фаза {elapsedSec:F0}/{PhaseDurationSec:F0} с";

        UpdateProgressUi();
    }

    private void UpdateProgressUi()
    {
        var donePrev = _phaseIndex * PhaseDurationSec;
        var inPhase = Math.Min(PhaseDurationSec, _phaseWatch.Elapsed.TotalSeconds);
        OverallProgress.Value = Math.Min(75, donePrev + inPhase);
        ProgressHintText.Text =
            $"{OverallProgress.Value:F0} / 75 с · этап {_phaseIndex + 1}/3";
    }

    private void CompleteCurrentPhase()
    {
        var elapsedSec = Math.Max(1e-6, Math.Min(PhaseDurationSec, _phaseWatch.Elapsed.TotalSeconds));
        var avgFps = _frameCount / elapsedSec;
        var minF = _minFps == double.MaxValue ? avgFps : _minFps;
        var maxF = Math.Max(_maxFps, avgFps);
        var (label, intensity) = PhaseDefinitions[_phaseIndex];

        _phaseResults.Add(
            new ScanStressPhaseMetrics(
                label,
                intensity,
                minF,
                avgFps,
                maxF,
                TimeSpan.FromSeconds(elapsedSec),
                _frameCount));

        if (_phaseIndex >= PhaseDefinitions.Length - 1)
        {
            FinishSuiteSuccess();
            return;
        }

        BeginPhase(_phaseIndex + 1);
    }

    private void FinishSuiteSuccess()
    {
        _suiteFinished = true;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;

        StopCpuLoad();
        _sensorTimer.Stop();

        ResultMetrics = QuickGpuStressMetrics.FromPhaseSuite(_phaseResults);
        SummaryText.Text = ResultMetrics.ReportSummaryText;

        RunPanel.Visibility = Visibility.Collapsed;
        HeaderProgressPanel.Visibility = Visibility.Collapsed;
        DxBorder.Visibility = Visibility.Collapsed;
        MetricsChartsPanel.Visibility = Visibility.Collapsed;
        FpsHintPanel.Visibility = Visibility.Collapsed;

        DonePanel.Visibility = Visibility.Visible;
        CancelBtn.Visibility = Visibility.Collapsed;
        DoneBtn.Visibility = Visibility.Visible;
        DoneBtn.IsEnabled = true;
    }

    private void StopCpuLoad()
    {
        _cpu?.Stop();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_suiteFinished && _running)
            _cancelReason ??= "Окно закрыто до завершения теста";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _cancelReason = "Отменено пользователем";
        _running = false;
        _suiteFinished = true;
        CompositionTarget.Rendering -= OnRendering;
        StopCpuLoad();
        try
        {
            _sensorTimer.Stop();
        }
        catch
        {
            // ignore
        }

        DialogResult = false;
        Close();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        StopCpuLoad();
        _cpu?.Dispose();
        _cpu = null;

        _sensorTimer.Stop();
        _sensorTimer.Tick -= OnSensorTick;
        _sensorService?.Dispose();
        _sensorService = null;

        _renderer?.Dispose();
        _renderer = null;
    }
}
