param([switch]$Test, [switch]$Benchmark, [switch]$Publish)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & dotnet build Compositor.Windows.slnx -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Test) {
        & dotnet test Compositor.Tests/Compositor.Tests.csproj -c Release --no-build --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
        $app = Join-Path $PSScriptRoot 'Compositor.App\bin\Release\net10.0-windows10.0.19041.0\Compositor.Windows.exe'
        $shot = Join-Path $PSScriptRoot 'artifacts\ui-smoke.png'
        $process = Start-Process -FilePath $app -ArgumentList @('--smoke', ('"' + $shot + '"')) -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw 'WPF smoke test timed out.' }
        if ($process.ExitCode -ne 0) { throw "WPF smoke test failed. See $shot.error.txt" }
    }
    if ($Benchmark) {
        & dotnet run --project Compositor.Benchmarks/Compositor.Benchmarks.csproj -c Release --no-build -- artifacts/benchmark.json
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
    }
    if ($Publish) {
        & dotnet publish Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination artifacts/publish/LICENSE.txt
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination artifacts/publish/THIRD-PARTY-NOTICES.md

        $assets = Get-Content -LiteralPath 'Compositor.App/obj/project.assets.json' -Raw | ConvertFrom-Json
        $packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
        $packagePaths = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Value.path })
        $runtime = Get-Content -LiteralPath 'artifacts/publish/Compositor.Windows.runtimeconfig.json' -Raw | ConvertFrom-Json
        foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
            $packagePaths += ($framework.name.ToLowerInvariant() + '.runtime.win-x64/' + $framework.version)
        }
        foreach ($packagePath in $packagePaths | Sort-Object -Unique) {
            foreach ($packageRoot in $packageRoots) {
                $packageDirectory = Join-Path $packageRoot $packagePath
                if (!(Test-Path -LiteralPath $packageDirectory)) { continue }
                $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|THIRD.PARTY.NOTICES)([.]|$)' })
                if ($notices.Count -eq 0) { continue }
                $licenseTarget = Join-Path 'artifacts/publish/licenses' ($packagePath -replace '[/\\\\]', '-')
                New-Item -ItemType Directory -Path $licenseTarget -Force | Out-Null
                foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $licenseTarget }
                break
            }
        }

    }
}
finally { Pop-Location }
