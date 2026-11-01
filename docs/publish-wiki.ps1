# Publishes docs/wiki/*.md to the GitHub wiki of this repo.
#
# One-time setup: GitHub only creates the wiki's git repository after a first page exists.
# Open the repo's Wiki tab, click "Create the first page", then Save (any text is fine).
# After that, run:   .\docs\publish-wiki.ps1
param([string]$Repo = 'TanmayS0406/MonteCarlo-Knowledge-Twin')

$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'wiki'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("wiki-" + [guid]::NewGuid())

git clone "https://github.com/$Repo.wiki.git" $tmp
if ($LASTEXITCODE -ne 0) {
    throw "Could not clone the wiki. Create the first page in the Wiki tab on GitHub, then run this again."
}

try {
    Get-ChildItem $tmp -Exclude '.git' | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $src '*.md') $tmp
    Push-Location $tmp
    git add -A
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Host 'Wiki is already up to date.'; return }
    git commit -m 'Update wiki from docs/wiki'
    git push
    Write-Host "Published: https://github.com/$Repo/wiki" -ForegroundColor Green
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
