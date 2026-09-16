# Debug : compile (sortie visible) + lance WinBoard.
param(
    [ValidateSet("arm64", "x64", "")]
    [string]$Arch = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "winboard-release-lib.ps1")
Initialize-WbConsoleUtf8

$Arch = if ([string]::IsNullOrWhiteSpace($Arch)) { Get-WbHostArchLabel } else { $Arch.ToLower() }
$platform = Get-WbMsbuildPlatform -ArchLabel $Arch

Write-WbBanner "WinBoard - Debug [1.Dbg] - $platform"
Stop-WinBoardProcess

if (-not (Build-WinBoardProject -Configuration "Debug" -ArchLabel $Arch)) {
    exit 1
}

exit (Start-WinBoardApp -Configuration "Debug" -ArchLabel $Arch)
