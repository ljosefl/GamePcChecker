using GamePcChecker.App.Models;

namespace GamePcChecker.App.Services;

public static class AdvisorService
{
    public static AnalysisReport Analyze(
        MachineSnapshot m,
        GameProfileRecord game,
        StressTestSessionRecord? stress = null)
    {
        var gpu = m.Gpus.Count > 0 ? m.Gpus[0] : null;
        var (vol, free) = MaxFreeVolume(m);

        var gScore = gpu != null ? ScoringService.GpuScore(gpu.Name, gpu.VramGb) : 0.0;
        var cScore = ScoringService.CpuScore(m.CpuName, m.CpuCores, m.CpuLogical, m.CpuMhz);

        var verdicts = new List<VerdictItem>();

        if (m.RamGb >= game.RecRamGb)
            verdicts.Add(new VerdictItem(VerdictSeverity.Ok, $"RAM: {m.RamGb:0} ГБ — выше рекомендуемых {game.RecRamGb} ГБ."));
        else if (m.RamGb >= game.MinRamGb)
            verdicts.Add(new VerdictItem(VerdictSeverity.Warn, $"RAM: {m.RamGb:0} ГБ — выше минимума ({game.MinRamGb}), но ниже рекомендуемых {game.RecRamGb} ГБ."));
        else
            verdicts.Add(new VerdictItem(VerdictSeverity.Bad, $"RAM: мало для минимума (нужно ≥ {game.MinRamGb} ГБ, у вас {m.RamGb:F1} ГБ)."));

        if (m.CpuCores >= game.RecCpuCores)
            verdicts.Add(new VerdictItem(VerdictSeverity.Ok, $"Ядра CPU: {m.CpuCores} — не ниже рекомендуемых {game.RecCpuCores}."));
        else if (m.CpuCores >= game.MinCpuCores)
            verdicts.Add(new VerdictItem(VerdictSeverity.Warn, $"Ядра CPU: {m.CpuCores} — минимум ({game.MinCpuCores}) есть; для комфорта в PoE часто лучше ≥ {game.RecCpuCores}."));
        else
            verdicts.Add(new VerdictItem(VerdictSeverity.Bad, $"Ядра CPU: мало (нужно ≥ {game.MinCpuCores})."));

        if (gpu != null)
        {
            if (gpu.VramGb >= game.RecVramGb)
                verdicts.Add(new VerdictItem(VerdictSeverity.Ok, $"VRAM: {gpu.VramGb:F1} ГБ ({gpu.VramMeasuredBy}) — не ниже ориентира «рекомендуемые» ({game.RecVramGb} ГБ)."));
            else if (gpu.VramGb >= game.MinVramGb)
                verdicts.Add(new VerdictItem(VerdictSeverity.Warn, $"VRAM: {gpu.VramGb:F1} ГБ ({gpu.VramMeasuredBy}) — ок для многих конфигов, для «{game.Title}» комфортнее ближе к {game.RecVramGb} ГБ."));
            else
                verdicts.Add(new VerdictItem(VerdictSeverity.Bad, $"VRAM: может не хватить (ориентир минимум ~{game.MinVramGb} ГБ)."));

            if (gScore >= game.RecGpuScore)
                verdicts.Add(new VerdictItem(VerdictSeverity.Ok, "Видеокарта по условной шкале — уровень «рекомендуемые» или выше."));
            else if (gScore >= game.MinGpuScore)
                verdicts.Add(new VerdictItem(VerdictSeverity.Warn, "Видеокарта по условной шкале — между минимумом и рекомендуемыми: низкие/средние пресеты, следите за VRAM."));
            else
                verdicts.Add(new VerdictItem(VerdictSeverity.Bad, "Видеокарта по условной шкале ниже официального минимума для этой игры — урезайте качество или планируйте апгрейд GPU."));
        }
        else
            verdicts.Add(new VerdictItem(VerdictSeverity.Bad, "Нет подходящей дискретной GPU для проверки."));

        if (free >= game.StorageNeedGb)
            verdicts.Add(new VerdictItem(VerdictSeverity.Ok, $"Место на диске: на томе {vol} достаточно под установку (~{game.StorageNeedGb} ГБ)."));
        else
            verdicts.Add(new VerdictItem(VerdictSeverity.Bad, $"Место: на самом большом томе только {free:0} ГБ — нужно ≥ {game.StorageNeedGb} ГБ на одном разделе."));

        if (game.SsdRecommended)
            verdicts.Add(new VerdictItem(VerdictSeverity.Warn, "SSD для игры настоятельно желателен (меньше фризов при подгрузке зон)."));

        AppendStressVerdicts(verdicts, stress);

        var upgrades = BuildUpgrades(game, m, gpu, gScore, free, stress);
        var peripherals = BuildPeripherals(game, gpu, gScore);

        var footnote =
            "Оценки GPU/CPU условные. VRAM дискретной карты берётся из DXGI (драйвер NVIDIA / AMD / Intel), не из WMI — WMI часто ошибается для объёмов >4 ГБ. " +
            "Итог по комфорту в игре — по FPS и стабильности кадра; стресс-тест — синтетика, не заменяет реальную сцену в «" + game.Title + "».";
        if (stress != null)
            footnote += " Учтена последняя сессия стресс-теста (длительность и FPS — ориентир стабильности под нагрузкой).";

        return new AnalysisReport(
            game.Title,
            game.Notes,
            m,
            cScore,
            gScore,
            gpu,
            vol,
            free,
            verdicts,
            upgrades,
            peripherals,
            footnote,
            stress);
    }

