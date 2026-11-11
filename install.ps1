#requires -Version 5.1
<#
    Installs the built KSP 1 mod into the game's GameData folder.

      .\install.ps1
      .\install.ps1 -KspDir "D:\Steam\steamapps\common\Kerbal Space Program"

    Build first: .\build.ps1
    Nothing here touches KSP's own files beyond copying GameData\KSPFlightDeck.
#>
param(
    [string]$KspDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$modSource = Join-Path $root "dist\GameData\KSPFlightDeck"

if ($KspDir -eq "") {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program",
        "C:\Program Files\Steam\steamapps\common\Kerbal Space Program",
        "D:\SteamLibrary\steamapps\common\Kerbal Space Program",
        "E:\SteamLibrary\steamapps\common\Kerbal Space Program"
    )
    $KspDir = $candidates | Where-Object { Test-Path (Join-Path $_ "KSP_x64.exe") } | Select-Object -First 1
}

if (-not $KspDir -or -not (Test-Path (Join-Path $KspDir "KSP_x64.exe"))) {
    throw "Could not find a KSP install. Pass -KspDir `"<path to Kerbal Space Program>`"."
}

if (-not (Test-Path $modSource)) {
    throw "No build output at $modSource. Run .\build.ps1 first."
}

$gameData = Join-Path $KspDir "GameData\KSPFlightDeck"
$failed = $false

if (Get-Process -Name KSP_x64 -ErrorAction SilentlyContinue) {
    Write-Host "KSP is running: the mod DLLs are locked until you quit the game." -ForegroundColor Yellow
}

# Keep the user's port/options and the log folder across upgrades.
$preserved = @()
if (Test-Path $gameData) {
    foreach ($name in @("settings.cfg", "PluginData")) {
        $path = Join-Path $gameData $name
        if (Test-Path $path) {
            $backup = Join-Path $env:TEMP ("flightdeck-keep-" + $name.Replace(".", "_") + "-" + [guid]::NewGuid().ToString("N"))
            Copy-Item $path $backup -Recurse
            $preserved += [pscustomobject]@{ Name = $name; Backup = $backup }
        }
    }
}

try {
    if (Test-Path $gameData) {
        Remove-Item $gameData -Recurse -Force
    }
    Copy-Item $modSource $gameData -Recurse

    foreach ($item in $preserved) {
        $destination = Join-Path $gameData $item.Name
        if (Test-Path $destination) {
            Remove-Item $destination -Recurse -Force
        }
        Copy-Item $item.Backup $destination -Recurse
        Remove-Item $item.Backup -Recurse -Force
    }

    Write-Host "KSP 1 mod installed -> $gameData" -ForegroundColor Green
    if ($preserved.Count -gt 0) {
        Write-Host ("Kept existing: " + (($preserved | ForEach-Object { $_.Name }) -join ", "))
    }
}
catch {
    $failed = $true
    Write-Host "Mod NOT installed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Quit KSP and run this script again." -ForegroundColor Yellow
    foreach ($item in $preserved) {
        if (Test-Path $item.Backup) {
            Remove-Item $item.Backup -Recurse -Force
        }
    }
}

if ($failed) {
    exit 1
}

Write-Host "Default API port: 8080 (change it in settings.cfg)." -ForegroundColor Green
