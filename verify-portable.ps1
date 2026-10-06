param([ValidateRange(1, 20)][int]$Runs = 3)
$ErrorActionPreference = 'Stop'
$report = Join-Path $PSScriptRoot 'artifacts/portable-validation.json'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$source = Join-Path $PSScriptRoot 'artifacts/publish'
if (!(Test-Path -LiteralPath (Join-Path $source 'Compositor.Windows.exe'))) { throw 'Run build.ps1 -Publish first.' }
$root = Join-Path $PSScriptRoot ('artifacts/portable check ' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$package = Join-Path $root 'app'
Copy-Item -LiteralPath $source -Destination $package -Recurse
$runtime = Get-Content -LiteralPath (Join-Path $package 'Compositor.Windows.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework) { throw 'Package is not self-contained.' }
$records = @()
for ($run = 1; $run -le $Runs; $run++) {
    $shot = Join-Path $root "smoke-$run.png"
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $package 'Compositor.Windows.exe'
    $info.Arguments = '--smoke "' + $shot + '"'
    $info.WorkingDirectory = $root
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.EnvironmentVariables['PATH'] = (Join-Path $env:SystemRoot 'System32')
    $info.EnvironmentVariables['DOTNET_ROOT'] = (Join-Path $root 'no-shared-runtime')
    $info.EnvironmentVariables['DOTNET_ROOT_X64'] = (Join-Path $root 'no-shared-runtime')
    $info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $process = [Diagnostics.Process]::Start($info)
    try {
        $loaded = @{}
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while (!$process.HasExited -and $clock.Elapsed.TotalSeconds -lt 45) {
            $process.Refresh()
            foreach ($module in $process.Modules) {
                if ($module.ModuleName -in @('coreclr.dll', 'libSkiaSharp.dll', 'Compositor.Native.dll')) {
                    $loaded[$module.ModuleName] = $module.FileName
                }
            }
            if ($process.WaitForExit(200)) { break }
        }
        if (!$process.HasExited) { throw 'Portable WPF check timed out.' }
        if ($process.ExitCode -ne 0) { throw "Portable WPF check failed. See $shot.error.txt" }
        foreach ($name in @('coreclr.dll', 'libSkiaSharp.dll', 'Compositor.Native.dll')) {
            if (!$loaded.ContainsKey($name) -or !$loaded[$name].StartsWith($package + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Expected package-local module was not observed: $name"
            }
        }
        if (!(Test-Path -LiteralPath $shot)) { throw 'Smoke screenshot is missing.' }
        $records += [pscustomobject]@{ run = $run; exitCode = $process.ExitCode; elapsedSeconds = $clock.Elapsed.TotalSeconds; modules = $loaded; screenshot = $shot }
        Write-Host "PASS portable WPF launch $run/$Runs"
    }
    finally {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
[pscustomobject]@{
    timeUtc = [DateTimeOffset]::UtcNow.ToString('o')
    passed = $true
    description = 'Copied self-contained package in a path containing spaces, launched outside its directory with restricted child PATH and invalid shared-runtime paths. Loaded CLR, Skia and C modules observed inside package. Same development PC; does not replace a clean Windows installation test.'
    runs = $records
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/portable-validation.json') -Encoding utf8
