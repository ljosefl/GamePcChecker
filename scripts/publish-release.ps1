# Публикация Game PC Checker для установки на другие ПК (Windows x64).
# Требуется установленный .NET SDK (совместимый с TargetFramework проекта).
param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Proj = Join-Path $Root "src\GamePcChecker.App\GamePcChecker.App.csproj"
$Ver = (Select-String -Path $Proj -Pattern '<Version>(.+)</Version>' | ForEach-Object { $_.Matches.Groups[1].Value } | Select-Object -First 1)
if (-not $Ver) { $Ver = "unknown" }

$OutName = if ($SelfContained) { "publish-$Runtime-selfcontained" } else { "publish-$Runtime-fdd" }
$OutDir = Join-Path $Root "artifacts\$OutName"

$args = @(
    "publish", $Proj,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $OutDir,
    "--self-contained", ($(if ($SelfContained) { "true" } else { "false" })),
    "-p:PublishSingleFile=$(if ($SingleFile) { 'true' } else { 'false' })",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

Write-Host "dotnet $($args -join ' ')"
& dotnet @args

$Zip = Join-Path $Root "artifacts\GamePcChecker-v$Ver-$Runtime-$(if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }).zip"
New-Item -ItemType Directory -Force -Path (Split-Path $Zip) | Out-Null
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $Zip
Write-Host "Готово: $OutDir"
Write-Host "Архив: $Zip"
Write-Host ""
Write-Host "Framework-dependent: без --SelfContained — на целевых ПК нужен .NET Desktop Runtime той же основной версии."
Write-Host "Self-contained: .\publish-release.ps1 -SelfContained — тяжелее, но без установки рантайма."
