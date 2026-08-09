param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$PublishDir,

    [string]$ManifestPath,

    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

$repoRoot = Get-RepositoryRoot -ScriptRoot $PSScriptRoot
$metadata = Get-AppReleaseMetadata -RepositoryRoot $repoRoot
$publishDir = if ([string]::IsNullOrWhiteSpace($PublishDir)) { Join-Path $repoRoot 'artifacts\publish\win-x64' } else { [IO.Path]::GetFullPath($PublishDir) }
$manifestPath = if ([string]::IsNullOrWhiteSpace($ManifestPath)) { Join-Path $publishDir 'release-manifest.json' } else { [IO.Path]::GetFullPath($ManifestPath) }
$outputDir = if ([string]::IsNullOrWhiteSpace($OutputDir)) { Join-Path $repoRoot 'artifacts\installer' } else { [IO.Path]::GetFullPath($OutputDir) }
$installerProjectDir = Join-Path $repoRoot 'installer\CEI.ReportGenerator.Installer'
$generatedWxs = Join-Path $installerProjectDir 'GeneratedFiles.wxs'
$installerProject = Join-Path $installerProjectDir 'CEI.ReportGenerator.Installer.wixproj'
$iconPath = Join-Path $repoRoot 'src\CEI.ReportGenerator.App\Assets\AppIcon.ico'
$expectedExe = Join-Path $publishDir 'CEI.ReportGenerator.App.exe'
$expectedDll = Join-Path $publishDir 'CEI.ReportGenerator.App.dll'
$expectedTemplate = Join-Path $publishDir 'Templates\CEI_Base_Template_Refined.docx'
$expectedMsi = Join-Path $outputDir $metadata.InstallerFileName
$expectedWixPdb = [IO.Path]::ChangeExtension($expectedMsi, '.wixpdb')

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

foreach ($existingArtifact in @($expectedMsi, $expectedWixPdb)) {
    if (Test-Path -LiteralPath $existingArtifact) {
        Remove-Item -LiteralPath $existingArtifact -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $existingArtifact) {
            throw "Could not remove previous installer artifact: $existingArtifact"
        }
    }
}

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

$publishFiles = Get-ChildItem -LiteralPath $publishDir -File -Recurse | Sort-Object FullName
if ($publishFiles.Count -eq 0) {
    throw "Publish directory is empty: $publishDir"
}

foreach ($requiredPath in @($expectedExe, $expectedDll, $expectedTemplate, $manifestPath, $iconPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required release input not found: $requiredPath"
    }
}

$publishManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$publishManifest.commit)) {
    throw "Publish manifest commit is missing: $manifestPath"
}

if ([string]::IsNullOrWhiteSpace([string]$publishManifest.version)) {
    throw "Publish manifest version is missing: $manifestPath"
}

if ($publishManifest.version -ne $metadata.InformationalVersion) {
    throw "Publish manifest version '$($publishManifest.version)' does not match source version '$($metadata.InformationalVersion)'."
}

if ($publishManifest.fileVersion -ne $metadata.FileVersion) {
    throw "Publish manifest fileVersion '$($publishManifest.fileVersion)' does not match source file version '$($metadata.FileVersion)'."
}

$publishInfo = Get-ExecutableVersionMetadata -Path $expectedExe
Assert-PublishedExecutableMatchesMetadata -ExpectedMetadata $metadata -PublishedExecutableMetadata $publishInfo -Stage 'INSTALLER INPUT VERIFICATION'

$directories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Get-SafeId {
    param(
        [string]$Prefix,
        [string]$Value
    )

    $safe = ($Value -replace '[^A-Za-z0-9_\.]', '_')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = 'Root'
    }

    if ($safe[0] -match '[0-9]') {
        $safe = '_' + $safe
    }

    return "${Prefix}_$safe"
}

