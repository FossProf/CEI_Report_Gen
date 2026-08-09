$ErrorActionPreference = 'Stop'

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [switch]$Wait
)

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'CEI_Report_Gen.sln'
$appProject = Join-Path $repoRoot 'src\CEI.ReportGenerator.App\CEI.ReportGenerator.App.csproj'
$appExe = Join-Path $repoRoot ("src\CEI.ReportGenerator.App\bin\{0}\net8.0-windows\CEI.ReportGenerator.App.exe" -f $Configuration)

$commit = $null
try
{
    $commit = (git -C $repoRoot rev-parse --short HEAD).Trim()
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

    Write-Host ''
    Write-Host ('Building ' + $Configuration + '...')
    dotnet build $solution --configuration $Configuration
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
