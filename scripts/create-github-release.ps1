# Создаёт GitHub Release для уже существующего тега и загружает zip из artifacts/.
# Нужен fine-grained или classic PAT с правом Contents: Read and write (repo для classic).
#   $env:GITHUB_TOKEN = "ghp_...."
#   .\scripts\create-github-release.ps1
param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Owner = "ljosefl"
$Repo = "GamePcChecker"
$Tag = "v$Version"

$zipName = if ($SelfContained) {
    "GamePcChecker-v$Version-$Runtime-selfcontained.zip"
} else {
    "GamePcChecker-v$Version-$Runtime-framework-dependent.zip"
}
$ZipPath = Join-Path $Root "artifacts\$zipName"

if (-not (Test-Path $ZipPath)) {
    Write-Error "Нет файла: $ZipPath. Сначала выполните: .\scripts\publish-release.ps1 $(if ($SelfContained) { '-SelfContained' })"
}

$token = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host @"
Не задан GITHUB_TOKEN.

Вариант А — веб-интерфейс:
  1. Откройте https://github.com/$Owner/$Repo/releases/new
  2. Выберите тег $Tag → заголовок «Game PC Checker $Version»
  3. Перетащите архив: $ZipPath

Вариант Б — CLI (после winget install GitHub.cli):
  gh release create $Tag `"$ZipPath`" --title `"Game PC Checker $Version`" --notes `"См. README`"

Вариант В — повторно запустите этот скрипт после:
  `$env:GITHUB_TOKEN = '<ваш токен>'
  .\scripts\create-github-release.ps1`
"@
    exit 1
}

$headers = @{
    Authorization = "Bearer $token"
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "GamePcChecker-ReleaseScript"
}

$releaseBody = @"
## Game PC Checker $Version

**Платформа:** Windows x64 ($(if ($SelfContained) { 'self-contained, рантайм внутри папки' } else { 'нужен установленный .NET Desktop той же линии' }))

1. Скачайте ``$zipName`` из раздела Assets.
2. Распакуйте архив и запустите ``GamePcChecker.exe``.
3. Для проверки обновлений переименуйте ``github-update.example.json`` в ``github-update.json`` (owner/repo уже указаны для этого репозитория).

Исходники: тег ``$Tag``.
"@

$createBody = @{
    tag_name         = $Tag
    name             = "Game PC Checker $Version"
    body             = $releaseBody
    draft            = $false
    generate_release_notes = $false
} | ConvertTo-Json

$releaseUri = "https://api.github.com/repos/$Owner/$Repo/releases"
$releaseByTagUri = "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Tag"

Write-Host "Создание релиза $Tag ..."
$rel = $null
try {
    $rel = Invoke-RestMethod -Uri $releaseUri -Method Post -Headers $headers -Body $createBody -ContentType "application/json; charset=utf-8"
} catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode.value__ }
    if ($status -eq 422) {
        try {
            $rel = Invoke-RestMethod -Uri $releaseByTagUri -Headers $headers
            Write-Host "Релиз для тега уже есть на GitHub — загружаем архив в него."
        } catch {
            throw
        }
    } else {
        throw
    }
}

if (-not $rel) { throw "Не удалось получить объект релиза." }

$uploadHeaders = @{
    Authorization = "Bearer $token"
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "GamePcChecker-ReleaseScript"
    "Content-Type"  = "application/octet-stream"
}

function Upload-Asset {
    param([string]$Path, [string]$AssetName)
    if (-not (Test-Path $Path)) {
        Write-Host "Пропуск (нет файла): $Path"
        return
    }
    $uploadUrl = $rel.upload_url -replace '\{\?name,label\}', "?name=$([Uri]::EscapeDataString($AssetName))"
    $mb = [math]::Round((Get-Item $Path).Length / 1MB, 1)
    Write-Host "Загрузка $AssetName (${mb} MB) ..."
    Invoke-WebRequest -Uri $uploadUrl -Method Post -Headers $uploadHeaders -InFile $Path -UseBasicParsing | Out-Null
}

Upload-Asset -Path $ZipPath -AssetName $zipName

$stableLocalName = "GamePcChecker-$Runtime-$(if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }).zip"
$StableZipPath = Join-Path $Root "artifacts\$stableLocalName"
if ((Test-Path $StableZipPath) -and ($stableLocalName -ne $zipName)) {
    Upload-Asset -Path $StableZipPath -AssetName $stableLocalName
}

Write-Host "Готово: $($rel.html_url)"
