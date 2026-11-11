#requires -Version 5.1
<#
    Builds and verifies every half of the project:

      KSP1\   the KSP 1 mod (compiles against your KSP install, unit + smoke tests)
      KSP2\   the KSP 2 mod (compiles against your KSP 2 install, protocol tests)
      plugin\ the Stream Deck plugin (node tests, packaged .streamDeckPlugin)

    Examples:
      .\build.ps1
      .\build.ps1 -KspDir "D:\Steam\steamapps\common\Kerbal Space Program" -Ksp2Dir "D:\Steam\steamapps\common\Kerbal Space Program 2"
      .\build.ps1 -SkipTests
      .\build.ps1 -Only KSP1
#>
param(
    [string]$KspDir = "",
    [string]$Ksp2Dir = "",
    [ValidateSet("All", "KSP1", "KSP2", "Plugin")]
    [string]$Only = "All",
    [switch]$SkipTests,
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ksp1Root = Join-Path $root "KSP1"
$ksp2Root = Join-Path $root "KSP2"
$pluginRoot = Join-Path $root "plugin"
$pluginSource = Join-Path $pluginRoot "src\com.drewdus.kspflightcontrol.sdPlugin"

function Invoke-Step {
    param([string]$Name, [scriptblock]$Block)
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Block
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

if ($Only -eq "All" -or $Only -eq "KSP1") {
    Invoke-Step "Building KSP 1 mod" {
        $arguments = @{}
        if ($KspDir -ne "") { $arguments["KspDir"] = $KspDir }
        if ($SkipTests) { $arguments["SkipTests"] = $true }
        if ($SkipPackage) { $arguments["SkipPackage"] = $true }
        & (Join-Path $ksp1Root "build.ps1") @arguments
    }
}

if ($Only -eq "All" -or $Only -eq "KSP2") {
    Invoke-Step "Building KSP 2 mod" {
        $arguments = @{}
        if ($Ksp2Dir -ne "") { $arguments["Ksp2Dir"] = $Ksp2Dir }
        if ($SkipTests) { $arguments["SkipTests"] = $true }
        if ($SkipPackage) { $arguments["SkipPackage"] = $true }
        & (Join-Path $ksp2Root "build.ps1") @arguments
    }
}

if ($Only -eq "All" -or $Only -eq "Plugin") {
    Push-Location $pluginRoot
    try {
        if (-not $SkipTests) {
            Invoke-Step "Running plugin tests" {
                node --test test\util.test.js test\snapshot.test.js test\state.test.js test\manifest.test.js test\art.test.js
            }
        }

        if (-not $SkipPackage) {
            Invoke-Step "Packaging Stream Deck plugin" {
                $package = Join-Path $pluginRoot "Release\com.drewdus.kspflightcontrol.streamDeckPlugin"
                if (Test-Path $package) {
                    Remove-Item $package -Force
                }
                & (Join-Path $pluginRoot "DistributionTool.exe") -b -i $pluginSource -o (Join-Path $pluginRoot "Release")
            }
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
if ($Only -eq "All" -or $Only -eq "KSP1") {
    Write-Host "  KSP 1:  $ksp1Root\dist\GameData\KSPFlightDeck"
}
if ($Only -eq "All" -or $Only -eq "KSP2") {
    Write-Host "  KSP 2:  $ksp2Root\dist\mods\KSPFlightDeck2"
}
if ($Only -eq "All" -or $Only -eq "Plugin") {
    Write-Host "  Plugin: $pluginRoot\Release\com.drewdus.kspflightcontrol.streamDeckPlugin"
}
