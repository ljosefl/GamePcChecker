namespace GamePcChecker.App.Models;

public enum RuntimeCheckStatus
{
    Ok,
    Missing,
    Info,
}

/// <param name="InstallUrl">Ссылка на установщик или страницу загрузки; для Info может быть null.</param>
public sealed record RuntimePrerequisite(
    string Title,
    RuntimeCheckStatus Status,
    string Detail,
    string? InstallUrl)
{
    public string StatusLabel => Status switch
    {
        RuntimeCheckStatus.Ok => "Ок",
        RuntimeCheckStatus.Missing => "Не найдено",
        RuntimeCheckStatus.Info => "Справка",
        _ => "Неизв.",
    };

    public bool HasLink => !string.IsNullOrEmpty(InstallUrl);

    public string LinkLine =>
        string.IsNullOrEmpty(InstallUrl) ? "" : $"Скачать: {InstallUrl}";
}
