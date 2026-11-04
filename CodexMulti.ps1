[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Action = 'gui',

    [Parameter(Position = 1)]
    [string]$Name
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

function Get-ProfileProcesses([string]$n) {
    $needle = Get-ProfilePath $n 'chromium'
    try {
        return @(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($needle)) })
    } catch {
        return @()
    }
}

function Test-ProfileRunning([string]$n) {
    return ((Get-ProfileProcesses $n).Count -gt 0)
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

function Initialize-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $dir = Get-ProfileDir $n

    New-Item -ItemType Directory -Force -Path `
        (Join-Path $dir 'codex-home'), `
        (Join-Path $dir 'electron'), `
        (Join-Path $dir 'chromium') | Out-Null

    $config = Join-Path $dir 'codex-home\config.toml'
    if (-not (Test-Path -LiteralPath $config)) {
        @(
            ('sqlite_home = ''{0}''' -f $SharedSqliteHome),
            'cli_auth_credentials_store = "file"',
            'mcp_oauth_credentials_store = "file"'
        ) | Set-Content -LiteralPath $config -Encoding ASCII
    }

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
    $dir = Initialize-Profile $n

    if (Test-ProfileRunning $n) {
        Write-Host "Profile '$n' is already running."
        return
    }

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

    Start-Sleep -Seconds 2
    $procs = Get-ProfileProcesses $n
    if ($procs.Count -gt 0) {
        Write-Host "Launched '$n' (PID $($procs[0].ProcessId)). Sign in inside that window."
    } else {
        Write-Host "Launch request sent for '$n'. If no window appeared, check the Codex app and try again."
    }
}

function Stop-Profile([string]$n) {
    $n = Assert-ProfileName $n
    $procs = Get-ProfileProcesses $n
    if ($procs.Count -eq 0) {
        Write-Host "Profile '$n' is not running."
        return
    }

    $pids = @($procs | Select-Object -ExpandProperty ProcessId -Unique)
    foreach ($procId in $pids) {
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Seconds 2
    $left = Get-ProfileProcesses $n
    if ($left.Count -gt 0) {
        Write-Host "Some processes are still running: $($left.ProcessId -join ', ')"
    } else {
        Write-Host "Closed profile '$n'."
    }
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

    $answer = Read-Host "Delete profile '$n' and all its local data? Type yes"
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
        $status = if (Test-ProfileRunning $p.Name) { 'running' } else { 'idle' }
        $auth = Get-AuthLabel $p.Name
        Write-Host ('{0,-22} {1,-10} {2}' -f $p.Name, $status, $auth)
    }
}

function Show-Gui {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName Microsoft.VisualBasic
    [Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object Windows.Forms.Form
    $form.Text = 'AI Desktop Multi'
    $form.AutoScaleMode = [Windows.Forms.AutoScaleMode]::None
    $screen = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $form.ClientSize = New-Object Drawing.Size(
        [Math]::Min(840, $screen.Width - 60),
        [Math]::Min(470, $screen.Height - 60))
    $form.MinimumSize = New-Object Drawing.Size(640, 360)
    $form.StartPosition = 'CenterScreen'
    $form.Font = New-Object Drawing.Font('Segoe UI', 9)

    try {
        $exe = Join-Path (Get-CodexPackage).InstallLocation 'app\ChatGPT.exe'
        $form.Icon = [Drawing.Icon]::ExtractAssociatedIcon($exe)
    } catch { }

    $list = New-Object Windows.Forms.ListView
    $list.Dock = 'Fill'
    $list.View = 'Details'
    $list.FullRowSelect = $true
    $list.MultiSelect = $false
    $list.HideSelection = $false
    [void]$list.Columns.Add('Profile', 170)
    [void]$list.Columns.Add('Status', 90)
    [void]$list.Columns.Add('Account', 340)
    [void]$list.Columns.Add('Chat history', 120)

    $bar = New-Object Windows.Forms.Panel
    $bar.Dock = 'Fill'
    $bar.Padding = New-Object Windows.Forms.Padding(10, 10, 10, 10)

    $flow = New-Object Windows.Forms.FlowLayoutPanel
    $flow.Dock = 'Fill'
    $flow.FlowDirection = 'LeftToRight'
    $flow.WrapContents = $false
    [void]$bar.Controls.Add($flow)

    $layout = New-Object Windows.Forms.TableLayoutPanel
    $layout.Dock = 'Fill'
    $layout.ColumnCount = 1
    $layout.RowCount = 2
    [void]$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent, 100)))
    [void]$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute, 54)))
    [void]$layout.Controls.Add($list, 0, 0)
    [void]$layout.Controls.Add($bar, 0, 1)
    [void]$form.Controls.Add($layout)

    $refresh = {
        $keep = $null
        if ($list.SelectedItems.Count -gt 0) { $keep = [string]$list.SelectedItems[0].Tag }
        $list.BeginUpdate()
        try {
            $list.Items.Clear()
            foreach ($p in Get-Profiles) {
                $running = Test-ProfileRunning $p.Name
                $item = New-Object Windows.Forms.ListViewItem($p.Name)
                [void]$item.SubItems.Add($(if ($running) { 'running' } else { 'idle' }))
                [void]$item.SubItems.Add((Get-AuthLabel $p.Name))
                [void]$item.SubItems.Add($(if (Test-SharedHistory $p.Name) { 'shared' } else { 'separate' }))
                $item.Tag = $p.Name
                if ($running) { $item.ForeColor = [Drawing.Color]::FromArgb(0, 120, 60) }
                [void]$list.Items.Add($item)
                if ($keep -and $p.Name -eq $keep) {
                    $item.Selected = $true
                    $item.Focused = $true
                }
            }
        } finally {
            $list.EndUpdate()
        }
    }

    $selected = {
        if ($list.SelectedItems.Count -eq 0) { return $null }
        return [string]$list.SelectedItems[0].Tag
    }

    $onLaunch = {
        $n = & $selected
        if (-not $n) { return }
        try { Start-Profile $n } catch {
            [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Launch failed') | Out-Null
        }
        & $refresh
    }

    $onClose = {
        $n = & $selected
        if (-not $n) { return }
        Stop-Profile $n | Out-Null
        & $refresh
    }

    $onNew = {
        $n = [Microsoft.VisualBasic.Interaction]::InputBox('Profile name', 'New profile')
        if ([string]::IsNullOrWhiteSpace($n)) { return }
        try {
            New-Profile $n
            & $refresh
            foreach ($item in $list.Items) {
                if ($item.Tag -eq $n) { $item.Selected = $true; $item.Focused = $true }
            }
            [Windows.Forms.MessageBox]::Show("Created '$n'. Click Launch, then sign in inside that window.", 'Profile created') | Out-Null
        } catch {
            [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Could not create profile') | Out-Null
        }
    }

    $onShortcut = {
        $n = & $selected
        if (-not $n) { return }
        New-ProfileShortcut $n | Out-Null
        [Windows.Forms.MessageBox]::Show("Desktop shortcut created for '$n'.", 'Shortcut') | Out-Null
    }

    $onDelete = {
        $n = & $selected
        if (-not $n) { return }
        $answer = [Windows.Forms.MessageBox]::Show(
            "Delete profile '$n' and all its local data?",
            'Delete profile',
            [Windows.Forms.MessageBoxButtons]::YesNo,
            [Windows.Forms.MessageBoxIcon]::Warning)
        if ($answer -ne [Windows.Forms.DialogResult]::Yes) { return }
        Remove-ProfileData $n | Out-Null
        & $refresh
    }

    $addButton = {
        param([string]$Text, [int]$Width, [scriptblock]$OnClick)
        $button = New-Object Windows.Forms.Button
        $button.Text = $Text
        $button.Width = $Width
        $button.Height = 30
        $button.Margin = New-Object Windows.Forms.Padding(0, 0, 8, 0)
        $button.Add_Click($OnClick)
        [void]$flow.Controls.Add($button)
    }

    & $addButton 'Launch' 90 $onLaunch
    & $addButton 'Close' 80 $onClose
    & $addButton 'New' 80 $onNew
    & $addButton 'Shortcut' 90 $onShortcut
    & $addButton 'Delete' 80 $onDelete
    & $addButton 'Refresh' 80 { & $refresh }

    $list.Add_DoubleClick({ & $onLaunch })

    $timer = New-Object Windows.Forms.Timer
    $timer.Interval = 3000
    $timer.Add_Tick({ & $refresh })
    $timer.Start()
    $form.Add_FormClosed({ $timer.Stop() })

    & $refresh
    if ($list.Items.Count -gt 0) {
        $list.Items[0].Selected = $true
        $list.Items[0].Focused = $true
    }

    [void]$form.ShowDialog()
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
        'gui' { Show-Gui }
        'menu' { Show-Menu }
        'list' { Show-Profiles }
        'new' { New-Profile $Name }
        'launch' { Start-Profile $Name }
        'close' { Stop-Profile $Name }
        'shortcut' { New-ProfileShortcut $Name }
        'delete' { Remove-Profile $Name }
        default { Show-Menu }
    }
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
