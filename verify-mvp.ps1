param([ValidateRange(200, 100000)][int]$Iterations = 1200)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $report = Join-Path $PSScriptRoot 'artifacts/mvp-acceptance.json'
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    if (@(Get-Process -Name Compositor.Windows -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Close Compositor before building the acceptance package; running user sessions will not be stopped.'
    }
    # RID-specific publishing changes NuGet lock metadata. Preserve the checked-in, portable locks.
    $locks = @{}
    foreach ($name in @('Compositor.App', 'Compositor.Core', 'Compositor.Imaging')) {
        $path = Join-Path $PSScriptRoot "$name/packages.lock.json"
        $locks[$path] = [IO.File]::ReadAllBytes($path)
    }
    try { & ./build.ps1 -Test -Stability -Publish }
    finally { foreach ($path in $locks.Keys) { [IO.File]::WriteAllBytes($path, $locks[$path]) } }

    $bench = Join-Path $PSScriptRoot 'Compositor.Benchmarks/bin/Release/net10.0/Compositor.Benchmarks.exe'
    & $bench --save-crash artifacts/save-crash-validation.json
    if ($LASTEXITCODE -ne 0) { throw 'Save crash validation failed.' }
    & $bench --soak artifacts/mvp-soak-validation.json $Iterations
    if ($LASTEXITCODE -ne 0) { throw 'Mixed edit validation failed.' }

    & ./verify-portable.ps1

    $stability = Get-Content -LiteralPath artifacts/stability-benchmark.json -Raw | ConvertFrom-Json
    # Missing measurements or a failed budget must never silently pass.
    $passes = @($stability.runs | ForEach-Object { $_.passes })
    if ($stability.runs.Count -ne 3 -or $passes.Count -ne 6 -or @($passes | Where-Object { $_.meetsInitialBudget -ne $true }).Count -gt 0) {
        throw 'Functional checks passed, but a 4K performance budget failed. Review artifacts/stability-benchmark.json.'
    }
    [pscustomobject]@{
        timeUtc = [DateTimeOffset]::UtcNow.ToString('o')
        localAutomatedChecksPassed = $true
        mvpAccepted = $false
        iterations = $Iterations
        reports = @('ui-smoke.checks.json', 'save-crash-validation.json', 'mvp-soak-validation.json', 'stability-benchmark.json', 'portable-validation.json')
        pending = @('Actual Mac round trip', 'Physical mouse interactions', 'Multi-monitor DPI transitions', 'Clean Windows 11 PC and extended interactive use')
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath artifacts/mvp-acceptance.json -Encoding utf8
    Write-Host 'Local automated MVP acceptance passed. Mac round trip, physical input, multi-monitor DPI and clean-PC acceptance remain separate.'
}
finally { Pop-Location }
