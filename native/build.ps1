param([string]$OutputDirectory = "$PSScriptRoot\..\artifacts\native")
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$kernelDirectory = Join-Path $PSScriptRoot 'kernels'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio 2022 C++ build tools and Windows SDK.' }
$tool = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$tool) { throw 'The Visual C++ x64 toolchain was not found.' }
$version = (Get-Content -LiteralPath (Join-Path $tool 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt')).Trim()
$vc = Join-Path $tool "VC\Tools\MSVC\$version"
$sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdkVersion = Get-ChildItem -LiteralPath "$sdk\Include" -Directory |
    Where-Object { Test-Path -LiteralPath "$($_.FullName)\ucrt\stdio.h" } | Sort-Object Name -Descending | Select-Object -First 1
if (!$sdkVersion) { throw 'Windows SDK headers were not found.' }
$sources = @(Get-ChildItem -LiteralPath $kernelDirectory -Filter '*.c' | ForEach-Object FullName)
$sources += "$PSScriptRoot\bridge.c"
$headers = @(Get-ChildItem -LiteralPath $kernelDirectory -Filter '*.h' | ForEach-Object FullName)
$output = Join-Path $OutputDirectory 'Compositor.Native.dll'
$inputs = @($sources) + @($headers) + @($PSCommandPath)
if ((Test-Path -LiteralPath $output) -and !(Get-Item -LiteralPath $inputs | Where-Object LastWriteTimeUtc -gt (Get-Item -LiteralPath $output).LastWriteTimeUtc)) { exit 0 }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$arguments = @('/nologo','/LD','/O2','/MT','/TC','/std:c17','/utf-8','/D_USE_MATH_DEFINES',
    "/I$vc\include", "/I$sdk\Include\$($sdkVersion.Name)\ucrt", "/I$kernelDirectory",
    "/Fo$OutputDirectory\", "/Fe$output") + $sources + @('/link',
    "/LIBPATH:$vc\lib\x64", "/LIBPATH:$sdk\Lib\$($sdkVersion.Name)\ucrt\x64",
    "/LIBPATH:$sdk\Lib\$($sdkVersion.Name)\um\x64", "/IMPLIB:$OutputDirectory\Compositor.Native.lib")
& "$vc\bin\Hostx64\x64\cl.exe" @arguments
if ($LASTEXITCODE -ne 0) { throw "Native compilation failed ($LASTEXITCODE)." }
