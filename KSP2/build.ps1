#requires -Version 5.1
<#
    Builds and verifies the KSP 2 Flight Deck mod.

      .\build.ps1
      .\build.ps1 -Ksp2Dir "D:\Steam\steamapps\common\Kerbal Space Program 2"
      .\build.ps1 -SkipTests

    Output: dist\mods\KSPFlightDeck2\  (swinfo.json + KSPFlightDeck2.dll)
#>
param(
    [string]$Ksp2Dir = "",
    [switch]$SkipTests,
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"

function Invoke-Step {
    param([string]$Name, [scriptblock]$Block)
    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Block
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

Push-Location $root
try {
    $buildArgs = @("build", "src\KSP2FlightDeck\KSP2FlightDeck.csproj", "-c", "Release", "-v", "m")
    if ($Ksp2Dir -ne "") {
        $buildArgs += "-p:Ksp2Dir=$Ksp2Dir"
    }
    Invoke-Step "Building KSP 2 mod" { dotnet @buildArgs }

    if (-not $SkipTests) {
        Invoke-Step "Running protocol tests" { dotnet test "tests\FlightDeck.Core.Tests\FlightDeck.Core.Tests.csproj" -c Release --nologo }

        Invoke-Step "Building dev host" { dotnet build "tests\FlightDeck.DevHost\FlightDeck.DevHost.csproj" -c Release -v q --nologo }
    }
}
finally {
    Pop-Location
}

if (-not $SkipPackage) {
    Invoke-Step "Packaging KSP 2 mod zip" {
        $zip = Join-Path $dist "KSPFlightDeck2-1.0.0.zip"
        if (Test-Path $zip) {
            Remove-Item $zip -Force
        }

        Compress-Archive -Path (Join-Path $dist "mods\KSPFlightDeck2") -DestinationPath $zip
        Write-Host "Created $zip"
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Mod: $dist\mods\KSPFlightDeck2"
Write-Host "  Install with: .\install.ps1"
