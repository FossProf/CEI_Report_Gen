function Get-RepositoryRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptRoot
    )

    return [IO.Path]::GetFullPath((Join-Path $ScriptRoot '..'))
}

function Get-AppReleaseMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $projectPath = Join-Path $RepositoryRoot 'src\CEI.ReportGenerator.App\CEI.ReportGenerator.App.csproj'
    [xml]$projectXml = Get-Content -LiteralPath $projectPath
    $propertyGroup = $projectXml.Project.PropertyGroup |
        Where-Object { $_.Product -or $_.Version -or $_.FileVersion -or $_.InformationalVersion } |
        Select-Object -First 1

    if ($null -eq $propertyGroup) {
        throw "Could not read release metadata from $projectPath"
    }

    $product = [string]$propertyGroup.Product
    $version = [string]$propertyGroup.Version
    $fileVersion = [string]$propertyGroup.FileVersion
    $informationalVersion = [string]$propertyGroup.InformationalVersion

    foreach ($pair in @(
        @{ Name = 'Product'; Value = $product },
        @{ Name = 'Version'; Value = $version },
        @{ Name = 'FileVersion'; Value = $fileVersion },
        @{ Name = 'InformationalVersion'; Value = $informationalVersion }
    )) {
        if ([string]::IsNullOrWhiteSpace($pair.Value)) {
            throw "Missing $($pair.Name) in $projectPath"
        }
    }

    return [pscustomobject]@{
        ProjectPath = $projectPath
        Product = $product
        Version = $version
        FileVersion = $fileVersion
        InformationalVersion = $informationalVersion
        InstallerFileName = ('{0}_{1}_x64.msi' -f $product, $informationalVersion)
        PortableZipFileName = ('{0}_{1}_win-x64.zip' -f $product, $informationalVersion)
    }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @(),

        [string]$WorkingDirectory
    )

    $locationPushed = $false
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Push-Location -LiteralPath $WorkingDirectory
            $locationPushed = $true
        }

        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($locationPushed) {
            Pop-Location
        }
    }

    if ($exitCode -ne 0) {
        throw "$Stage failed with exit code $exitCode."
    }
}

function Invoke-NativeCommandCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @(),

        [string]$WorkingDirectory
    )

    $locationPushed = $false
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Push-Location -LiteralPath $WorkingDirectory
            $locationPushed = $true
        }

        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($locationPushed) {
            Pop-Location
        }
    }

    if ($exitCode -ne 0) {
        $text = ($output | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            throw "$Stage failed with exit code $exitCode."
        }

        throw "$Stage failed with exit code $exitCode.`n$text"
    }

    return $output
}

function Get-GitReleaseMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $fullCommit = ((Invoke-NativeCommandCapture -Stage 'GIT' -FilePath 'git' -Arguments @('-C', $RepositoryRoot, 'rev-parse', 'HEAD')) | Select-Object -First 1).ToString().Trim()
    $shortCommit = ((Invoke-NativeCommandCapture -Stage 'GIT' -FilePath 'git' -Arguments @('-C', $RepositoryRoot, 'rev-parse', '--short', 'HEAD')) | Select-Object -First 1).ToString().Trim()
    $statusLines = Invoke-NativeCommandCapture -Stage 'GIT' -FilePath 'git' -Arguments @('-C', $RepositoryRoot, 'status', '--porcelain')
    $changedFiles = @($statusLines | ForEach-Object { $_.ToString().TrimEnd() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    return [pscustomobject]@{
        Commit = $fullCommit
        CommitShort = $shortCommit
        ChangedFiles = $changedFiles
        IsDirty = $changedFiles.Count -gt 0
    }
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRoot = [IO.Path]::GetFullPath($RepositoryRoot)

    if (-not $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        -not $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path outside repository root: $fullPath"
    }

    return $fullPath
}

function Remove-PathSafely {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $safePath = Assert-PathUnderRoot -Path $Path -RepositoryRoot $RepositoryRoot
    if (-not (Test-Path -LiteralPath $safePath)) {
        return
    }

    Remove-Item -LiteralPath $safePath -Recurse -Force -ErrorAction Stop
    if (Test-Path -LiteralPath $safePath) {
        throw "Could not remove path: $safePath"
    }
}

function Get-RelativeRepositoryPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath($Path)
    $rootUri = [Uri]$root
    $targetUri = [Uri]$target
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Get-ExecutableVersionMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Expected file not found: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    $versionInfo = $item.VersionInfo
    return [pscustomobject]@{
        Path = $item.FullName
        FileVersion = [string]$versionInfo.FileVersion
        ProductVersion = [string]$versionInfo.ProductVersion
        ProductName = [string]$versionInfo.ProductName
        LastWriteTimeUtc = $item.LastWriteTimeUtc
    }
}

function Assert-PublishedExecutableMatchesMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$ExpectedMetadata,

        [Parameter(Mandatory = $true)]
        [psobject]$PublishedExecutableMetadata,

        [Parameter(Mandatory = $true)]
        [string]$Stage
    )

    if ($PublishedExecutableMetadata.ProductName -and $PublishedExecutableMetadata.ProductName -ne $ExpectedMetadata.Product) {
        throw "$Stage failed.`nExpected product '$($ExpectedMetadata.Product)' but found '$($PublishedExecutableMetadata.ProductName)'."
    }

    if ($PublishedExecutableMetadata.FileVersion -ne $ExpectedMetadata.FileVersion) {
        throw "$Stage failed.`nSource file version: $($ExpectedMetadata.FileVersion)`nPublished executable version: $($PublishedExecutableMetadata.FileVersion)"
    }

    if (-not $PublishedExecutableMetadata.ProductVersion.StartsWith($ExpectedMetadata.InformationalVersion, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Stage failed.`nSource version: $($ExpectedMetadata.InformationalVersion)`nPublished executable version: $($PublishedExecutableMetadata.ProductVersion)"
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $json = $Value | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Write-HashFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [string]$HashOutputPath
    )

    $hash = Get-FileHash -LiteralPath $TargetPath -Algorithm SHA256
    $line = '{0} *{1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $TargetPath)
    [IO.File]::WriteAllText($HashOutputPath, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    return $hash.Hash.ToLowerInvariant()
}
