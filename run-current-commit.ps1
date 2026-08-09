param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    [switch]$Wait
)

# If Windows blocks local script execution, you can either run:
#   Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
# or a one-time invocation:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-current-commit.ps1
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'CEI_Report_Gen.sln'
$appExe = Join-Path $repoRoot ("src\CEI.ReportGenerator.App\bin\{0}\net8.0-windows\CEI.ReportGenerator.App.exe" -f $Configuration)

$commit = $null
try
{
    $commitText = git -C $repoRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commitText))
    {
        $commit = $commitText.Trim()
    }
}
catch
{
    $commit = $null
}

Write-Host 'CEI Report Generator runner'
Write-Host ('Repository: ' + $repoRoot)
if ($commit)
{
    Write-Host ('Commit: ' + $commit)
}

if (-not $NoBuild)
{
    Write-Host ''
    Write-Host 'Restoring solution...'
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    Write-Host ''
    Write-Host ('Building ' + $Configuration + '...')
    dotnet build $solution --configuration $Configuration
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $appExe))
{
    throw ('App executable not found: ' + $appExe)
}

Write-Host ''
Write-Host ('Launching ' + $appExe)

if ($Wait)
{
    & $appExe
}
else
{
    Start-Process -FilePath $appExe -WorkingDirectory $repoRoot | Out-Null
}
