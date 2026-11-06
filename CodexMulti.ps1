[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Action = 'menu',

    [Parameter(Position = 1)]
    [string]$Name,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProfilesRoot = Join-Path $ScriptRoot 'profiles'
# Every profile reads and writes the main ~/.codex chat database, so all windows share one history.
$SharedSqliteHome = Join-Path $env:USERPROFILE '.codex'

function Get-CodexPackage {
    $pkg = Get-AppxPackage -Name 'OpenAI.Codex' |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $pkg) {
        throw 'OpenAI.Codex is not installed for this user.'
    }
    return $pkg
}

function Get-CodexAppId([object]$pkg) {
    try {
        $apps = (Get-AppxPackageManifest -Package $pkg).Package.Applications.Application
        $app = $apps | Where-Object { $_.Executable -eq 'app/ChatGPT.exe' } | Select-Object -First 1
        if ($app) { return [string]$app.Id }
    } catch { }
    return 'App'
}

function Assert-ProfileName([string]$n) {
    if ([string]::IsNullOrWhiteSpace($n)) {
        throw 'Profile name is required.'
    }
    $n = $n.Trim()
    if ($n -notmatch '^[A-Za-z0-9][A-Za-z0-9 _-]{0,31}$') {
        throw 'Use 1-32 characters: letters, numbers, spaces, dash, underscore. Start with a letter or number.'
    }
    return $n
}

function Get-ProfileDir([string]$n) {
    $n = Assert-ProfileName $n
    return Join-Path $ProfilesRoot $n
}

function Get-ProfilePath([string]$n, [string]$child) {
    return [IO.Path]::Combine((Get-ProfileDir $n), $child)
}

function Get-Profiles {
    if (-not (Test-Path -LiteralPath $ProfilesRoot)) { return @() }
    return @(Get-ChildItem -LiteralPath $ProfilesRoot -Directory | Sort-Object Name)
}

function Test-ProcessAlive([int]$processId) {
    # A terminated packaged app can stay listed in WMI for a while. Trust the
    # process itself, or the old PID looks like a profile that is still open.
    try {
        $p = Get-Process -Id $processId -ErrorAction Stop
        return (-not $p.HasExited)
    } catch {
        return $false
    }
}

function Get-ProfileProcesses([string]$n) {
    # Child Chromium processes inherit the user-data-dir flag. Match only the
    # browser process, or a closing window looks like a running profile.
    $needle = [regex]::Escape((Get-ProfilePath $n 'chromium'))
    try {
        return @(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'" -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                ($_.CommandLine -match $needle) -and
                ($_.CommandLine -notmatch '--type=') -and
                (Test-ProcessAlive ([int]$_.ProcessId))
            })
    } catch {
        return @()
    }
}

function Get-ProfileWindow([int]$processId) {
    try {
        $p = Get-Process -Id $processId -ErrorAction Stop
        $p.Refresh()
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { return $p.MainWindowHandle }
    } catch { }
    return [IntPtr]::Zero
}

function Get-ProfileState([string]$n) {
    $browser = @(Get-ProfileProcesses $n)
    if ($browser.Count -eq 0) { return 'idle' }
    foreach ($b in $browser) {
        if ((Get-ProfileWindow ([int]$b.ProcessId)) -ne [IntPtr]::Zero) { return 'running' }
    }
    return 'stale'
}

function Test-ProfileRunning([string]$n) {
    return ((Get-ProfileState $n) -eq 'running')
}

function Focus-ProfileWindow([string]$n) {
    foreach ($b in Get-ProfileProcesses $n) {
        if ((Get-ProfileWindow ([int]$b.ProcessId)) -ne [IntPtr]::Zero) {
            try {
                (New-Object -ComObject WScript.Shell).AppActivate([int]$b.ProcessId) | Out-Null
                return $true
            } catch { }
        }
    }
    return $false
}

