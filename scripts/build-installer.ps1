$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\publish\win-x64'))
$installerProjectDir = Join-Path $repoRoot 'installer\CEI.ReportGenerator.Installer'
$generatedWxs = Join-Path $installerProjectDir 'GeneratedFiles.wxs'
$installerProject = Join-Path $installerProjectDir 'CEI.ReportGenerator.Installer.wixproj'
$iconPath = Join-Path $repoRoot 'src\CEI.ReportGenerator.App\Assets\AppIcon.ico'
$outputDir = Join-Path $repoRoot 'artifacts\installer'

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$directories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$files = Get-ChildItem -LiteralPath $publishDir -File -Recurse | Sort-Object FullName

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

foreach ($file in $files) {
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
foreach ($file in $files) {
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
    [void]$builder.AppendLine("        </Component>")
}

[void]$builder.AppendLine('    </ComponentGroup>')
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('</Wix>')

Set-Content -LiteralPath $generatedWxs -Value $builder.ToString() -Encoding UTF8

dotnet build $installerProject `
  --configuration Release `
  /p:PublishDir=$publishDir `
  /p:AppIconPath=$iconPath `
  /p:OutputPath=$outputDir

if ($LASTEXITCODE -ne 0) {
    throw 'MSI build failed.'
}

Write-Host ('Installer completed: ' + (Join-Path $outputDir 'SPINgen_0.3.0-alpha_x64.msi'))
