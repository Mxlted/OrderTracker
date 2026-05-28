param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [switch]$SelfContained,

    [switch]$Installer
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "OrderTracker.Desktop\OrderTracker.Desktop.csproj"
$output = Join-Path $root "build"
$installerScript = Join-Path $root "installer\OrderTracker.iss"
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $selfContainedValue `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Build written to $output"

if ($Installer) {
    if (-not (Test-Path -LiteralPath $installerScript)) {
        throw "Installer script was not found at $installerScript"
    }

    $iscc = (Get-Command "iscc.exe" -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @(
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    }

    if (-not $iscc) {
        throw "Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or add it to PATH."
    }

    & $iscc $installerScript

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed with exit code $LASTEXITCODE"
    }

    Write-Host ("Installer written to {0}" -f (Join-Path $root "installer-output\OrderTrackerSetup.exe"))
}
