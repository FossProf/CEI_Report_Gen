param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipTests,

    [switch]$KeepArtifacts,

    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'build-release.ps1') `
    -Configuration $Configuration `
    -SkipTests:$SkipTests `
    -SkipInstaller `
    -KeepArtifacts:$KeepArtifacts `
    -AllowDirty:$AllowDirty
