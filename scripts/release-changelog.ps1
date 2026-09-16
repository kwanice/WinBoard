# Workflow release : version WinBoard.csproj + changelog FR/EN + commit + tag.
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "winboard-release-lib.ps1")
Initialize-WbConsoleUtf8

$projectRoot = Get-WbProjectRoot
$docsDir = Get-WbDocsDir
New-Item -ItemType Directory -Force -Path $docsDir | Out-Null

$commitFile = Join-Path $docsDir "changelog_commit.txt"
$changelogFr = Join-Path $docsDir "changelog_fr.txt"
$changelogEn = Join-Path $docsDir "changelog_en.txt"
$emptyChangelog = "# Changelog`n`n(Notes de version a rediger.)`n"

$currentName = Get-WbVersion

Write-Host ""
Write-Host ("=" * 60)
Write-Host "Workflow release WinBoard"
Write-Host ("=" * 60)
Write-Host "Version courante : $currentName"
$newName = Read-Host "Nouvelle version (ex. 0.3.2)"
if ([string]::IsNullOrWhiteSpace($newName)) {
    Write-Host "Annule."
    exit 0
}

if (-not (Read-WbConfirm "Confirmer la version $newName ?")) {
    Write-Host "Annule."
    exit 0
}

Set-WbVersion -Version $newName
Write-Host "WinBoard.csproj + README mis a jour."

if (-not (Read-WbConfirm "Generer le brouillon changelog depuis Git ?")) {
    Write-Host "Arret. Version conservee sans changelog."
    exit 0
}

Set-Location $projectRoot
$lastTag = git describe --tags --abbrev=0 2>$null
$range = if ($lastTag) { "$lastTag..HEAD" } else { "HEAD" }
$lines = git log $range --pretty=format:"- %s" 2>$null
if (-not $lines) { $lines = "- (aucun commit depuis le dernier tag)" }
[System.IO.File]::WriteAllText($commitFile, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))

if (-not (Test-Path $changelogFr)) { [System.IO.File]::WriteAllText($changelogFr, $emptyChangelog, [System.Text.UTF8Encoding]::new($false)) }
if (-not (Test-Path $changelogEn)) { [System.IO.File]::WriteAllText($changelogEn, $emptyChangelog, [System.Text.UTF8Encoding]::new($false)) }

Start-Process $commitFile
Start-Sleep -Milliseconds 400
Start-Process $changelogFr
Start-Sleep -Milliseconds 400
Start-Process $changelogEn
Write-Host ""
Write-Host "Editez changelog_fr.txt / changelog_en.txt puis Entree."
Read-Host

if (-not (Read-WbConfirm "Creer le commit 'chore: bump to version $newName' ?")) {
    Write-Host "Commit annule."
    exit 0
}

git add src/WinBoard/WinBoard.csproj README.md docs/changelog_commit.txt docs/changelog_fr.txt docs/changelog_en.txt
if ($LASTEXITCODE -ne 0) { exit 1 }
git commit -m "chore: bump to version $newName"
if ($LASTEXITCODE -ne 0) { exit 1 }

if (-not (Read-WbConfirm "Creer le tag v$newName ?")) {
    Write-Host "Termine sans tag."
    exit 0
}

git tag -a "v$newName" -m "Version $newName"
Write-Host "Tag v$newName cree. Pensez a : git push && git push --tags"
exit 0