function Get-AuthLabel([string]$n) {
    $auth = Get-ProfilePath $n 'codex-home\auth.json'
    if (-not (Test-Path -LiteralPath $auth)) { return 'not signed in' }

    try {
        $raw = Get-Content -LiteralPath $auth -Raw -ErrorAction Stop
        if (-not $raw -or $raw.Length -lt 10) { return 'not signed in' }

        $j = $raw | ConvertFrom-Json
        $idToken = $null
        if ($j.tokens -and $j.tokens.id_token) { $idToken = [string]$j.tokens.id_token }

        if ($idToken) {
            $parts = $idToken.Split('.')
            if ($parts.Count -ge 2) {
                $b = $parts[1].Replace('-', '+').Replace('_', '/')
                while ($b.Length % 4 -ne 0) { $b += '=' }
                $payload = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b)) | ConvertFrom-Json
                if ($payload.email) { return [string]$payload.email }
            }
        }
        return 'signed in'
    } catch {
        return 'auth unreadable'
    }
}

function Repair-SharedHistory([string]$n) {
    # Safe while Codex is open: it never closes a profile and never touches a
    # folder that holds local rollouts. A folder the running app keeps locked is
    # reported and retried on the next launch.
    $n = Assert-ProfileName $n
    $dir = Get-ProfileDir $n
    $links = [ordered]@{
        sessions = Join-Path $SharedSqliteHome 'sessions'
        archived_sessions = Join-Path $SharedSqliteHome 'archived_sessions'
    }

    foreach ($linkName in $links.Keys) {
        $link = Join-Path $dir ('codex-home\' + $linkName)
        $target = $links[$linkName]

        if (-not (Test-Path -LiteralPath $target)) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
        }

        if (Test-Path -LiteralPath $link) {
            $item = Get-Item -LiteralPath $link -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }

            # A real folder may hold local rollouts. Replace it only when empty.
            if (@(Get-ChildItem -LiteralPath $link -Force -ErrorAction SilentlyContinue).Count -gt 0) {
                Write-Warning "Left $linkName as a local folder because it is not empty. New sessions there will not appear in other profiles."
                continue
            }

            try {
                Remove-Item -LiteralPath $link -Force -Recurse -ErrorAction Stop
            } catch {
                Write-Warning "Could not replace $linkName while '$n' is open. Close and launch it again to finish the link."
                continue
            }
        }

        cmd /c mklink /J "$link" "$target" 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not link $linkName for '$n'. Close and launch it again to retry."
        }
    }
}

