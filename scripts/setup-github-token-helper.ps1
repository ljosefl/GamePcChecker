# Helper: GitHub PAT for local scripts (releases). Cursor OAuth is separate — see Show-CursorHint.
# Usage: powershell -ExecutionPolicy Bypass -File .\scripts\setup-github-token-helper.ps1

param([switch]$SkipMenu)

$ErrorActionPreference = 'Stop'

function Show-CursorHint {
    $t = @'

--- Cursor vs PAT ---
  Sign in to GitHub inside Cursor: Settings (Ctrl+,) -> Account / Integrations.
  The GitHub "Cursor" app install does NOT expose a token to PowerShell.
  For create-github-release.ps1 you still need GITHUB_TOKEN (PAT) or "gh auth login".

--- GITHUB_TOKEN ---
  Used by REST API from PowerShell (upload release assets).

'@
    Write-Host $t -ForegroundColor DarkGray
}

function Open-GitHubTokenPages {
    Write-Host 'Opening GitHub token pages in browser...' -ForegroundColor Cyan
    Start-Process 'https://github.com/settings/personal-access-tokens/new'
    Start-Sleep -Milliseconds 400
    Start-Process 'https://github.com/settings/tokens/new'
    $after = @'

Fine-grained: select repo GamePcChecker -> Contents Read and write.
Classic: enable scope "repo".

'@
    Write-Host $after -ForegroundColor Yellow
}

function Invoke-GhAuthLogin {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        Write-Host 'GitHub CLI not found. Install: https://cli.github.com/ then run: gh auth login' -ForegroundColor Yellow
        return
    }
    Write-Host 'Starting gh auth login...' -ForegroundColor Cyan
    & gh auth login
}

function Save-UserGitHubToken {
    Write-Host 'Paste PAT (hidden): ' -NoNewline -ForegroundColor Cyan
    $secure = Read-Host -AsSecureString
    if ($null -eq $secure -or $secure.Length -eq 0) {
        Write-Host 'Empty input, cancelled.' -ForegroundColor Yellow
        return
    }
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    [Environment]::SetEnvironmentVariable('GITHUB_TOKEN', $plain, 'User')
    Write-Host 'Saved User env GITHUB_TOKEN. Restart Cursor/terminal to pick it up in new processes.' -ForegroundColor Green
}

function Test-GitHubToken {
    $token = [Environment]::GetEnvironmentVariable('GITHUB_TOKEN', 'User')
    if ([string]::IsNullOrWhiteSpace($token)) {
        $token = $env:GITHUB_TOKEN
    }
    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Host 'GITHUB_TOKEN is not set (User env or current session).' -ForegroundColor Yellow
        return
    }
    $headers = @{
        Authorization = 'Bearer ' + $token.Trim()
        Accept        = 'application/vnd.github+json'
        'User-Agent'  = 'GamePcChecker-TokenTest'
    }
    try {
        $u = Invoke-RestMethod -Uri 'https://api.github.com/user' -Headers $headers -Method Get
        Write-Host ("OK: API login: {0}" -f $u.login) -ForegroundColor Green
    }
    catch {
        Write-Host "API error: $_" -ForegroundColor Red
    }
}

Show-CursorHint

if ($SkipMenu) {
    return
}

while ($true) {
    $menu = @'

Choose:
  1 - Open browser (new fine-grained + new classic PAT pages)
  2 - Run gh auth login (if GitHub CLI installed)
  3 - Paste PAT -> save to User environment variable GITHUB_TOKEN
  4 - Test GITHUB_TOKEN (GET api.github.com/user)
  0 - Exit

'@
    Write-Host $menu -ForegroundColor White
    $c = Read-Host 'Option'
    switch ($c) {
        '1' { Open-GitHubTokenPages }
        '2' { Invoke-GhAuthLogin }
        '3' { Save-UserGitHubToken }
        '4' { Test-GitHubToken }
        '0' { break }
        default { Write-Host 'Unknown option.' -ForegroundColor Yellow }
    }
}
