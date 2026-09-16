# MSIX pour Microsoft Store (winapp package + layout self-contained).
param(
    [ValidateSet("x64", "arm64", "both", "")]
    [string]$Arch = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "winboard-release-lib.ps1")
Initialize-WbConsoleUtf8
Ensure-WinAppCli

$projectRoot = Get-WbProjectRoot
$paths = Get-WbSyncPaths
$msixStoreDir = Join-Path $projectRoot "msix-store"
$layoutDir = Join-Path $msixStoreDir "layout"
$manifestPath = Join-Path $msixStoreDir "Package.appxmanifest"
$configPath = Join-Path $msixStoreDir "store-package.config.json"

if (-not (Test-Path $configPath)) {
    throw "Config introuvable : msix-store/store-package.config.json"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
if ($config.packageName -like "REPLACE_*" -or $config.publisher -like "*REPLACE_*") {
    throw "Remplissez msix-store/store-package.config.json (packageName, publisher Partner Center) avant MSIX Store."
}

$version = Get-WbVersion
$msixVersion = ConvertTo-MsixVersion -Version $version
Write-Host "Version app: $version (MSIX: $msixVersion)" -ForegroundColor Cyan

$Arch = Resolve-WbArchParam -Arch $Arch -Kind "MSIX Store"
$labels = Get-WbArchLabels -Arch $Arch
New-Item -ItemType Directory -Force -Path $paths.MsixFolder | Out-Null

$built = New-Object System.Collections.Generic.List[string]

foreach ($label in $labels) {
    Write-Host "`n=== MSIX ($label) ===" -ForegroundColor Cyan
    try {
        $publishDir = Publish-WinBoardPortable -ArchLabel $label
        $exePath = Join-Path $publishDir "WinBoard.exe"
        if (-not (Test-Path $exePath)) { throw "WinBoard.exe introuvable apres publish" }

        New-Item -ItemType Directory -Force -Path $layoutDir | Out-Null
        Get-ChildItem -Path $layoutDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
        Copy-Item -Path (Join-Path $publishDir "*") -Destination $layoutDir -Recurse -Force

        if (-not (Test-Path $manifestPath)) {
            $logo = Join-Path $msixStoreDir "Assets\StoreLogo.png"
            if (-not (Test-Path $logo)) { throw "Logo MSIX manquant : msix-store/Assets/StoreLogo.png" }
            Push-Location $msixStoreDir
            try {
                winapp manifest generate . `
                    --package-name $config.packageName `
                    --publisher-name $config.publisher `
                    --version $msixVersion `
                    --description $config.displayName `
                    --executable WinBoard.exe `
                    --logo-path $logo `
                    --if-exists Overwrite
            } finally {
                Pop-Location
            }
        }

        $destName = "WinBoard $version $label.msix"
        $destPath = Join-Path $paths.MsixFolder $destName
        Push-Location $msixStoreDir
        try {
            winapp package .\layout --manifest .\Package.appxmanifest --output $destPath
            if ($LASTEXITCODE -ne 0) { throw "winapp package a echoue" }
        } finally {
            Pop-Location
        }

        Write-Host "MSIX Store : $destPath" -ForegroundColor Green
        $built.Add($destPath)
    } catch {
        Write-Warning "Echec MSIX $label : $($_.Exception.Message)"
    }
}

if ($built.Count -eq 0) { throw "Aucun MSIX produit." }
Write-Host "Deposez les .msix dans Partner Center (x64 + arm64 si les deux)." -ForegroundColor Green
explorer.exe $paths.MsixFolder
exit 0