function Initialize-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $dir = Get-ProfileDir $n

    New-Item -ItemType Directory -Force -Path `
        (Join-Path $dir 'codex-home'), `
        (Join-Path $dir 'electron'), `
        (Join-Path $dir 'chromium') | Out-Null

    $config = Join-Path $dir 'codex-home\config.toml'
    $sharedLine = ('sqlite_home = ''{0}''' -f $SharedSqliteHome)

    if (-not (Test-Path -LiteralPath $config)) {
        @(
            $sharedLine,
            'cli_auth_credentials_store = "file"',
            'mcp_oauth_credentials_store = "file"'
        ) | Set-Content -LiteralPath $config -Encoding ASCII
    } elseif (-not (Select-String -LiteralPath $config -Pattern '^\s*sqlite_home\s*=' -Quiet)) {
        Add-Content -LiteralPath $config -Value $sharedLine -Encoding ASCII
    }

    Repair-SharedHistory $n
    return $dir
}

function New-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $dir = Initialize-Profile $n
    Write-Host "Created profile '$n'."
    Write-Host "Launch it, then sign in with the account for this profile."
    Write-Host "Path: $dir"
}

function Start-Profile([string]$n) {
    $n = Assert-ProfileName $n

    $state = Get-ProfileState $n
    if ($state -eq 'running') {
        if (Focus-ProfileWindow $n) {
            Write-Host "Profile '$n' is already open. Focused its window."
        } else {
            Write-Host "Profile '$n' is already running."
        }
        return
    }

    if ($state -eq 'stale') {
        Write-Host "Profile '$n' has a process without a window. Restarting it."
        Stop-Profile $n | Out-Null
    }

    $dir = Initialize-Profile $n
    $pkg = Get-CodexPackage
    $exe = Join-Path $pkg.InstallLocation 'app\ChatGPT.exe'
    $cli = Join-Path $pkg.InstallLocation 'app\resources\codex.exe'
    $wrapper = Join-Path $dir 'launch.cmd'

    $lines = @(
        '@echo off',
        ('set "CODEX_HOME={0}"' -f (Join-Path $dir 'codex-home')),
        ('set "CODEX_ELECTRON_USER_DATA_PATH={0}"' -f (Join-Path $dir 'electron')),
        ('set "CODEX_CLI_PATH={0}"' -f $cli),
        ('start "" "{0}" "--user-data-dir={1}"' -f $exe, (Join-Path $dir 'chromium')),
        'exit /b 0'
    )
    Set-Content -LiteralPath $wrapper -Value $lines -Encoding ASCII

    $appId = Get-CodexAppId $pkg
    Invoke-CommandInDesktopPackage `
        -PackageFamilyName $pkg.PackageFamilyName `
        -AppId $appId `
        -Command 'cmd.exe' `
        -Args ('/c "{0}"' -f $wrapper) `
        -PreventBreakaway | Out-Null

    # Give the packaged process time to inherit the environment and create a window.
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        if ((Get-ProfileState $n) -eq 'running') { break }
    }

    $browser = @(Get-ProfileProcesses $n)
    if ($browser.Count -gt 0) {
        Write-Host "Launched '$n' (PID $($browser[0].ProcessId)). Sign in inside that window."
    } else {
        Write-Host "Launch request sent for '$n'. If no window appeared, check the Codex app and try again."
    }
}

function Stop-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $browser = @(Get-ProfileProcesses $n)
    if ($browser.Count -eq 0) {
        Write-Host "Profile '$n' is not running."
        return
    }

    foreach ($b in $browser) {
        # /T closes the renderer and helper processes with the browser process.
        # A helper can exit between enumeration and shutdown; taskkill then
        # writes to stderr, which a Stop preference would turn into a failure.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        taskkill /PID ([int]$b.ProcessId) /T /F 2>&1 | Out-Null
        $ErrorActionPreference = $previous
    }

    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        if (@(Get-ProfileProcesses $n).Count -eq 0) { break }
    }

    $left = @(Get-ProfileProcesses $n)
    if ($left.Count -gt 0) {
        throw "Some processes are still running: $($left.ProcessId -join ', ')"
    }

    Write-Host "Closed profile '$n'."
}

function New-ProfileShortcut([string]$n) {
    $n = Assert-ProfileName $n
    Initialize-Profile $n | Out-Null

    $pkg = Get-CodexPackage
    $exe = Join-Path $pkg.InstallLocation 'app\ChatGPT.exe'
    $desktop = [Environment]::GetFolderPath('Desktop')
    $lnk = Join-Path $desktop ("Codex - {0}.lnk" -f $n)

    $wsh = New-Object -ComObject WScript.Shell
    $s = $wsh.CreateShortcut($lnk)
    $s.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $s.Arguments = ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" launch "{1}"' -f (Join-Path $ScriptRoot 'CodexMulti.ps1'), $n)
    $s.WorkingDirectory = $ScriptRoot
    $s.IconLocation = "$exe,0"
    $s.Description = "Launch Codex profile $n"
    $s.Save()

    Write-Host "Shortcut created: $lnk"
}