function Escape-Xml {
    param([string]$Value)

    return [Security.SecurityElement]::Escape($Value)
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseUri = [Uri]((Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\') + '\')
    $targetUri = [Uri](Resolve-Path -LiteralPath $TargetPath).Path
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Get-DirectoryDepth {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return 0
    }

    return ($Path -split '[\\/]').Count
}

foreach ($file in $publishFiles) {
    $relativeDirectory = [IO.Path]::GetDirectoryName((Get-RelativePath -BasePath $publishDir -TargetPath $file.FullName))
    while (-not [string]::IsNullOrWhiteSpace($relativeDirectory)) {
        [void]$directories.Add($relativeDirectory)
        $relativeDirectory = [IO.Path]::GetDirectoryName($relativeDirectory)
    }
}

$directoryIds = @{}
$directoryIds['.'] = 'INSTALLFOLDER'
foreach ($directory in $directories | Sort-Object { Get-DirectoryDepth $_ }, { $_ }) {
    $directoryIds[$directory] = Get-SafeId -Prefix 'dir' -Value $directory
}

$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')

foreach ($directory in $directories | Sort-Object { Get-DirectoryDepth $_ }, { $_ }) {
    $parentPath = [IO.Path]::GetDirectoryName($directory)
    if ([string]::IsNullOrWhiteSpace($parentPath)) {
        $parentPath = '.'
    }

    $directoryName = Split-Path -Path $directory -Leaf
    $directoryId = $directoryIds[$directory]
    $parentId = $directoryIds[$parentPath]

    [void]$builder.AppendLine('  <Fragment>')
    [void]$builder.AppendLine("    <DirectoryRef Id=""$parentId"">")
    [void]$builder.AppendLine("      <Directory Id=""$directoryId"" Name=""$(Escape-Xml $directoryName)"" />")
    [void]$builder.AppendLine('    </DirectoryRef>')
    [void]$builder.AppendLine('  </Fragment>')
}
[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <ComponentGroup Id="PublishedApplicationFiles">')

$counter = 0
foreach ($file in $publishFiles) {
    $counter++
    $relativePath = Get-RelativePath -BasePath $publishDir -TargetPath $file.FullName
    $relativeDirectory = [IO.Path]::GetDirectoryName($relativePath)
    if ([string]::IsNullOrWhiteSpace($relativeDirectory)) {
        $relativeDirectory = '.'
    }

    $componentId = "cmp_{0:D4}" -f $counter
    $fileId = if ($file.Name -eq 'CEI.ReportGenerator.App.exe') { 'fil_CEI_ReportGenerator_App_exe' } else { "fil_{0:D4}" -f $counter }
    $directoryId = $directoryIds[$relativeDirectory]
    $escapedSource = Escape-Xml $file.FullName
    $escapedName = Escape-Xml $file.Name
    [void]$builder.AppendLine("      <Component Id=""$componentId"" Guid=""*"" Directory=""$directoryId"">")
    [void]$builder.AppendLine("        <File Id=""$fileId"" Source=""$escapedSource"" Name=""$escapedName"" KeyPath=""yes"" />")
    [void]$builder.AppendLine('        </Component>')
}

[void]$builder.AppendLine('    </ComponentGroup>')
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('</Wix>')

[IO.File]::WriteAllText($generatedWxs, $builder.ToString(), [Text.UTF8Encoding]::new($false))

$buildStartedUtc = (Get-Date).ToUniversalTime()
Invoke-NativeCommand -Stage 'INSTALLER' -FilePath 'dotnet' -Arguments @(
    'build',
    $installerProject,
    '--configuration', $Configuration,
    ('/p:PublishDir="{0}"' -f $publishDir),
    ('/p:AppIconPath="{0}"' -f $iconPath),
    ('/p:ReleaseProductName="{0}"' -f $metadata.Product),
    ('/p:ReleaseProductVersion="{0}"' -f $metadata.Version),
    ('/p:ReleaseInformationalVersion="{0}"' -f $metadata.InformationalVersion),
    ('/p:OutputPath="{0}"' -f $outputDir)
) -WorkingDirectory $repoRoot

if (-not (Test-Path -LiteralPath $expectedMsi)) {
    throw "Installer build completed but expected MSI was not created: $expectedMsi"
}

$installerItem = Get-Item -LiteralPath $expectedMsi
if ($installerItem.LastWriteTimeUtc -lt $buildStartedUtc) {
    throw "Installer file was not refreshed during the current run: $expectedMsi"
}

Write-Host ('Installer completed: ' + $expectedMsi)
