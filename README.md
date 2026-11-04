# AI Desktop Multi

Run several **real** Codex desktop windows on one Windows machine, each signed into its own ChatGPT account, all sharing one chat history.

Every window is the official app, not a wrapper or a proxy, so screenshots, Computer Use, the built-in browser, plugins, skills, and the sandbox keep working. Only the account, the window state, and the storage folders are separated.

## Why this exists

Directly starting `ChatGPT.exe` from `C:\Program Files\WindowsApps` fails with `Access is denied`. The app must be activated through its MSIX package identity. This launcher does that with `Invoke-CommandInDesktopPackage`, then hands the app an isolated `CODEX_HOME` and window data folder.

## Requirements

- Windows 10 or 11
- Codex desktop app installed from the Microsoft Store (`OpenAI.Codex`)
- Windows PowerShell 5.1 (the `.cmd` and `.vbs` wrappers use it; PowerShell 7 cannot call the package activation cmdlet)

## Quick start

1. Double-click `CodexMulti.vbs`. The window opens without a console.
2. Click **New**, type a profile name, press OK.
3. Select the profile and click **Launch**.
4. Sign in inside that Codex window with the account you want for that profile.
5. Repeat for as many accounts as you need.

Everything is also available from the command line:

```powershell
.\CodexMulti.cmd list
.\CodexMulti.cmd new work
.\CodexMulti.cmd launch work
.\CodexMulti.cmd close work
.\CodexMulti.cmd shortcut work
.\CodexMulti.cmd delete work
.\CodexMulti.cmd menu
```

`CodexMulti.cmd` with no arguments opens the GUI. The `.vbs` is just a console-free way to start the same GUI.

## GUI

- **Profile** - name of the profile
- **Status** - running or idle
- **Account** - email decoded from that profile's `auth.json`
- **Chat history** - `shared` when the profile points at the common database, `separate` otherwise
- **Launch / Close** - start or stop only that profile's processes
- **New** - create a profile folder
- **Shortcut** - put a desktop shortcut for that profile
- **Delete** - remove the profile and its local data after confirmation

Double-clicking a row launches it. The list refreshes itself every few seconds.

## Shared chat history

Each profile signs in with a different account, but all of them read and write one SQLite state database. This is set in each profile's `codex-home\config.toml`:

```toml
sqlite_home = 'C:\Users\<you>\.codex'
```

`CODEX_HOME` stays per profile, so `auth.json`, config, skills, plugins, and session rollouts remain separate. Only the database is common, which is why a thread created in one window appears in the others and new messages show up in both directions.

New profiles get this line automatically. A profile created before this feature can be updated by adding the same line to its `config.toml` while the window is closed.

### Caveats

- Not an officially supported configuration. SQLite handles concurrent windows, but keep a backup of `~\.codex\state_5.sqlite` if the history matters.
- Memories, goals, logs, and the automation queue live in the same folder, so they are shared too.
- Cloud ChatGPT conversations belong to the account that created them. Only local Codex threads are shared.
- The shared database is keyed by thread id. Two windows editing the same thread at the same time is not a supported workflow.

## Files

```
CodexMulti.ps1    profile logic, GUI, CLI
CodexMulti.cmd    CLI entry point, also opens the GUI with no arguments
CodexMulti.vbs    console-free GUI entry point
profiles\<name>\  codex-home, electron, chromium per profile (not in git)
```

## How it works

For each launch the script writes `profiles\<name>\launch.cmd`, then runs it inside the package context:

```
Invoke-CommandInDesktopPackage -PackageFamilyName OpenAI.Codex_2p2nqsd0c76g0 \
  -AppId App -Command cmd.exe -Args '/c "<profile>\launch.cmd"' -PreventBreakaway
```

The wrapper sets `CODEX_HOME`, `CODEX_ELECTRON_USER_DATA_PATH`, and `CODEX_CLI_PATH`, then starts `ChatGPT.exe` with its own `--user-data-dir`. That gives each window its own account, cookies, and window state while all of them share the history database.

## Roadmap

- Claude desktop support. The app is also an MSIX package, so the launch path is the same; the open question is whether its login survives a separate `--user-data-dir` without sharing Windows Credential Manager entries. Needs testing with two real accounts before it ships.
- Other Electron-based AI desktop apps once their profile isolation is verified.

## Notes

- Sign in to one profile at a time. Codex uses a fixed local callback port during login.
- Never copy `auth.json` between profiles.
- MCP OAuth credentials are forced to `file` storage per profile, so MCP accounts do not bleed through Windows Credential Manager.
- This is an unsupported packaging workaround. A Store update can change the launch behavior; re-test after updates.
- Profiles are isolation, not a security sandbox. Anything running as your Windows user can read the tokens.
- Use only accounts you own. Using multiple accounts to avoid usage limits may violate OpenAI's terms.

## License

MIT. See `LICENSE`.
