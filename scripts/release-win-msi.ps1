# MSI + ZIP portable (self-contained WinApp SDK 2.4) pour usage sans Store.
param(
    [ValidateSet("x64", "arm64", "both", "")]
    [string]$Arch = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "winboard-release-lib.ps1")
Initialize-WbConsoleUtf8

$paths = Get-WbSyncPaths
$version = Get-WbVersion
$Arch = Resolve-WbArchParam -Arch $Arch -Kind "MSI / portable"
$labels = Get-WbArchLabels -Arch $Arch

New-Item -ItemType Directory -Force -Path $paths.MsiFolder | Out-Null
New-Item -ItemType Directory -Force -Path $paths.PortableFolder | Out-Null

$builtMsi = New-Object System.Collections.Generic.List[string]
$builtZip = New-Object System.Collections.Generic.List[string]

Ensure-WixToolset
Stop-WinBoardProcess

foreach ($label in $labels) {
    Write-Host "`n=== Publish portable ($label) ===" -ForegroundColor Cyan
    Stop-WinBoardProcess
    $publishDir = Publish-WinBoardPortable -ArchLabel $label

    $zipName = "WinBoard $version $label.zip"
    $zipPath = Join-Path $paths.PortableFolder $zipName
    Copy-WinBoardPortableZip -SourceDir $publishDir -DestZip $zipPath
    Write-Host "ZIP portable : $zipPath" -ForegroundColor Green
    $builtZip.Add($zipPath)

    $msiName = "WinBoard $version $label.msi"
    $msiPath = Join-Path $paths.MsiFolder $msiName
    $ok = New-WinBoardMsiFromFolder -SourceDir $publishDir -DestMsi $msiPath -ProductName "WinBoard" -Version $version
    if ($ok) {
        Write-Host "MSI : $msiPath" -ForegroundColor Green
        $builtMsi.Add($msiPath)
    } else {
        Write-Warning "WiX Toolset absent : MSI non genere pour $label. ZIP portable disponible."
    }
}

Write-Host "`nResume : $($builtZip.Count) ZIP, $($builtMsi.Count) MSI"
if ($builtMsi.Count -gt 0) { explorer.exe $paths.MsiFolder }
elseif ($builtZip.Count -gt 0) { explorer.exe $paths.PortableFolder }
else { throw "Aucun artefact produit." }

exit 0
