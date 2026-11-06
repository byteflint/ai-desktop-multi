[CmdletBinding()]
param(
    [string]$Action = 'status',
    [string]$Name = 'obsidian',
    [int]$Port = 0,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProfilesRoot = Join-Path $ScriptRoot 'profiles'

# One line reason: OpenCode CLI is not on PATH yet, npm packages are the
# smallest install path (Node 24 is already present). This keeps Desktop
# harness alive and adds OpenCode as an MCP tool, no exe patching.
function Test-OpenCode {
    return ([bool](Get-Command 'opencode' -ErrorAction SilentlyContinue))
}
function Test-Mcp {
    return ([bool](Get-Command 'npx' -ErrorAction SilentlyContinue))
}
function Get-ProfileDir([string]$n) { return Join-Path $ProfilesRoot $n }
function Get-ProfileConfig([string]$n) { return Join-Path (Get-ProfileDir $n) 'codex-home\config.toml' }

function Get-ProfilePort([string]$n) {
    if ($Port -gt 0) { return $Port }
    # Static per-profile port to avoid collisions when two profiles run.
    $hash = 0
    foreach ($c in $n.ToCharArray()) { $hash = ($hash + [int][char]$c) % 1000 }
    return (4100 + $hash)
}

function Add-McpBlock([string]$configPath, [int]$port) {
    if (-not (Test-Path $configPath)) { throw "Profile config not found: $configPath. Run CodexMulti new first." }
    $text = Get-Content -LiteralPath $configPath -Raw
    if ([regex]::IsMatch($text, '(?m)^\s*\[mcp_servers\.opencode\]')) {
        Write-Host 'opencode MCP already configured.'
        return
    }
    $block = @(
        '',
        '[mcp_servers.opencode]',
        'command = "npx"',
        'args = ["-y", "opencode-mcp"]',
        ('env = { OPENCODE_BASE_URL = "http://127.0.0.1:' + $port + '" }'),
        ''
    ) -join "`n"
    Add-Content -LiteralPath $configPath -Value $block -Encoding ASCII
    Write-Host "Added opencode MCP to $configPath (port $port)."
}

function Start-Serve([string]$n, [int]$port) {
    if (-not (Test-OpenCode)) { throw 'opencode CLI not found. Install: npm i -g opencode-ai' }
    $dir = Get-ProfileDir $n
    $cwd = Join-Path $ScriptRoot 'profiles'
    $log = Join-Path $dir 'opencode-serve.log'
    $existing = Get-Process -Name 'opencode' -ErrorAction SilentlyContinue | Where-Object {
        try { (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)" -ErrorAction SilentlyContinue).CommandLine -match "port\s+$port" } catch { $false }
    }
    if ($existing) { Write-Host "opencode serve already running on $port for '$n'."; return }
    $p = Start-Process 'opencode' -ArgumentList @('serve','--hostname','127.0.0.1','--port',"$port") -WorkingDirectory $cwd -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if (-not (Test-ProcessAlive $p.Id)) { throw "opencode serve exited immediately. See $log" }
    Write-Host "Started opencode serve for '$n' on http://127.0.0.1:$port (PID $($p.Id)). Log: $log"
    return $p.Id
}
function Test-ProcessAlive([int]$processId) {
    try { $p = Get-Process -Id $processId -ErrorAction Stop; return (-not $p.HasExited) } catch { return $false }
}

try {
    switch ($Action.ToLowerInvariant()) {
        'status' {
            Write-Host "opencode CLI on PATH: $(Test-OpenCode)  npx on PATH: $(Test-Mcp)"
            if (Test-OpenCode) { & opencode --version 2>&1 | Select-Object -First 2 }
            $cfg = Get-ProfileConfig $Name
            if (Test-Path $cfg) {
                $has = Select-String -LiteralPath $cfg -Pattern 'mcp_servers\.opencode' -Quiet
                Write-Host "Profile '$Name' config: $(Split-Path $cfg)"
                Write-Host "  opencode MCP configured: $has"
                Write-Host "  port for '$Name': $(Get-ProfilePort $Name)"
            } else {
                Write-Host "No profile '$Name' config yet. Run CodexMulti new $Name first."
            }
        }
        'setup' {
            if (-not (Test-OpenCode)) { Write-Host 'Installing opencode-ai...'; npm i -g opencode-ai | Out-Null }
            if (-not (Test-OpenCode)) { throw 'opencode CLI still not found after install.' }
            & opencode --version 2>&1 | Select-Object -First 2
            Add-McpBlock (Get-ProfileConfig $Name) (Get-ProfilePort $Name)
            Write-Host 'Next: launch the Codex window, then call opencode_setup inside the prompt.'
        }
        'serve' {
            $port = Get-ProfilePort $Name
            Start-Serve $Name $port
        }
        default { Write-Host 'Actions: status | setup | serve (Name, Port optional)' }
    }
} catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
