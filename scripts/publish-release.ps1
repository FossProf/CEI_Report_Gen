$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repoRoot 'CEI_Report_Gen.sln'
$smokeTestDll = Join-Path $repoRoot 'src\CEI.ReportGenerator.SmokeTests\bin\Release\net8.0-windows\CEI.ReportGenerator.SmokeTests.dll'
$publishProject = Join-Path $repoRoot 'src\CEI.ReportGenerator.App\CEI.ReportGenerator.App.csproj'
$publishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'

Write-Host 'Restoring solution...'
dotnet restore $solution

Write-Host 'Building Release...'
dotnet build $solution --configuration Release

Write-Host 'Running smoke tests...'
dotnet $smokeTestDll

Write-Host 'Publishing self-contained win-x64 release...'
dotnet publish $publishProject `
  --configuration Release `
  --property:PublishProfile=win-x64

Write-Host ''
Write-Host ('Publish completed: ' + $publishDir)
