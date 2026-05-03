# Публикация Game PC Checker для установки на другие ПК (Windows x64).
# Требуется установленный .NET SDK (совместимый с TargetFramework проекта).
param(
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    # По умолчанию один exe в корне и раскладка data/temp/etc; включите для классической папки с множеством dll.
    [switch]$MultiFile
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Proj = Join-Path $Root "src\GamePcChecker.App\GamePcChecker.App.csproj"
$Ver = (Select-String -Path $Proj -Pattern '<Version>(.+)</Version>' | ForEach-Object { $_.Matches.Groups[1].Value } | Select-Object -First 1)
if (-not $Ver) { $Ver = "unknown" }

$OutName = if ($SelfContained) { "publish-$Runtime-selfcontained" } else { "publish-$Runtime-fdd" }
$OutDir = Join-Path $Root "artifacts\$OutName"

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$useSingleFile = -not $MultiFile

$dotnetArgs = @(
    "publish", $Proj,
    "-c", "Release",
    "-r", $Runtime,
    "-o", $OutDir,
    "--self-contained", ($(if ($SelfContained) { "true" } else { "false" })),
    "-p:PublishSingleFile=$(if ($useSingleFile) { 'true' } else { 'false' })",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

if ($useSingleFile) {
    $dotnetArgs += "-p:EnableCompressionInSingleFile=true"
}

Write-Host "dotnet $($dotnetArgs -join ' ')"
& dotnet @dotnetArgs

& (Join-Path $PSScriptRoot "organize-publish-layout.ps1") -PublishDir $OutDir

$Zip = Join-Path $Root "artifacts\GamePcChecker-v$Ver-$Runtime-$(if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }).zip"
New-Item -ItemType Directory -Force -Path (Split-Path $Zip) | Out-Null
if (Test-Path $Zip) { Remove-Item $Zip -Force }

# tar handles large trees more reliably than Compress-Archive (AV file locks on fresh dlls).
$zipName = Split-Path $Zip -Leaf
$parent = Split-Path $Zip
Push-Location $OutDir
try {
    & tar.exe -c -a -f (Join-Path $parent $zipName) *
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit $LASTEXITCODE" }
}
finally {
    Pop-Location
}

# Постоянное имя для README (releases/latest/download/...) и GitHub Release
$StableZip = Join-Path $Root "artifacts\GamePcChecker-$Runtime-$(if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }).zip"
Copy-Item -LiteralPath $Zip -Destination $StableZip -Force

Write-Host ('Done output: ' + $OutDir)
Write-Host ('Zip versioned: ' + $Zip)
Write-Host ('Zip stable name: ' + $StableZip)
Write-Host ""
Write-Host "Framework-dependent: without -SelfContained you need .NET Desktop Runtime on target PCs."
Write-Host "Self-contained: runtime included. Default: single-file exe + data/temp/etc; use -MultiFile for a flat dll layout."
