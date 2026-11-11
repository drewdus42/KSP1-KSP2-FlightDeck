#requires -Version 5.1
<#
    Installs the built artifacts onto this machine.

      .\install.ps1                  # both mods
      .\install.ps1 -Plugin          # both mods + the Stream Deck plugin
      .\install.ps1 -Only KSP2       # just the KSP 2 mod
      .\install.ps1 -KspDir "D:\...\Kerbal Space Program" -Ksp2Dir "D:\...\Kerbal Space Program 2"

    Build first: .\build.ps1
    Nothing here touches the games' own files beyond copying the mod folders.
#>
param(
    [string]$KspDir = "",
    [string]$Ksp2Dir = "",
    [ValidateSet("All", "KSP1", "KSP2", "Plugin")]
    [string]$Only = "All",
    [switch]$Plugin
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginPackage = Join-Path $root "plugin\Release\com.drewdus.kspflightcontrol.streamDeckPlugin"
$pluginFolderName = "com.drewdus.kspflightcontrol.sdPlugin"

$wantsPlugin = $Plugin -or $Only -eq "Plugin"

if ($Only -eq "All" -or $Only -eq "KSP1") {
    $arguments = @{}
    if ($KspDir -ne "") { $arguments["KspDir"] = $KspDir }
    & (Join-Path $root "KSP1\install.ps1") @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($Only -eq "All" -or $Only -eq "KSP2") {
    $arguments = @{}
    if ($Ksp2Dir -ne "") { $arguments["Ksp2Dir"] = $Ksp2Dir }
    & (Join-Path $root "KSP2\install.ps1") @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($wantsPlugin) {
    if (-not (Test-Path $pluginPackage)) {
        throw "No plugin package at $pluginPackage. Run .\build.ps1 first."
    }

    $pluginsRoot = Join-Path $env:APPDATA "Elgato\StreamDeck\Plugins"
    if (-not (Test-Path $pluginsRoot)) {
        New-Item -ItemType Directory -Force -Path $pluginsRoot | Out-Null
    }

    $target = Join-Path $pluginsRoot $pluginFolderName
    $temp = Join-Path $env:TEMP ("sd-install-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temp | Out-Null

    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($pluginPackage, $temp)
        $extracted = Join-Path $temp $pluginFolderName
        if (-not (Test-Path $extracted)) {
            throw "Unexpected package layout: $pluginPackage"
        }

        # Older builds used a different author namespace; remove them so Stream Deck does not load two copies.
        $legacyNames = @("com.novistrum.kspflightcontrol.sdPlugin", "com.novistrum.ksp2flightcontrol.sdPlugin")
        foreach ($legacyName in $legacyNames) {
            $legacy = Join-Path $pluginsRoot $legacyName
            if (Test-Path $legacy) {
                Remove-Item $legacy -Recurse -Force
                Write-Host "Removed previous plugin -> $legacy" -ForegroundColor DarkGray
            }
        }

        if (Test-Path $target) {
            Remove-Item $target -Recurse -Force
        }
        Copy-Item $extracted $target -Recurse
        Write-Host "Plugin installed -> $target" -ForegroundColor Green
        Write-Host "Restart the Stream Deck app so it loads the new build." -ForegroundColor Yellow
    }
    finally {
        if (Test-Path $temp) {
            Remove-Item $temp -Recurse -Force
        }
    }
}

Write-Host "Done." -ForegroundColor Green
