# Creates GitHub Release and uploads zip artifacts from artifacts/.
# Requires: $env:GITHUB_TOKEN (fine-grained Contents write or classic repo scope)
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
    Write-Error "Missing file: $ZipPath. Run: .\scripts\publish-release.ps1 $(if ($SelfContained) { '-SelfContained' })"
}

$token = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host @"
GITHUB_TOKEN is not set.

Create a Personal Access Token (repo / Contents write), then:
  `$env:GITHUB_TOKEN = '<token>'
  .\scripts\create-github-release.ps1

Or attach zips manually at https://github.com/$Owner/$Repo/releases/new (tag $Tag).
"@
    exit 1
}

$bearer = "Bearer " + $token.Trim()
$headers = @{
    Authorization = $bearer
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "GamePcChecker-ReleaseScript"
}

$platformNote = if ($SelfContained) {
    "Self-contained (runtime included)."
} else {
    ".NET Desktop Runtime required on target PCs."
}

$releaseBody = @"
## Game PC Checker $Version

Platform: Windows x64 ($platformNote)

1. Download ``$zipName`` from Assets (or the stable-named zip).
2. Extract and run ``GamePcChecker.exe``.
3. For updates: rename ``github-update.example.json`` to ``github-update.json``.

Source: tag ``$Tag``.
"@

$createBody = @{
    tag_name                 = $Tag
    name                     = "Game PC Checker $Version"
    body                     = $releaseBody
    draft                    = $false
    generate_release_notes   = $false
} | ConvertTo-Json

$releaseUri = "https://api.github.com/repos/$Owner/$Repo/releases"
$releaseByTagUri = "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Tag"

Write-Host "Creating release $Tag ..."
$rel = $null
try {
    $rel = Invoke-RestMethod -Uri $releaseUri -Method Post -Headers $headers -Body $createBody -ContentType "application/json; charset=utf-8"
} catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode.value__ }
    if ($status -eq 422) {
        $rel = Invoke-RestMethod -Uri $releaseByTagUri -Headers $headers
        Write-Host "Release exists; uploading assets."
    } else {
        throw
    }
}

if (-not $rel) { throw "Release object missing." }

$uploadHeaders = @{
    Authorization   = $bearer
    Accept          = "application/vnd.github+json"
    "User-Agent"    = "GamePcChecker-ReleaseScript"
    "Content-Type"  = "application/octet-stream"
}

function Upload-Asset {
    param([string]$Path, [string]$AssetName)
    if (-not (Test-Path $Path)) {
        Write-Host "Skip (missing): $Path"
        return
    }
    $uploadUrl = $rel.upload_url -replace '\{\?name,label\}', "?name=$([Uri]::EscapeDataString($AssetName))"
    $mb = [math]::Round((Get-Item $Path).Length / 1MB, 1)
    Write-Host "Upload $AssetName (${mb} MB) ..."
    Invoke-WebRequest -Uri $uploadUrl -Method Post -Headers $uploadHeaders -InFile $Path -UseBasicParsing | Out-Null
}

Upload-Asset -Path $ZipPath -AssetName $zipName

$stableLocalName = "GamePcChecker-$Runtime-$(if ($SelfContained) { 'selfcontained' } else { 'framework-dependent' }).zip"
$StableZipPath = Join-Path $Root "artifacts\$stableLocalName"
if ((Test-Path $StableZipPath) -and ($stableLocalName -ne $zipName)) {
    Upload-Asset -Path $StableZipPath -AssetName $stableLocalName
}

Write-Host ("Done: " + $rel.html_url)
