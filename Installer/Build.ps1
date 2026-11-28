param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler is unavailable.'
}

Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$temporary = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary 'ZhCnPayload.zip'
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($package in @(
            @{ Id = 'Afterburner'; Root = $repo; Skin = 'MSIWin11Dark-zh-CN.usf' }
            @{ Id = 'RTSS'; Root = (Join-Path $repo 'RTSS'); Skin = 'Default4Chinese.usf' }
        )) {
            $language = Join-Path $package.Root 'Localization\CHN'
            $skin = Join-Path $package.Root ('Skins\' + $package.Skin)
            if (-not (Test-Path -LiteralPath (Join-Path $language 'Description') -PathType Leaf) -or
                -not (Test-Path -LiteralPath $skin -PathType Leaf)) {
                throw "Incomplete package: $($package.Id)"
            }

            $files = @(Get-ChildItem -LiteralPath $language -File -Recurse)
            $files += Get-Item -LiteralPath $skin
            foreach ($source in ($files | Sort-Object -Property FullName)) {
                $relative = $source.FullName.Substring($package.Root.Length + 1).Replace('\', '/')
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, $source.FullName, ($package.Id + '/' + $relative),
                    [IO.Compression.CompressionLevel]::Optimal)
            }
        }
    } finally {
        $zip.Dispose()
    }

    $output = New-Item -Path $OutputDirectory -ItemType Directory -Force
    $exe = Join-Path $output.FullName 'Afterburner-RTSS-zh-CN-Setup.exe'
    $options = @(
        '/nologo', '/langversion:5', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001'
        '/reference:System.IO.Compression.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll'
        "/win32manifest:$PSScriptRoot\app.manifest", "/resource:$archive,ZhCnPayload.zip", "/out:$exe"
        "$PSScriptRoot\Program.cs", "$PSScriptRoot\Installation.cs"
    )
    & $compiler @options
    if ($LASTEXITCODE -ne 0) { throw "Compiler exited with code $LASTEXITCODE" }
    Get-Item -LiteralPath $exe
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
