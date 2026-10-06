param(
    [Parameter(Mandatory=$true)][int]$AppProcessId,
    [ValidateRange(1, 180)][int]$Minutes = 20,
    [string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts/manual-window-observations.jsonl')
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ManualWindowProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int awareness);
}
'@
# Only the observer's coordinate context changes; user display settings are untouched.
[void][ManualWindowProbe]::SetProcessDpiAwareness(2)
$process = Get-Process -Id $AppProcessId
if ($process.ProcessName -ne 'Compositor.Windows') { throw 'Select a Compositor.Windows process.' }
$started = $process.StartTime.ToUniversalTime().ToString('o')
$output = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
$writer = New-Object IO.StreamWriter($output, $false, (New-Object Text.UTF8Encoding($false)))
$writer.AutoFlush = $true
$clock = [Diagnostics.Stopwatch]::StartNew()
try {
    $writer.WriteLine(([pscustomobject]@{ kind='start'; timeUtc=[DateTimeOffset]::UtcNow.ToString('o'); processId=$AppProcessId;
        processStartedUtc=$started; executable=$process.Path; durationMinutes=$Minutes;
        note='Read-only telemetry. No input injection. Does not prove physical interaction, visual correctness, or manual acceptance.' } | ConvertTo-Json -Compress))
    while ($clock.Elapsed.TotalMinutes -lt $Minutes) {
        $process.Refresh()
        if ($process.HasExited) { break }
        $window = $process.MainWindowHandle
        $rect = New-Object ManualWindowProbe+Rect
        $hasBounds = $window -ne [IntPtr]::Zero -and [ManualWindowProbe]::GetWindowRect($window, [ref]$rect)
        $writer.WriteLine(([pscustomobject]@{
            kind='sample'; timeUtc=[DateTimeOffset]::UtcNow.ToString('o'); elapsedSeconds=$clock.Elapsed.TotalSeconds;
            processId=$AppProcessId; window=$window.ToInt64(); dpi=[ManualWindowProbe]::GetDpiForWindow($window);
            monitor=[ManualWindowProbe]::MonitorFromWindow($window, 0).ToInt64(); hasBounds=$hasBounds;
            left=$rect.Left; top=$rect.Top; right=$rect.Right; bottom=$rect.Bottom;
            workingSetMiB=$process.WorkingSet64/1MB; privateMiB=$process.PrivateMemorySize64/1MB; handles=$process.HandleCount
        } | ConvertTo-Json -Compress))
        if ($process.WaitForExit(1000)) { break }
    }
    $writer.WriteLine(([pscustomobject]@{kind='end'; timeUtc=[DateTimeOffset]::UtcNow.ToString('o');
        reason=$(if ($process.HasExited) {'app-exited'} else {'duration-complete'}); elapsedSeconds=$clock.Elapsed.TotalSeconds} | ConvertTo-Json -Compress))
}
catch {
    $writer.WriteLine(([pscustomobject]@{kind='error'; timeUtc=[DateTimeOffset]::UtcNow.ToString('o'); error=$_.Exception.Message} | ConvertTo-Json -Compress))
    throw
}
finally { $writer.Dispose(); $process.Dispose() }
