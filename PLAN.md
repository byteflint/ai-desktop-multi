# PLAN: Universal Desktop Harness (Codex + Claude + OpenCode)

## What you asked
- One launcher that can run **several** real desktop windows at once (Codex + Claude), each with its own account.
- Inside ChatGPT / Claude, be able to pick **OpenCode or another agent** (GPT / Claude / others) using your existing subscriptions, with login.
- Keep the full harness: screenshots, Computer Use, browser, plugins, skills, sandbox.
- Not a CLI / API proxy that loses those tools.

## Why CLI proxies lose screenshot / Computer Use
Codex Desktop is not a text API. The harness is:
`ChatGPT.exe` (Electron) -> `codex.exe app-server` -> MCP servers (`codex-app-tools`, `browser`, `computer-use`, `node_repl`).
Screenshots and CUA come from the Electron window + those MCP servers, wired through `CODEX_HOME`, `CODEX_ELECTRON_USER_DATA_PATH`, `CODEX_CLI_PATH`, and `--user-data-dir`.

A plain `opencode` CLI / API bridge only has text + bash + edit. It has no Electron window, no CUA pipe, no screenshot MCP. That's why it feels broken compared to Desktop.

## What exists today (researched)

| Repo | What it does | Gap vs this plan |
|---|---|---|
| `ugarchance/codex-plus-plus` | Copies MSIX payload + patches `app.asar`, per-profile `--user-data-dir` for Codex. Good isolation, but patches signed binary, breaks Authenticode. | Must copy/patch to win. Our version keeps the signed Store exe untouched via `Invoke-CommandInDesktopPackage` - strictly better on updates. |
| `Zoltak-Dev/ai-multi-instance` | Python manager for **both** Claude + Codex Desktop profiles side by side (`--user-data-dir`), reads each profile's token + `codex/usage`. Closest to goal. | Python, unsigned exe, separate Python runtime. Fails to explain harness/MCP sharing. |
| `vodongha/claude-desktop-clone`, `odahcam/claude-desktop-profiles`, `thomasog/multi-claude` | Claude Desktop multi-instance via `--user-data-dir` (Win/MSIX or macOS). Proves Claude also isolates via that flag. | macOS first, Linux safeStorage caveats. Windows Cowork VM limits noted in `vodongha`. |
| `farion1231/cc-switch` | Cross-platform desktop switcher for Claude Code / Codex / OpenCode provider switching. Unified account manager. | Switches provider/config, does **not** give multi-window harness isolation. |
| `different-ai/openwork` | OpenCode-based desktop app (Cowork/Codex alternative). `opencode serve` + desktop UI. Any provider / API key / Ollama. | Replaces Desktop, does not reuse Codex/Claude Desktop harness (no `codex-app-tools`/`computer-use`). |
| `AlaeddineMessadi/opencode-mcp` | MCP bridge: `opencode serve --port 4096` + `opencode-mcp` as MCP server for Claude Code / Cursor / ChatGPT. 70 tools. | **This is the right way to put OpenCode inside Desktop**, but needs Desktop MCP wiring + per-profile ports. |

No repo today does all three: (1) Codex + Claude multi-window with **one** launcher, (2) shared history via junctions/`sqlite_home`, (3) OpenCode injected as MCP so you keep screenshots/CUA.

## The better architecture (this plan)

```
+------------------------------------------------ Dev machine (Win10/11, MSIX)
|  AiDesktopMulti.exe (compiled C#, this repo)
|  |- profiles\codex\<name>\   CODEX_HOME + chromium + electron
|  |- profiles\claude\<name>\  CLAUDE_CONFIG_DIR + userData
|  +- backend per window: official desktop exe, untouched signature
|
|  Window A (Codex):  ChatGPT.exe --user-data-dir=codex\chromium
|                     CODEX_HOME=codex\codex-home   -> app-server (own token)
|  Window B (Codex):  ChatGPT.exe --user-data-dir=codex2\chromium
|  Window C (Claude): Claude.exe --user-data-dir=claude\userData
|                     CLAUDE_CONFIG_DIR=claude\config -> Claude Code daemon (own token)
|
|  OpenCode inside Desktop (optional, per profile):
|    opencode serve --port <n>   (one per profile, no clash)
|    opencode-mcp as MCP server in Codex config.toml [mcp_servers.opencode]
|    or Claude Desktop claude_desktop_config.json mcpServers.opencode
|  -> you pick opencode provider/model in the Desktop prompt.
|     Subscription auth via OpenCode's own OAuth (ChatGPT Plus / Claude Pro).
|     No API key needed. Desktop keeps screenshots / browser / CUA / skills.
+------------------------------------------------
```

