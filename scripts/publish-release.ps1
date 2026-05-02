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

# Постоянное имя для README (releases/latest/download/...) и GitHub Release
$StableZip = Join-Path $Root "artifacts\GamePcChecker-$Runtime-$(if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }).zip"
Copy-Item -LiteralPath $Zip -Destination $StableZip -Force

Write-Host ('Done output: ' + $OutDir)
Write-Host ('Zip versioned: ' + $Zip)
Write-Host ('Zip stable name: ' + $StableZip)
Write-Host ""
Write-Host "Framework-dependent: without -SelfContained you need .NET Desktop Runtime on target PCs."
Write-Host "Self-contained: heavier folder but no separate runtime install."
