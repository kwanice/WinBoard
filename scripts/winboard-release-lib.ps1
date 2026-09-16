$ErrorActionPreference = "Stop"

function Initialize-WbConsoleUtf8 {
    try {
        $null = cmd /c "chcp 65001>nul" 2>$null
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = $utf8
        [Console]::InputEncoding = $utf8
        $script:OutputEncoding = $utf8
    } catch { }
}

function Get-WbProjectRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-WbCsprojPath {
    return Join-Path (Get-WbProjectRoot) "src\WinBoard\WinBoard.csproj"
}

function Get-WbReadmePath {
    return Join-Path (Get-WbProjectRoot) "README.md"
}

function Get-WbDocsDir {
    return Join-Path (Get-WbProjectRoot) "docs"
}

function Get-WbSyncPaths {
    $syncRoot = "C:\Users\Frank\OneDrive\Progr\WinBoard"
    return @{
        SyncRoot       = $syncRoot
        MsiFolder      = Join-Path $syncRoot "Msi"
        MsixFolder     = Join-Path $syncRoot "Msix"
        PortableFolder = Join-Path $syncRoot "Portable"
    }
}

function Get-WbHostArchLabel {
    if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
        return "arm64"
    }
    return "x64"
}

function Get-WbVersion {
    $csproj = Get-WbCsprojPath
    if (-not (Test-Path $csproj)) { return "0.0.0" }
    $xml = [xml](Get-Content $csproj -Raw)
    $v = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if ($v) { return $v.Trim() }
    return "0.0.0"
}

function Set-WbVersion {
    param([string]$Version)
    $csproj = Get-WbCsprojPath
    $content = Get-Content $csproj -Raw
    $parts = $Version.Split(".")
    while ($parts.Count -lt 4) { $parts += "0" }
    $assemblyVersion = ($parts[0..3] -join ".")

    $content = [regex]::Replace($content, '(<Version>)[^<]*(</Version>)', "`${1}$Version`${2}")
    $content = [regex]::Replace($content, '(<AssemblyVersion>)[^<]*(</AssemblyVersion>)', "`${1}$assemblyVersion`${2}")
    $content = [regex]::Replace($content, '(<FileVersion>)[^<]*(</FileVersion>)', "`${1}$assemblyVersion`${2}")
    [System.IO.File]::WriteAllText($csproj, $content, [System.Text.UTF8Encoding]::new($false))

    $readme = Get-WbReadmePath
    if (Test-Path $readme) {
        $readmeContent = Get-Content $readme -Raw
        $readmeContent = [regex]::Replace($readmeContent, '\*\*Version [^*]+\*\*', "**Version $Version**")
        [System.IO.File]::WriteAllText($readme, $readmeContent, [System.Text.UTF8Encoding]::new($false))
    }
}

function Read-WbConfirm {
    param([string]$Message)
    $r = Read-Host "$Message (o/n)"
    if ([string]::IsNullOrEmpty($r)) { return $false }
    $r = $r.Trim().ToLower()
    return $r -eq 'o' -or $r -eq 'oui' -or $r -eq 'y' -or $r -eq 'yes'
}

function Resolve-WbArchParam {
    param(
        [string]$Arch,
        [string]$Kind = "build"
    )
    if (-not [string]::IsNullOrWhiteSpace($Arch)) { return $Arch.Trim().ToLower() }
    $defaultChoice = if ((Get-WbHostArchLabel) -eq "arm64") { "1" } else { "2" }
    while ($true) {
        Write-Host ""
        Write-Host "Architecture pour $Kind ?" -ForegroundColor Yellow
        Write-Host "  [1] ARM64"
        Write-Host "  [2] x64"
        Write-Host "  [3] Les deux"
        $choice = Read-Host "Choix (1/2/3, defaut $defaultChoice, q annuler)"
        if ([string]::IsNullOrWhiteSpace($choice)) { $choice = $defaultChoice }
        switch ($choice.Trim().ToLower()) {
            "1" { return "arm64" }
            "2" { return "x64" }
            "3" { return "both" }
            "q" { throw "Annule." }
            default { Write-Host "Choix invalide." -ForegroundColor DarkYellow }
        }
    }
}

function Get-WbArchLabels {
    param([string]$Arch)
    switch ($Arch) {
        "both" { return @("arm64", "x64") }
        "arm64" { return @("arm64") }
        "x64" { return @("x64") }
        default { throw "Architecture inconnue: $Arch" }
    }
}