Key rules:
- Never patch `app.asar` / exe. Activate via `Invoke-CommandInDesktopPackage` like today.
- Each profile owns: `CODEX_HOME` / `CLAUDE_CONFIG_DIR`, `user-data-dir`, electron data, MCP oauth store = `file`.
- History: Codex profiles share `sqlite_home` + `sessions`/`archived_sessions` junctions. Claude profiles share `~/.claude` projects/sessions only if you want it (opt-in, default separate to avoid quota confusion).
- OpenCode is a **tool** inside the harness, not a replacement harness. That preserves screenshots/CUA.

## Phases

### Phase 1 (build now): Claude Desktop multi-instance on Windows
Reuse the Codex pattern for `AnthropicPBC.Claude`:
`Get-AppxPackage` -> exe `app\Claude.exe` -> wrapper `.cmd` sets `CLAUDE_CONFIG_DIR` + `--user-data-dir` -> `Invoke-CommandInDesktopPackage`.
Actions mirror Codex: `new`/`launch`/`close`/`shortcut`/`delete`/`list`/`repair`.
Status: `idle`/`starting`/`running`/`stale`, focus via `WScript.Shell AppActivate`, close via `taskkill /PID /T /F` (with stderr fix already in Codex).

### Phase 2: OpenCode as MCP inside Codex + Claude
- Detect `opencode` (today: not on PATH). Install if missing (`npm i -g opencode-ai` or `irm ... | iex` per opencode docs).
- Per profile: start `opencode serve --hostname 127.0.0.1 --port <profile_port>` on launch; stop on close.
- Add `opencode-mcp` to Codex `config.toml` automatically:
  ```toml
  [mcp_servers.opencode]
  command = "npx"
  args = ["-y", "opencode-mcp"]
  ```
  and to Claude Desktop `claude_desktop_config.json`.
- In Desktop prompt you then call `opencode_setup`, pick provider/model, `opencode_fire`. No CLI window, no screenshot loss.

### Phase 3: One surface in AiDesktopMulti.exe
- Add `App` column: `Codex` / `Claude`.
- Profiles root: `profiles\codex\...`, `profiles\claude\...`.
- Account column: decode `auth.json`/`settings.json` email already done for Codex, extend to Claude.
- Usage hint: show `You have a usage limit reset available` per profile (already visible in Desktop, surface via re-read of usage cache if available).

### Phase 4: Publish
- `AiDesktopMulti.exe` + `CodexMulti.ps1` + `ClaudeMulti.ps1` + `build.ps1` in one release.
- README + DESIGN.md updated with junction history + live repair + concurrency guarantees.

## Verification (must pass before claim)
1. Codex main (3320) + Codex obsidian (14864) concurrent, `OB` vs `JS` avatars differ.
2. Same for Claude: main + profile concurrent, different avatars.
3. OpenCode MCP task in Codex Desktop can list files **and** take a screenshot (proves harness kept).
4. `codex doctor` clean, no zombie `ChatGPT.exe`/`Claude.exe` after `close`.

## Risks
- Claude Cowork VM is per-machine (Hyper-V). Two Claude windows share one VM - may limit full isolation. Document, do not promise VM per profile.
- Store updates change exe paths/CLI resolution. Re-test after every update (already needed for Codex).
- Using multiple accounts to dodge usage limits may violate provider ToS. This tool is for own accounts convenience only.
