# Первый push в https://github.com/ljosefl/GamePcChecker
# Перед запуском: на GitHub создайте пустой репозиторий GamePcChecker (public), без README или со слиянием позже.
$ErrorActionPreference = "Stop"
Set-Location (Split-Path -Parent $PSScriptRoot)

if (-not (Test-Path .git)) {
    git init
}

# Локально для этого репозитория (без --global), иначе commit не создаётся
git config user.email "ljosefl@users.noreply.github.com"
git config user.name "ljosefl"

git add -A
$st = git status --porcelain
if ($st) {
    git commit -m "Initial commit: Game PC Checker"
}
elseif (-not (git rev-parse HEAD 2>$null)) {
    git commit -m "Initial commit: Game PC Checker" --allow-empty
}

git branch -M main 2>$null

$originUrl = "https://github.com/ljosefl/GamePcChecker.git"
$r = @(git remote 2>$null)
if ($r -contains 'origin') {
    git remote set-url origin $originUrl
}
else {
    git remote add origin $originUrl
}

Write-Host "Выполняется: git push -u origin main"
git push -u origin main
