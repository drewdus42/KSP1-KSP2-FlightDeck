#requires -Version 5.1
<#
    Builds and verifies the KSP 1 Flight Deck mod.

      .\build.ps1
      .\build.ps1 -KspDir "D:\Steam\steamapps\common\Kerbal Space Program"
      .\build.ps1 -SkipTests

    Output: dist\GameData\KSPFlightDeck\  (KSPFlightDeck.dll + KSPFlightDeck.version + settings.cfg)
#>
param(
    [string]$KspDir = "",
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
    $buildArgs = @("build", "src\KSPFlightDeck\KSPFlightDeck.csproj", "-c", "Release", "-v", "m")
    if ($KspDir -ne "") {
        $buildArgs += "-p:KspDir=$KspDir"
    }
    Invoke-Step "Building KSP 1 mod" { dotnet @buildArgs }

    if (-not $SkipTests) {
        Invoke-Step "Running mod unit tests" { dotnet test "tests\FlightDeck.Core.Tests\FlightDeck.Core.Tests.csproj" -c Release --nologo }

        $smokeArgs = @("build", "tests\FlightDeck.Smoke\FlightDeck.Smoke.csproj", "-c", "Release", "-v", "q", "--nologo")
        if ($KspDir -ne "") {
            $smokeArgs += "-p:KspDir=$KspDir"
        }
        Invoke-Step "Building mod smoke test" { dotnet @smokeArgs }

        Invoke-Step "Building dev host" {
            dotnet build "tests\FlightDeck.DevHost\FlightDeck.DevHost.csproj" -c Release -v q --nologo
        }

        Invoke-Step "Running mod smoke test (loads the DLL next to KSP assemblies)" {
            & (Join-Path $root "tests\FlightDeck.Smoke\bin\Release\net472\FlightDeck.Smoke.exe")
        }
    }
}
finally {
    Pop-Location
}

if (-not $SkipPackage) {
    Invoke-Step "Packaging KSP 1 mod zip" {
        $zip = Join-Path $dist "KSPFlightDeck-1.0.1.zip"
        if (Test-Path $zip) {
            Remove-Item $zip -Force
        }
        Compress-Archive -Path (Join-Path $dist "GameData") -DestinationPath $zip
        Write-Host "Created $zip"
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Mod: $dist\GameData\KSPFlightDeck"
Write-Host "  Install with: .\install.ps1"
