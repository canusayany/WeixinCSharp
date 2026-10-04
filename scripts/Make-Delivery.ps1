param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDirectory = Join-Path $workspacePath 'artifacts'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
Push-Location -LiteralPath $workspacePath
try {
    if (-not $SkipBuild) {
        & (Join-Path $PSScriptRoot 'Test-All.ps1')
    }
    if (-not (Test-Path -LiteralPath (Join-Path $outputDirectory 'win-x64\weixin.exe'))) { throw 'Published EXE missing.' }
    if (-not (Test-Path -LiteralPath 'docs\REPORT.html')) { throw 'Rendered report missing; run python scripts/render_report.py first.' }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceZip = Join-Path $outputDirectory 'WeixinCSharp-1.2.1-source.zip'
    $runtimeZip = Join-Path $outputDirectory 'WeixinCSharp-1.2.1-win-x64.zip'
    $explicitRootFiles = @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','Directory.Build.props','global.json','WeixinAssistant.slnx','.gitignore','.gitattributes')
    $sourceFiles = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($fileName in $explicitRootFiles) { $sourceFiles.Add((Get-Item -LiteralPath (Join-Path $workspacePath $fileName))) }
    foreach ($directoryName in @('src','tests','docs','scripts','research\upstream-snapshot','research\typing-lifecycle-snapshot','runtime\voice','research\runtime-dependencies')) {
        Get-ChildItem -LiteralPath (Join-Path $workspacePath $directoryName) -File -Recurse | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj|__pycache__)[\\/]' -and $_.Name -notmatch '\.dpapi|(^|\.)login-qr\.html$|\.tmp$|\.pyc$'
        } | ForEach-Object { $sourceFiles.Add($_) }
    }
    foreach ($fileName in @('research\official-evidence.md','research\github-evidence.md','research\provenance.json')) {
        $sourceFiles.Add((Get-Item -LiteralPath (Join-Path $workspacePath $fileName)))
    }
    function Write-Zip($zipPath, $files, $baseDirectory) {
        $fileStream = [IO.File]::Open($zipPath, [IO.FileMode]::Create)
        $archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($fileInfo in $files) {
                $entryName = $fileInfo.FullName.Substring($baseDirectory.Length + 1).Replace('\','/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $fileInfo.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose(); $fileStream.Dispose() }
    }
    Write-Zip $sourceZip $sourceFiles $workspacePath
    $publishedPath = Join-Path $outputDirectory 'win-x64'
    Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md,LICENSE -Destination $publishedPath -Force
    $docsDirectory = Join-Path $publishedPath 'docs'
    [IO.Directory]::CreateDirectory($docsDirectory) | Out-Null
    $sourceDocsPath = Join-Path $workspacePath 'docs'
    # Keep the report's local evidence links usable in the runtime archive too.
    Get-ChildItem -LiteralPath $sourceDocsPath -File -Recurse | ForEach-Object {
        $relativeDocPath = $_.FullName.Substring($sourceDocsPath.Length + 1)
        $destinationDocPath = Join-Path $docsDirectory $relativeDocPath
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationDocPath)) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destinationDocPath -Force
    }
    Write-Zip $runtimeZip (Get-ChildItem -LiteralPath $publishedPath -File -Recurse) $publishedPath
    $sums = @($sourceZip,$runtimeZip) | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
        $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_)
    }
    $sums | Set-Content -LiteralPath (Join-Path $outputDirectory 'SHA256SUMS.txt') -Encoding ASCII
    Get-Item -LiteralPath $sourceZip,$runtimeZip | Select-Object Name,Length
} finally { Pop-Location }
