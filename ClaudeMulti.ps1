[CmdletBinding()]
param([string]$Action = 'menu', [string]$Name, [switch]$Force)
$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$CodexRoot = $ScriptRoot
$ProfilesRoot = Join-Path $CodexRoot 'profiles\claude'

function Get-ClaudePackage {
    $pkg = Get-AppxPackage -Name 'AnthropicPBC.Claude' | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $pkg) { throw 'AnthropicPBC.Claude is not installed.' }
    return $pkg
}
function Get-ClaudeAppId([object]$pkg) {
    try {
        $apps = (Get-AppxPackageManifest -Package $pkg).Package.Applications.Application
        $app = $apps | Where-Object { $_.Executable -eq 'app\Claude.exe' } | Select-Object -First 1
        if ($app) { return [string]$app.Id }
    } catch { }
    return 'Claude'
}
function Assert-ProfileName([string]$n) {
    if ([string]::IsNullOrWhiteSpace($n)) { throw 'Profile name is required.' }
    $n = $n.Trim()
    if ($n -notmatch '^[A-Za-z0-9][A-Za-z0-9 _-]{0,31}$') { throw 'Use 1-32 characters: letters, numbers, spaces, dash, underscore.' }
    return $n
}
function Get-ProfileDir([string]$n) { return Join-Path $ProfilesRoot (Assert-ProfileName $n) }
function Get-ProfilePath([string]$n, [string]$child) { return [IO.Path]::Combine((Get-ProfileDir $n), $child) }
function Get-Profiles { if (-not (Test-Path $ProfilesRoot)) { return @() }; return @(Get-ChildItem $ProfilesRoot -Directory | Sort-Object Name) }
function Test-ProcessAlive([int]$processId) {
    try { $p = Get-Process -Id $processId -ErrorAction Stop; return (-not $p.HasExited) } catch { return $false }
}
function Get-ProfileProcesses([string]$n) {
    $needle = [regex]::Escape((Get-ProfilePath $n 'userData'))
    try {
        return @(Get-CimInstance Win32_Process -Filter "Name='Claude.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and ($_.CommandLine -match $needle) -and ($_.CommandLine -notmatch '--type=') -and (Test-ProcessAlive ([int]$_.ProcessId)) })
    } catch { return @() }
}
function Get-ProfileWindow([int]$processId) {
    try { $p = Get-Process -Id $processId -ErrorAction Stop; $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { return $p.MainWindowHandle } } catch { }
    return [IntPtr]::Zero
}
function Get-ProfileState([string]$n) {
    $browser = @(Get-ProfileProcesses $n)
    if ($browser.Count -eq 0) { return 'idle' }
    foreach ($b in $browser) { if ((Get-ProfileWindow ([int]$b.ProcessId)) -ne [IntPtr]::Zero) { return 'running' } }
    return 'stale'
}
function Focus-ProfileWindow([string]$n) {
    foreach ($b in Get-ProfileProcesses $n) {
        if ((Get-ProfileWindow ([int]$b.ProcessId)) -ne [IntPtr]::Zero) {
            try { (New-Object -ComObject WScript.Shell).AppActivate([int]$b.ProcessId) | Out-Null; return $true } catch { }
        }
    }
    return $false
}
function Get-AuthLabel([string]$n) {
    # Claude Desktop email is in Electron storage, not a plain file. Fall back to CLAUDE_CONFIG_DIR creds if present.
    $cred = Get-ProfilePath $n 'config\.credentials.json'
    if (Test-Path $cred) { return 'signed in (cli creds present)' }
    return 'not signed in'
}
function Initialize-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $dir = Get-ProfileDir $n
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'config'), (Join-Path $dir 'userData') | Out-Null
    return $dir
}
function New-Profile([string]$n) {
    $n = Assert-ProfileName $n; $dir = Initialize-Profile $n
    Write-Host "Created Claude profile '$n'."; Write-Host "Launch it, then sign in with the account for this profile."; Write-Host "Path: $dir"
}
function Start-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $state = Get-ProfileState $n
    if ($state -eq 'running') {
        if (Focus-ProfileWindow $n) { Write-Host "Profile '$n' is already open. Focused its window." } else { Write-Host "Profile '$n' is already running." }
        return
    }
    if ($state -eq 'stale') { Write-Host "Profile '$n' has a process without a window. Restarting it."; Stop-Profile $n | Out-Null }
    $dir = Initialize-Profile $n
    $pkg = Get-ClaudePackage
    $exe = Join-Path $pkg.InstallLocation 'app\Claude.exe'
    $wrapper = Join-Path $dir 'launch.cmd'
    $lines = @(
        '@echo off',
        ('set "CLAUDE_CONFIG_DIR={0}"' -f (Join-Path $dir 'config')),
        ('set "CLAUDE_CODE_CONFIG_DIR={0}"' -f (Join-Path $dir 'config')),
        ('start "" "{0}" "--user-data-dir={1}"' -f $exe, (Join-Path $dir 'userData')),
        'exit /b 0'
    )
    Set-Content -LiteralPath $wrapper -Value $lines -Encoding ASCII
    $appId = Get-ClaudeAppId $pkg
    Invoke-CommandInDesktopPackage -PackageFamilyName $pkg.PackageFamilyName -AppId $appId -Command 'cmd.exe' -Args ('/c "{0}"' -f $wrapper) -PreventBreakaway | Out-Null
    for ($i = 0; $i -lt 20; $i++) { Start-Sleep -Milliseconds 500; if ((Get-ProfileState $n) -eq 'running') { break } }
    $browser = @(Get-ProfileProcesses $n)
    if ($browser.Count -gt 0) { Write-Host "Launched '$n' (PID $($browser[0].ProcessId)). Sign in inside that window." }
    else { Write-Host "Launch request sent for '$n'. If no window appeared, check the Claude app and try again." }
}
function Stop-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $browser = @(Get-ProfileProcesses $n)
    if ($browser.Count -eq 0) { Write-Host "Profile '$n' is not running."; return }
    foreach ($b in $browser) {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        taskkill /PID ([int]$b.ProcessId) /T /F 2>&1 | Out-Null
        $ErrorActionPreference = $prev
    }
    for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 250; if (@(Get-ProfileProcesses $n).Count -eq 0) { break } }
    $left = @(Get-ProfileProcesses $n)
    if ($left.Count -gt 0) { throw "Some processes are still running: $($left.ProcessId -join ', ')" }
    Write-Host "Closed profile '$n'."
}
function Show-Profiles {
    $profiles = Get-Profiles
    if ($profiles.Count -eq 0) { Write-Host 'No Claude profiles yet.'; return }
    Write-Host ('{0,-22} {1,-10} {2}' -f 'NAME','STATUS','ACCOUNT')
    Write-Host ('-' * 64)
    foreach ($p in $profiles) { Write-Host ('{0,-22} {1,-10} {2}' -f $p.Name, (Get-ProfileState $p.Name), (Get-AuthLabel $p.Name)) }
}
if (-not (Get-Command Invoke-CommandInDesktopPackage -ErrorAction SilentlyContinue)) { throw 'Invoke-CommandInDesktopPackage is unavailable. Run with Windows PowerShell 5.1.' }
try {
    switch ($Action.ToLowerInvariant()) {
        'menu' { Show-Profiles; Write-Host 'Use: new <name> | launch <name> | close <name> | list' }
        'list' { Show-Profiles }
        'new' { New-Profile $Name }
        'launch' { Start-Profile $Name }
        'close' { Stop-Profile $Name }
        default { Show-Profiles }
    }
} catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
