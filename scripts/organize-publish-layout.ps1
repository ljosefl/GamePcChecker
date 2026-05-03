# After dotnet publish: root = mostly GamePcChecker.exe; docs and sample config go under etc\; data\ and temp\ reserved.
param(
    [Parameter(Mandatory)]
    [string]$PublishDir
)

$ErrorActionPreference = "Stop"
$base = (Resolve-Path -LiteralPath $PublishDir).Path

foreach ($name in @("data", "temp", "etc")) {
    $p = Join-Path $base $name
    if (-not (Test-Path -LiteralPath $p)) {
        New-Item -ItemType Directory -Force -Path $p | Out-Null
    }
}

# Empty folders may be omitted from some zip tools; keep a zero-byte marker.
foreach ($name in @("data", "temp")) {
    $keep = Join-Path (Join-Path $base $name) ".keep"
    if (-not (Test-Path -LiteralPath $keep)) {
        [System.IO.File]::WriteAllText($keep, "")
    }
}

$toMove = @(
    @{ Src = "CHANGELOG.md"; Dest = "etc\CHANGELOG.md" },
    @{ Src = "github-update.example.json"; Dest = "etc\github-update.example.json" }
)

foreach ($item in $toMove) {
    $from = Join-Path $base $item.Src
    $to = Join-Path $base $item.Dest
    if (Test-Path -LiteralPath $from) {
        Move-Item -LiteralPath $from -Destination $to -Force
    }
}