    private static void AppendStressVerdicts(
        List<VerdictItem> verdicts,
        StressTestSessionRecord? stress)
    {
        if (stress == null)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                "Стресс-тест (необязательно): пока не выполнялся — после сканирования можно запустить «Стресс-тест GPU»; отчёт обновится сам или нажмите «Сканировать» ещё раз."));
            return;
        }

        verdicts.Add(new VerdictItem(
            VerdictSeverity.Ok,
            $"Стресс-тест учтён: {stress.ShortSummary}"));

        if (!stress.IsMeaningful)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                "Сессия стресс-теста короткая (< 20 с) — для выводов о стабильности повторите 2–5 минут."));
            return;
        }

        if (stress.MinFps < 12)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Bad,
                $"Под синтетикой очень низкий минимум FPS (~{stress.MinFps:F0}). Проверьте перегрев/троттлинг, драйвер, разгон, фоновые процессы и при симптомах — блок питания и кабели GPU."));
        }
        else if (stress.MinFps < 25)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                $"Минимальный FPS в стресс-сессии умеренный (~{stress.MinFps:F0}) — при артефактах или ребутах наглядно проверьте охлаждение и БП."));
        }

        if (stress.AvgFps > 1 && stress.MaxFps > stress.AvgFps * 2.5 && stress.MinFps < stress.AvgFps * 0.4)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                "В стресс-сессии большой разброс min/max FPS — возможны просадки (фон, энергосбережение, троттлинг). Убедитесь, что в Windows выбран режим «Высокая производительность» для стационарного ПК."));
        }

        if (stress.CpuStress && stress.MinFps >= 30)
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                "При одновременной нагрузке на CPU минимальный FPS остаётся приемлемым — хороший знак для узких мест CPU+GPU."));
    }

    private static (string Letter, double FreeGb) MaxFreeVolume(MachineSnapshot m)
    {
        (string Letter, double FreeGb) best = ("?", 0.0);
        foreach (var d in m.Disks)
        {
            if (d.FreeGb > best.FreeGb)
                best = (d.Letter, d.FreeGb);
        }

        return best;
    }

    private static IReadOnlyList<string> BuildUpgrades(
        GameProfileRecord game,
        MachineSnapshot machine,
        GpuCard? gpu,
        double gScore,
        double freeMax,
        StressTestSessionRecord? stress)
    {
        var outList = new List<string>();
        if (freeMax < game.StorageNeedGb)
            outList.Add("Освободить место на одном диске под игру (или добавить SSD/HDD для данных).");

        if (gpu != null && gScore < game.MinGpuScore)
            outList.Add("Видеокарта — главный узел для FPS; ориентир — модели из блока «рекомендуемые» в Steam для этой игры.");
        else if (gpu != null && gScore < game.RecGpuScore)
            outList.Add("Для высоких настроек и стабильного FPS — апгрейд GPU даст больше, чем периферия.");

        if (machine.CpuCores < game.RecCpuCores)
            outList.Add("CPU: больше ядер снижает просадки в «шумных» боях; платформа с 6+ физ. ядрами — заметный шаг.");

        if (machine.RamGb < game.RecRamGb)
            outList.Add($"Довести RAM до {game.RecRamGb} ГБ+, если слоты и платформа позволяют.");

        if (game.SsdRecommended)
            outList.Add("Установить игру на SSD (SATA или NVMe), не на медленный HDD.");

        if (stress is { IsMeaningful: true, MinFps: < 20 })
            outList.Add("По стресс-сессии: низкий минимальный FPS — почистите корпус/радиаторы, проверьте кривую вентиляторов в драйвере GPU; при отвалах/ребутах под нагрузкой — подозрение на БП или кабель питания GPU.");

        if (stress is { IsMeaningful: true } &&
            machine.SystemHealthSummary.Contains("ОЗУ почти заполнено", StringComparison.Ordinal))
            outList.Add("Мало свободной ОЗУ при проверке — закройте браузер и тяжёлые фоновые приложения перед игрой.");

        if (outList.Count == 0)
            outList.Add("По основным метрикам конфиг близок к рекомендуемым или выше; дальше — настройки графики и стабильный интернет.");

        return outList;
    }

    private static IReadOnlyList<string> BuildPeripherals(GameProfileRecord game, GpuCard? gpu, double gScore)
    {
        var tips = new List<string>
        {
            "Сеть: для онлайн-ARPG стабильнее Ethernet к роутеру, чем слабый Wi‑Fi.",
            "Монитор: 24–27″ 1080p/1440p — разумный компромисс; высокий FPS имеет смысл, если GPU реально выдаёт столько кадров.",
            "Мышь: лёгкая с хорошим сенсором; размер под хват для длинных сессий.",
            "Клавиатура: n-key rollover по желанию для частых нажатий скиллов.",
            "Звук: слышимость лута/скиллов — по вкусу.",
        };

        if (gpu != null && gScore < game.RecGpuScore)
            tips.Insert(1, "Монитор с FreeSync/G-Sync Compatible сглаживает нестабильный FPS (не заменяет апгрейд GPU).");

        return tips;
    }
}
