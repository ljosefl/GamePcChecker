using GamePcChecker.App.Models;

namespace GamePcChecker.App.Services;

/// <summary>Последняя завершённая сессия стресс-теста для объединения с отчётом.</summary>
public static class StressTestSessionStore
{
    public static StressTestSessionRecord? Last { get; private set; }

    /// <summary>Срабатывает при сохранении новой сессии (после «Стоп» или закрытия окна во время прогона).</summary>
    public static event Action? Changed;

    public static void Commit(StressTestSessionRecord record)
    {
        Last = record;
        Changed?.Invoke();
    }

    public static void Clear()
    {
        Last = null;
        Changed?.Invoke();
    }
}
