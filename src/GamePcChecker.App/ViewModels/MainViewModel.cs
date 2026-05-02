using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GamePcChecker.App.Configuration;
using GamePcChecker.App.StressTest;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamePcChecker.App.Models;
using GamePcChecker.App.Services;

namespace GamePcChecker.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HardwareProbeService _hardware;
    private readonly GitHubReleaseUpdateChecker _updateChecker = new();

    /// <summary>Последний снимок железа для повторного расчёта отчёта после стресс-теста без полного скана.</summary>
    private MachineSnapshot? _lastSnapshot;

    public MainViewModel(HardwareProbeService hardware)
    {
        _hardware = hardware;
        foreach (var g in GameCatalog.All)
            GameChoices.Add(g);

        if (GameChoices.Count > 0)
            SelectedGame = GameChoices[0];

        StressTestSessionStore.Changed += OnStressSessionChanged;

        AppVersionLabel = $"v{AppVersionInfo.DisplayVersion}";
        _ = CheckForUpdatesAsync();
    }

    [ObservableProperty]
    private string _appVersionLabel = "";

    /// <summary>Есть ли рядом с exe настройка GitHub для проверки релизов.</summary>
    [ObservableProperty]
    private bool _updateCheckConfigured;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string? _updateLatestVersionLabel;

    [ObservableProperty]
    private string? _releasePageUrl;

    [ObservableProperty]
    private string? _preferredDownloadUrl;

    [ObservableProperty]
    private string? _preferredAssetName;

    [ObservableProperty]
    private string? _updateStatusHint;

    public bool HasPreferredDownload => !string.IsNullOrWhiteSpace(PreferredDownloadUrl);

    partial void OnPreferredDownloadUrlChanged(string? value) => OnPropertyChanged(nameof(HasPreferredDownload));

    private void OnStressSessionChanged()
    {
        if (_lastSnapshot == null || SelectedGame == null)
            return;
        Report = AdvisorService.Analyze(_lastSnapshot, SelectedGame, StressTestSessionStore.Last);
        StatusMessage = "Отчёт обновлён с учётом стресс-теста.";
    }

    public ObservableCollection<GameProfileRecord> GameChoices { get; } = [];

    [ObservableProperty]
    private GameProfileRecord? _selectedGame;

    /// <summary>Необязательный путь к папке установки — чтобы привязать блок «игра и диск» к нужному тому (SSD/NVMe/HDD), если автоопределение не сработало.</summary>
    [ObservableProperty]
    private string? _gameInstallPathUser;

    [ObservableProperty]
    private AnalysisReport? _report;

    public bool HasReport => Report is not null;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSelectedGameChanged(GameProfileRecord? value)
    {
        if (value != null)
            StatusMessage = null;
        if (value != null && _lastSnapshot != null)
            RefreshGameInstallFromLastSnapshot(value);
    }

    partial void OnGameInstallPathUserChanged(string? value)
    {
        if (SelectedGame != null && _lastSnapshot != null)
            RefreshGameInstallFromLastSnapshot(SelectedGame);
    }

    private string? OptionalInstallPath =>
        string.IsNullOrWhiteSpace(GameInstallPathUser) ? null : GameInstallPathUser.Trim();

    private void RefreshGameInstallFromLastSnapshot(GameProfileRecord game)
    {
        var gi = GameInstallProbeService.Describe(game.Key, _lastSnapshot!.Disks, OptionalInstallPath);
        _lastSnapshot = _lastSnapshot! with { GameInstall = gi };
        Report = AdvisorService.Analyze(_lastSnapshot, game, StressTestSessionStore.Last);
    }

    partial void OnReportChanged(AnalysisReport? value)
    {
        OnPropertyChanged(nameof(HasReport));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (SelectedGame == null)
        {
            StatusMessage = "Выберите игру.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Сканирование…";
        Report = null;
        try
        {
            var snapshot = await Task.Run(() => _hardware.Probe()).ConfigureAwait(true);
            StatusMessage = "Стресс GPU+CPU (3×25 с)…";
            var owner = System.Windows.Application.Current?.MainWindow;
            var quick = owner != null
                ? ScanStressWindow.ShowModal(owner)
                : QuickGpuStressMetrics.Failed("Нет главного окна");
            var gameInstall = GameInstallProbeService.Describe(SelectedGame.Key, snapshot.Disks, OptionalInstallPath);
            var enriched = snapshot with { QuickGpuStress = quick, GameInstall = gameInstall };
            _lastSnapshot = enriched;
            Report = AdvisorService.Analyze(enriched, SelectedGame, StressTestSessionStore.Last);
            StatusMessage = "Готово.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BrowseGameInstallFolder()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Папка установки Path of Exile / Path of Exile 2",
            UseDescriptionForTitle = true,
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(GameInstallPathUser) && Directory.Exists(GameInstallPathUser))
                dlg.SelectedPath = GameInstallPathUser;
        }
        catch
        {
            // ignore invalid path for initial selection
        }

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            GameInstallPathUser = dlg.SelectedPath;
    }

    [RelayCommand]
    private void CopyReport()
    {
        if (Report == null)
            return;
        var text = ReportFormatter.ToPlainText(Report);
        System.Windows.Clipboard.SetText(text);
        StatusMessage = "Отчёт скопирован в буфер.";
    }

    [RelayCommand]
    private void SaveReport()
    {
        if (Report == null)
            return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Текст (*.txt)|*.txt|Все файлы|*.*",
            FileName = $"game-pc-check-{Report.GameTitle.Replace(' ', '-')}.txt",
        };
        if (dlg.ShowDialog() != true)
            return;

        File.WriteAllText(dlg.FileName, ReportFormatter.ToPlainText(Report), System.Text.Encoding.UTF8);
        StatusMessage = $"Сохранено: {dlg.FileName}";
    }

    [RelayCommand]
    private async Task RefreshUpdateCheck() => await CheckForUpdatesAsync();

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleasePageUrl))
            return;
        OpenUrl(ReleasePageUrl);
    }

    [RelayCommand]
    private void OpenPreferredDownload()
    {
        if (string.IsNullOrWhiteSpace(PreferredDownloadUrl))
            return;
        OpenUrl(PreferredDownloadUrl);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось открыть ссылку: {ex.Message}",
                "Game PC Checker",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        var cfg = GitHubUpdateConfig.TryLoad();
        UpdateCheckConfigured = cfg != null;
        UpdateAvailable = false;
        UpdateLatestVersionLabel = null;
        ReleasePageUrl = null;
        PreferredDownloadUrl = null;
        PreferredAssetName = null;

        if (cfg is null)
        {
            UpdateStatusHint = null;
            return;
        }

        UpdateStatusHint = "Проверка обновлений…";
        try
        {
            var current = AppVersionInfo.ParseVersionOrFallback();
            var r = await _updateChecker.CheckForNewerReleaseAsync(current, CancellationToken.None).ConfigureAwait(true);
            if (r is null)
            {
                UpdateStatusHint = "Не удалось получить сведения о релизе (репозиторий или сеть).";
                return;
            }

            ReleasePageUrl = string.IsNullOrWhiteSpace(r.ReleasePageUrl) ? null : r.ReleasePageUrl;

            if (r.HasUpdate && r.LatestVersion is not null)
            {
                UpdateAvailable = true;
                UpdateLatestVersionLabel = $"v{r.LatestVersion}";
                PreferredDownloadUrl = r.PreferredDownloadUrl;
                PreferredAssetName = r.PreferredAssetName;
                UpdateStatusHint = null;
            }
            else
            {
                UpdateStatusHint = r.LatestVersion is not null
                    ? $"Актуальная версия (последний релиз на GitHub: v{r.LatestVersion})."
                    : "Установлена последняя опубликованная версия.";
            }
        }
        catch
        {
            UpdateStatusHint = "Проверка обновлений недоступна (сеть или GitHub).";
        }
    }
}