function Remove-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $dir = Get-ProfileDir $n

    if (-not (Test-Path -LiteralPath $dir)) {
        Write-Host "No profile '$n'."
        return
    }

    $answer = 'yes'
    if (-not $Force) {
        $answer = Read-Host "Delete profile '$n' and all its local data? Type yes"
    }
    if ($answer -ne 'yes') {
        Write-Host 'Cancelled.'
        return
    }

    Remove-ProfileData $n | Out-Null
    Write-Host "Deleted profile '$n'."
}

function Remove-ProfileData([string]$n) {
    $n = Assert-ProfileName $n
    $dir = Get-ProfileDir $n

    if (-not (Test-Path -LiteralPath $dir)) { return $false }

    Stop-Profile $n | Out-Null
    Remove-Item -LiteralPath $dir -Recurse -Force

    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) ("Codex - {0}.lnk" -f $n)
    if (Test-Path -LiteralPath $lnk) {
        Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue
    }

    return $true
}

function Test-SharedHistory([string]$n) {
    $config = Get-ProfilePath $n 'codex-home\config.toml'
    if (-not (Test-Path -LiteralPath $config)) { return $false }
    return [bool](Select-String -LiteralPath $config -Pattern '^\s*sqlite_home\s*=' -Quiet)
}

function Show-Profiles {
    $profiles = Get-Profiles
    if ($profiles.Count -eq 0) {
        Write-Host 'No profiles yet.'
        return
    }

    Write-Host ('{0,-22} {1,-10} {2}' -f 'NAME', 'STATUS', 'ACCOUNT')
    Write-Host ('-' * 64)
    foreach ($p in $profiles) {
        $status = Get-ProfileState $p.Name
        $auth = Get-AuthLabel $p.Name
        Write-Host ('{0,-22} {1,-10} {2}' -f $p.Name, $status, $auth)
    }
}

function Show-Menu {
    while ($true) {
        Clear-Host
        Write-Host 'Codex Multi-Instance' -ForegroundColor Cyan
        Write-Host "Profiles: $ProfilesRoot"
        Write-Host ''
        Show-Profiles
        Write-Host ''
        Write-Host '1 Launch   2 New   3 Close   4 Shortcut   5 Delete   6 Refresh   0 Quit'
        $choice = Read-Host 'Choose'

        switch ($choice) {
            '1' { $n = Read-Host 'Profile name'; if ($n) { Start-Profile $n }; [void](Read-Host 'Press Enter') }
            '2' { $n = Read-Host 'New profile name'; if ($n) { New-Profile $n }; [void](Read-Host 'Press Enter') }
            '3' { $n = Read-Host 'Profile name'; if ($n) { Stop-Profile $n }; [void](Read-Host 'Press Enter') }
            '4' { $n = Read-Host 'Profile name'; if ($n) { New-ProfileShortcut $n }; [void](Read-Host 'Press Enter') }
            '5' { $n = Read-Host 'Profile name'; if ($n) { Remove-Profile $n }; [void](Read-Host 'Press Enter') }
            '6' { }
            '0' { return }
            default { }
        }
    }
}

if (-not (Get-Command Invoke-CommandInDesktopPackage -ErrorAction SilentlyContinue)) {
    throw 'Invoke-CommandInDesktopPackage is unavailable. Run CodexMulti.cmd (Windows PowerShell 5.1).'
}

try {
    switch ($Action.ToLowerInvariant()) {
        'menu' { Show-Menu }
        'list' { Show-Profiles }
        'new' { New-Profile $Name }
        'launch' { Start-Profile $Name }
        'close' { Stop-Profile $Name }
        'shortcut' { New-ProfileShortcut $Name }
        'delete' { Remove-Profile $Name }
        'repair' {
            if ($Name) {
                Initialize-Profile $Name | Out-Null
                Write-Host "Repaired shared history links for '$Name'."
            } else {
                foreach ($p in Get-Profiles) { Initialize-Profile $p.Name | Out-Null }
                Write-Host 'Repaired shared history links.'
            }
        }
        default { Show-Menu }
    }
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