function Get-WbMsbuildPlatform {
    param([string]$ArchLabel)
    if ($ArchLabel -eq "arm64") { return "ARM64" }
    return "x64"
}

function Get-WbRuntimeIdentifier {
    param([string]$ArchLabel)
    if ($ArchLabel -eq "arm64") { return "win-arm64" }
    return "win-x64"
}

function Stop-WinBoardProcess {
    $existing = Get-Process -Name "WinBoard" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-WbStep "Arret de l'instance WinBoard en cours (PID $($existing.Id -join ', '))..."
        $existing | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
}

function Write-WbBanner {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor DarkCyan
    Write-Host $Title -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor DarkCyan
}

function Write-WbStep {
    param([string]$Message)
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] $Message"
    [Console]::Out.Flush()
}

function Write-WbOk {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
    [Console]::Out.Flush()
}

function Write-WbFail {
    param([string]$Message)
    Write-Host "[ERREUR] $Message" -ForegroundColor Red
    [Console]::Out.Flush()
}

function Get-WbTargetFramework {
    $content = Get-Content (Get-WbCsprojPath) -Raw
    $m = [regex]::Match($content, '<TargetFramework>([^<]+)</TargetFramework>')
    if (-not $m.Success) { throw "TargetFramework introuvable dans le csproj." }
    return $m.Groups[1].Value.Trim()
}

function Get-WinBoardExePath {
    param(
        [string]$Configuration,
        [string]$ArchLabel
    )
    $platform = Get-WbMsbuildPlatform -ArchLabel $ArchLabel
    $tfm = Get-WbTargetFramework
    return Join-Path (Get-WbProjectRoot) "src\WinBoard\bin\$platform\$Configuration\$tfm\WinBoard.exe"
}

function Build-WinBoardProject {
    param(
        [string]$Configuration,
        [string]$ArchLabel
    )
    $csproj = Get-WbCsprojPath
    $platform = Get-WbMsbuildPlatform -ArchLabel $ArchLabel
    $version = Get-WbVersion

    Write-WbStep "Compilation $Configuration / $platform (v$version)..."
    Write-WbStep "dotnet build -> $csproj"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    $code = Invoke-WbDotNet @(
        "build", $csproj,
        "-c", $Configuration,
        "-p:Platform=$platform",
        "-v", "minimal",
        "-consoleLoggerParameters:Summary;ForceNoAlign"
    )
    $sw.Stop()

    if ($code -ne 0) {
        Write-WbFail "Compilation echouee (code $code) apres $($sw.Elapsed.TotalSeconds.ToString('0.0')) s."
        return $false
    }

    $exe = Get-WinBoardExePath -Configuration $Configuration -ArchLabel $ArchLabel
    if (-not (Test-Path $exe)) {
        Write-WbFail "Build OK mais exe introuvable : $exe"
        return $false
    }

    Write-WbOk "Compilation reussie en $($sw.Elapsed.TotalSeconds.ToString('0.0')) s."
    Write-WbStep "Sortie : $exe"
    return $true
}

function Start-WinBoardApp {
    param(
        [string]$Configuration,
        [string]$ArchLabel
    )
    $exe = Get-WinBoardExePath -Configuration $Configuration -ArchLabel $ArchLabel
    if (-not (Test-Path $exe)) {
        Write-WbFail "Executable introuvable : $exe"
        return 1
    }

    $cwd = Split-Path $exe -Parent
    Write-WbStep "Demarrage de WinBoard..."
    $proc = Start-Process -FilePath $exe -WorkingDirectory $cwd -PassThru
    Start-Sleep -Milliseconds 800

    $running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $running) {
        Write-WbFail "WinBoard s'est arrete immediatement (PID $($proc.Id))."
        Write-Host "        Verifiez Windows App Runtime 2.4 (ARM64) ou lancez l'exe a la main pour voir l'erreur." -ForegroundColor Yellow
        return 1
    }

    Write-WbOk "WinBoard en cours (PID $($proc.Id))."
    Write-Host ""
    Write-Host "  Clavier overlay : bandeau en bas / au-dessus des fenetres." -ForegroundColor White
    Write-Host "  Ouvrez Notepad ou un champ texte pour tester la saisie." -ForegroundColor DarkGray
    Write-Host "  F5 dans Cursor = meme app AVEC debogueur et breakpoints." -ForegroundColor DarkGray
    Write-Host ""
    return 0
}

