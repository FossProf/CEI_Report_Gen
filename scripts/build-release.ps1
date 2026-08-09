param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipTests,

    [switch]$SkipInstaller,

    [switch]$KeepArtifacts,

    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$stage = 'PRE-FLIGHT'
$testsPassed = $false
$publishVerified = $false
$installerVerified = $false
$repoRoot = Get-RepositoryRoot -ScriptRoot $PSScriptRoot
$publishDir = Join-Path $repoRoot 'artifacts\publish\win-x64'
$installerDir = Join-Path $repoRoot 'artifacts\installer'
$installerStagingDir = Join-Path $repoRoot 'artifacts\installer-staging'
$releaseDir = Join-Path $repoRoot 'artifacts\release'
$generatedWxs = Join-Path $repoRoot 'installer\CEI.ReportGenerator.Installer\GeneratedFiles.wxs'
$solution = Join-Path $repoRoot 'CEI_Report_Gen.sln'
$appProject = Join-Path $repoRoot 'src\CEI.ReportGenerator.App\CEI.ReportGenerator.App.csproj'
$smokeTestDll = Join-Path $repoRoot 'src\CEI.ReportGenerator.SmokeTests\bin\Release\net8.0-windows\CEI.ReportGenerator.SmokeTests.dll'

try {
    $metadata = Get-AppReleaseMetadata -RepositoryRoot $repoRoot
    $git = Get-GitReleaseMetadata -RepositoryRoot $repoRoot

    if (-not $AllowDirty -and $git.IsDirty) {
        Write-Host 'Release aborted:'
        Write-Host 'Working tree contains uncommitted changes.'
        $git.ChangedFiles | ForEach-Object { Write-Host ('  ' + $_) }
        throw 'Dirty working tree.'
    }

    $runningApp = Get-Process -Name 'CEI.ReportGenerator.App' -ErrorAction SilentlyContinue
    if ($runningApp) {
        throw 'SPINgen is currently running. Close the application before building a release.'
    }

    Write-Host '================================================'
    Write-Host 'SPINgen Release Build'
    Write-Host '================================================'
    Write-Host ('Version: ' + $metadata.InformationalVersion)
    Write-Host ('Commit: ' + $git.Commit)
    Write-Host ('Configuration: ' + $Configuration)
    Write-Host ''

    $stage = 'CLEAN'
    foreach ($path in @($publishDir, $installerDir, $installerStagingDir, $releaseDir, $generatedWxs)) {
        Remove-PathSafely -Path $path -RepositoryRoot $repoRoot
    }

    Write-Host 'Cleaning solution...'
    Invoke-NativeCommand -Stage $stage -FilePath 'dotnet' -Arguments @('clean', $solution, '--configuration', $Configuration) -WorkingDirectory $repoRoot

    $stage = 'RESTORE'
    Write-Host ''
    Write-Host 'Restoring solution...'
    Invoke-NativeCommand -Stage $stage -FilePath 'dotnet' -Arguments @('restore', $solution, '--runtime', 'win-x64') -WorkingDirectory $repoRoot

    $stage = 'BUILD'
    Write-Host ''
    Write-Host 'Building solution...'
    Invoke-NativeCommand -Stage $stage -FilePath 'dotnet' -Arguments @('build', $solution, '--configuration', $Configuration, '--no-restore') -WorkingDirectory $repoRoot

    if (-not $SkipTests) {
        $stage = 'TESTS'
        Write-Host ''
        Write-Host 'Running smoke tests...'
        Invoke-NativeCommand -Stage $stage -FilePath 'dotnet' -Arguments @($smokeTestDll) -WorkingDirectory $repoRoot
        $testsPassed = $true
    }

    $stage = 'PUBLISH'
    Write-Host ''
    Write-Host 'Publishing verified win-x64 output...'
    Invoke-NativeCommand -Stage $stage -FilePath 'dotnet' -Arguments @(
        'publish',
        $appProject,
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishDir,
        '--no-restore'
    ) -WorkingDirectory $repoRoot

    $stage = 'PUBLISH VERSION VERIFICATION'
    $publishExe = Join-Path $publishDir 'CEI.ReportGenerator.App.exe'
    $publishDll = Join-Path $publishDir 'CEI.ReportGenerator.App.dll'
    $publishCoreDll = Join-Path $publishDir 'CEI.ReportGenerator.Core.dll'
    $publishTemplate = Join-Path $publishDir 'Templates\CEI_Base_Template_Refined.docx'

    foreach ($expectedFile in @($publishExe, $publishDll, $publishCoreDll, $publishTemplate)) {
        if (-not (Test-Path -LiteralPath $expectedFile)) {
            throw "$stage failed. Expected file not found: $expectedFile"
        }
    }

    $publishInfo = Get-ExecutableVersionMetadata -Path $publishExe
    Assert-PublishedExecutableMatchesMetadata -ExpectedMetadata $metadata -PublishedExecutableMetadata $publishInfo -Stage $stage

    $publishManifestPath = Join-Path $publishDir 'release-manifest.json'
    $publishManifest = [ordered]@{
        product = $metadata.Product
        version = $metadata.InformationalVersion
        fileVersion = $metadata.FileVersion
        commit = $git.Commit
        commitShort = $git.CommitShort
        configuration = $Configuration
        runtime = 'win-x64'
        builtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    Write-JsonFile -Path $publishManifestPath -Value $publishManifest
    $publishVerified = $true

    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

    $zipPath = Join-Path $releaseDir $metadata.PortableZipFileName
    Write-Host ''
    Write-Host 'Creating portable ZIP from verified publish output...'
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
    if (-not (Test-Path -LiteralPath $zipPath)) {
        throw 'FAILED: PORTABLE ZIP'
    }

    $installerPath = $null
    if (-not $SkipInstaller) {
        $stage = 'INSTALLER'
        Write-Host ''
        Write-Host 'Building installer from verified publish output...'
        & (Join-Path $PSScriptRoot 'build-installer.ps1') `
            -Configuration $Configuration `
            -PublishDir $publishDir `
            -ManifestPath $publishManifestPath `
            -OutputDir $installerDir
        if ($LASTEXITCODE -ne 0) {
            throw 'Installer script failed.'
        }

        $installerPath = Join-Path $installerDir $metadata.InstallerFileName
        if (-not (Test-Path -LiteralPath $installerPath)) {
            throw "FAILED: INSTALLER`nExpected installer not found: $installerPath"
        }

        $installerVerified = $true
    }

    $stage = 'CREATE RELEASE MANIFEST'
    $zipHashPath = Join-Path $releaseDir ($metadata.PortableZipFileName + '.sha256')
    $zipHash = Write-HashFile -TargetPath $zipPath -HashOutputPath $zipHashPath

    $installerHash = $null
    if ($installerPath) {
        $installerHashPath = Join-Path $releaseDir ($metadata.InstallerFileName + '.sha256')
        $installerHash = Write-HashFile -TargetPath $installerPath -HashOutputPath $installerHashPath
    }

    $releaseManifestPath = Join-Path $releaseDir 'release-manifest.json'
    $releaseManifest = [ordered]@{
        product = $metadata.Product
        version = $metadata.InformationalVersion
        fileVersion = $metadata.FileVersion
        commit = $git.Commit
        commitShort = $git.CommitShort
        configuration = $Configuration
        runtime = 'win-x64'
        publishPath = (Get-RelativeRepositoryPath -RepositoryRoot $repoRoot -Path $publishDir)
        installer = if ($installerPath) { Split-Path -Leaf $installerPath } else { $null }
        portableZip = Split-Path -Leaf $zipPath
        buildUtc = (Get-Date).ToUniversalTime().ToString('o')
        testsPassed = ($SkipTests -or $testsPassed)
        publishVerified = $publishVerified
        installerVerified = ($SkipInstaller -or $installerVerified)
        hashes = [ordered]@{
            installerSha256 = $installerHash
            portableZipSha256 = $zipHash
        }
    }
    Write-JsonFile -Path $releaseManifestPath -Value $releaseManifest

    Write-Host ''
    Write-Host '================================================'
    Write-Host 'SPINgen Release Build Complete'
    Write-Host '================================================'
    Write-Host ('Version: ' + $metadata.InformationalVersion)
    Write-Host ('Commit: ' + $git.Commit)
    Write-Host ('Publish: ' + (Get-RelativeRepositoryPath -RepositoryRoot $repoRoot -Path $publishDir))
    if ($installerPath) {
        Write-Host ('Installer: ' + (Get-RelativeRepositoryPath -RepositoryRoot $repoRoot -Path $installerPath))
    }
    else {
        Write-Host 'Installer: skipped'
    }
    Write-Host ('Portable: ' + (Get-RelativeRepositoryPath -RepositoryRoot $repoRoot -Path $zipPath))
    Write-Host ('Tests: ' + ($(if ($SkipTests) { 'SKIPPED' } else { 'PASS' })))
    Write-Host ('Published version: ' + ($(if ($publishVerified) { 'VERIFIED' } else { 'FAILED' })))
    Write-Host ('Installer input: ' + ($(if ($SkipInstaller) { 'SKIPPED' } elseif ($installerVerified) { 'VERIFIED' } else { 'FAILED' })))
    if ($installerHash) {
        Write-Host ('Installer SHA256: ' + $installerHash)
    }
    Write-Host ('Portable SHA256: ' + $zipHash)
    Write-Host '================================================'
}
catch {
    Write-Host ''
    Write-Host ('FAILED: ' + $stage)
    Write-Host $_.Exception.Message

    if (-not $KeepArtifacts) {
        foreach ($path in @($publishDir, $installerDir, $installerStagingDir, $releaseDir, $generatedWxs)) {
            try {
                Remove-PathSafely -Path $path -RepositoryRoot $repoRoot
            }
            catch {
            }
        }
    }

    throw
}
