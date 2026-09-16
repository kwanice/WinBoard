# Release : compile + lance WinBoard (test local).
param(
    [ValidateSet("arm64", "x64", "")]
    [string]$Arch = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "winboard-release-lib.ps1")
Initialize-WbConsoleUtf8

$Arch = if ([string]::IsNullOrWhiteSpace($Arch)) { Get-WbHostArchLabel } else { $Arch.ToLower() }
$platform = Get-WbMsbuildPlatform -ArchLabel $Arch

Write-WbBanner "WinBoard - Release [2.Rel] - $platform"
Stop-WinBoardProcess

if (-not (Build-WinBoardProject -Configuration "Release" -ArchLabel $Arch)) {
    exit 1
}

exit (Start-WinBoardApp -Configuration "Release" -ArchLabel $Arch)
