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
        var (storageHeading, vol, free) = ResolveStorageVolumeForReport(m);

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

        AppendGameDriveVerdicts(verdicts, game, m.GameInstall);

        AppendStressVerdicts(verdicts, stress, m.QuickGpuStress);

        var upgrades = BuildUpgrades(game, m, gpu, gScore, free, stress);
        var peripherals = BuildPeripherals(game, gpu, gScore);

        var footnote = "";

        return new AnalysisReport(
            game.Title,
            game.Notes,
            m,
            cScore,
            gScore,
            gpu,
            storageHeading,
            vol,
            free,
            verdicts,
            upgrades,
            peripherals,
            footnote,
            stress);
    }

    private static void AppendGameDriveVerdicts(
        List<VerdictItem> verdicts,
        GameProfileRecord game,
        GameInstallProbeResult? gi)
    {
        if (gi is not { Found: true })
        {
            if (game.SsdRecommended)
            {
                verdicts.Add(new VerdictItem(
                    VerdictSeverity.Warn,
                    "SSD для игры настоятельно желателен (меньше фризов при подгрузке зон). Папку установки автоматически не нашли — если игра на HDD, перенос на SSD заметно ускорит загрузки."));
            }

            return;
        }

        verdicts.Add(new VerdictItem(
            VerdictSeverity.Ok,
            $"Установка игры: {gi.InstallPath} · том {gi.DriveLetter}: ({gi.DiskKindLabel})."));

        if (game.SsdRecommended && !gi.IsSsdClass)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Bad,
                "Игра на медленном HDD: для Path of Exile сильно рекомендуется перенести на SSD или NVMe — короче загрузки и меньше микрофризов."));
        }
        else if (game.SsdRecommended && gi.IsNvme)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                "Том с игрой — NVMe: оптимально для быстрых подгрузок."));
        }
        else if (game.SsdRecommended && gi.IsSsdClass)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                "Том с игрой — SSD: хороший выбор для комфортной игры."));
        }
    }

    private static void AppendStressVerdicts(
        List<VerdictItem> verdicts,
        StressTestSessionRecord? stress,
        QuickGpuStressMetrics? quick)
    {
        if (quick is { Success: true, IsMeaningful: true } qScan)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                $"Сканирование — стресс GPU+CPU (3×25 с, три уровня нагрузки на шейдер + все ядра CPU): общий итог мин {qScan.MinFps:F0} / ср {qScan.AvgFps:F0} / макс {qScan.MaxFps:F0} FPS (синтетика, не игра). Подробности по этапам — в сводке отчёта."));
        }
        else if (quick is { Success: false })
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                $"Встроенный стресс GPU+CPU при сканировании не выполнен: {quick.Error ?? "неизвестная причина"}."));
        }

        object? fpsSource = null;
        if (stress is { IsMeaningful: true })
            fpsSource = stress;
        else if (quick is { IsMeaningful: true })
            fpsSource = quick;

        if (fpsSource is StressTestSessionRecord stLong)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                $"Окно стресс-теста: {stLong.ShortSummary}"));

            AppendSyntheticFpsThresholdVerdicts(verdicts, stLong.MinFps, stLong.AvgFps, stLong.MaxFps);

            if (stLong.CpuStress && stLong.MinFps >= 30)
            {
                verdicts.Add(new VerdictItem(
                    VerdictSeverity.Ok,
                    "При одновременной нагрузке на CPU минимальный FPS остаётся приемлемым — хороший знак для узких мест CPU+GPU."));
            }

            return;
        }

        if (fpsSource is QuickGpuStressMetrics qm)
            AppendSyntheticFpsThresholdVerdicts(verdicts, qm.MinFps, qm.AvgFps, qm.MaxFps);

        if (stress != null && !stress.IsMeaningful)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                $"Сохранённая сессия старого окна стресс-теста (короткая): {stress.ShortSummary}"));
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                "Эта сессия короче 20 с — для грубых выводов лучше опираться на стресс при «Сканировать» (75 с, GPU+CPU)."));
        }

        if (stress is not { IsMeaningful: true } && quick is not { Success: true, IsMeaningful: true })
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Ok,
                "Полный стресс GPU+CPU (3×25 с, графики °C) выполняется при каждом «Сканировать»."));
        }
    }

    private static void AppendSyntheticFpsThresholdVerdicts(
        List<VerdictItem> verdicts,
        double minFps,
        double avgFps,
        double maxFps)
    {
        if (minFps < 12)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Bad,
                $"Под синтетикой очень низкий минимум FPS (~{minFps:F0}). Проверьте перегрев/троттлинг, драйвер, разгон, фоновые процессы и при симптомах — блок питания и кабели GPU."));
        }
        else if (minFps < 25)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                $"Минимальный FPS под синтетикой умеренный (~{minFps:F0}) — при артефактах или ребутах проверьте охлаждение и БП."));
        }

        if (avgFps > 1 && maxFps > avgFps * 2.5 && minFps < avgFps * 0.4)
        {
            verdicts.Add(new VerdictItem(
                VerdictSeverity.Warn,
                "Большой разброс min/max FPS под синтетикой — возможны просадки (фон, энергосбережение, троттлинг). Для стационарного ПК попробуйте режим «Высокая производительность» в Windows."));
        }
    }

    /// <summary>
    /// Для отчёта и проверки «хватает ли места под игру» используем том, где найдена/указана установка.
    /// Если путь игры неизвестен — как раньше: том с максимальной свободной ёмкостью.
    /// </summary>
    private static (string Heading, string Letter, double FreeGb) ResolveStorageVolumeForReport(MachineSnapshot m)
    {
        var gi = m.GameInstall;
        if (gi is { Found: true, DriveLetter: { } ch })
        {
            double? gb = gi.FreeGbOnVolume;
            if (gb is null)
            {
                var key = ch.ToString();
                foreach (var d in m.Disks)
                {
                    var letterOnly = d.Letter.TrimEnd('\\').TrimEnd(':');
                    if (letterOnly.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        gb = d.FreeGb;
                        break;
                    }
                }
            }

            if (gb is { } g)
                return ("Свободно на томе установки игры", $"{char.ToUpperInvariant(ch)}:", g);
        }

        var best = MaxFreeVolume(m);
        return ("Макс. свободно на томе", best.Letter, best.FreeGb);
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

        if (game.SsdRecommended && machine.GameInstall is not { Found: true, IsSsdClass: true })
            outList.Add("Установить игру на SSD (SATA или NVMe), не на медленный HDD.");

        var stressMin = stress is { IsMeaningful: true }
            ? stress.MinFps
            : machine.QuickGpuStress is { IsMeaningful: true }
                ? machine.QuickGpuStress.MinFps
                : double.PositiveInfinity;

        if (stressMin < 20 && stressMin > 0 && stressMin < double.PositiveInfinity)
            outList.Add("По синтетическому стрессу: низкий минимальный FPS — почистите корпус/радиаторы, проверьте кривую вентиляторов в драйвере GPU; при отвалах/ребутах под нагрузкой — подозрение на БП или кабель питания GPU.");

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