function Invoke-WbDotNet {
    param(
        [string[]]$DotnetArgs,
        [string]$ProjectRoot = (Get-WbProjectRoot)
    )
    Push-Location $ProjectRoot
    try {
        & dotnet @DotnetArgs 2>&1 | ForEach-Object { Write-Host $_; [Console]::Out.Flush() }
        $exit = $LASTEXITCODE
        if ($null -eq $exit) { $exit = 0 }
        return $exit
    } finally {
        Pop-Location
    }
}

function Publish-WinBoardPortable {
    param([string]$ArchLabel)
    $root = Get-WbProjectRoot
    $csproj = Get-WbCsprojPath
    $platform = Get-WbMsbuildPlatform -ArchLabel $ArchLabel
    $rid = Get-WbRuntimeIdentifier -ArchLabel $ArchLabel
    $outDir = Join-Path $root "publish\$ArchLabel"

    if (Test-Path $outDir) {
        Remove-Item -Path $outDir -Recurse -Force
    }

    $code = Invoke-WbDotNet @(
        "publish", $csproj,
        "-c", "Release",
        "-p:Platform=$platform",
        "-r", $rid,
        "-p:SelfContained=true",
        "-p:WindowsAppSDKSelfContained=true",
        "-p:PublishReadyToRun=false",
        "-o", $outDir
    )
    if ($code -ne 0) { throw "dotnet publish a echoue ($ArchLabel, exit $code)" }
    return $outDir
}

function ConvertTo-MsixVersion {
    param([string]$Version)
    $parts = $Version.Split(".")
    while ($parts.Count -lt 4) { $parts += "0" }
    return ($parts[0..3] -join ".")
}

function Ensure-WinAppCli {
    if (Get-Command winapp -ErrorAction SilentlyContinue) { return }
    Write-Host "Installation de WinApp CLI (winget)..." -ForegroundColor Yellow
    winget install Microsoft.WinAppCli --accept-source-agreements --accept-package-agreements 2>$null
    if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
        throw "winapp introuvable. Installez WinApp CLI puis relancez le terminal."
    }
}

function Find-WixTool {
    param([string]$ToolName)
    $cmd = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        "${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin\$ToolName.exe",
        "${env:ProgramFiles}\WiX Toolset v3.11\bin\$ToolName.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

function New-WinBoardMsiFromFolder {
    param(
        [string]$SourceDir,
        [string]$DestMsi,
        [string]$ProductName,
        [string]$Version
    )
    $heat = Find-WixTool -ToolName "heat"
    $candle = Find-WixTool -ToolName "candle"
    $light = Find-WixTool -ToolName "light"
    if (-not $heat -or -not $candle -or -not $light) {
        return $false
    }

    $wixDir = Join-Path (Get-WbProjectRoot) "packaging\wix\obj"
    New-Item -ItemType Directory -Force -Path $wixDir | Out-Null
    $frag = Join-Path $wixDir "AppFiles.wxs"
    $wxs = Join-Path (Get-WbProjectRoot) "packaging\wix\Product.wxs"
    if (-not (Test-Path $wxs)) { return $false }

    & $heat dir $SourceDir -cg AppHarvest -dr INSTALLFOLDER -gg -sfrag -srd -out $frag
    if ($LASTEXITCODE -ne 0) { throw "heat.exe a echoue" }

    $wixobj = Join-Path $wixDir "Product.wixobj"
    $fragObj = Join-Path $wixDir "AppFiles.wixobj"
    & $candle -nologo -ext WixUIExtension -dSourceDir=$SourceDir -dProductVersion=$Version -dProductName=$ProductName -out $wixobj $wxs
    if ($LASTEXITCODE -ne 0) { throw "candle Product.wxs a echoue" }
    & $candle -nologo -out $fragObj $frag
    if ($LASTEXITCODE -ne 0) { throw "candle fragment a echoue" }

    New-Item -ItemType Directory -Force -Path (Split-Path $DestMsi -Parent) | Out-Null
    & $light -nologo -ext WixUIExtension -out $DestMsi $wixobj $fragObj
    if ($LASTEXITCODE -ne 0) { throw "light.exe a echoue" }
    return $true
}

function Copy-WinBoardPortableZip {
    param(
        [string]$SourceDir,
        [string]$DestZip
    )
    New-Item -ItemType Directory -Force -Path (Split-Path $DestZip -Parent) | Out-Null
    if (Test-Path $DestZip) { Remove-Item $DestZip -Force }
    Compress-Archive -Path (Join-Path $SourceDir "*") -DestinationPath $DestZip -Force
}
