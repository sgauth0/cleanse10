# AGENTS.md — Cleanse10 Codebase Guide

This file is intended for agentic coding assistants operating in this repository.

---

## Project Overview

Cleanse10 is a Windows-only .NET 9 application for offline-servicing Windows 10 ISOs (debloat, tweaks, driver injection, unattend.xml). It consists of three projects in `Cleanse10.sln`:

| Project | Type | Purpose |
|---|---|---|
| `Cleanse10.Core` | Class library | All business logic (DISM, registry, presets, tweaks) |
| `Cleanse10.GUI` | WPF application | MVVM desktop UI |
| `Cleanse10.CLI` | Console application | `System.CommandLine`-based CLI |

---

## Build Commands

```powershell
# Build entire solution
dotnet build Cleanse10.sln

# Build a specific project
dotnet build src\Cleanse10.Core\Cleanse10.Core.csproj
dotnet build src\Cleanse10.GUI\Cleanse10.GUI.csproj
dotnet build src\Cleanse10.CLI\Cleanse10.CLI.csproj

# Publish GUI (single-file, self-contained, win-x64) to publish\
dotnet publish src\Cleanse10.GUI\Cleanse10.GUI.csproj --configuration Release --output publish

# Publish CLI
dotnet publish src\Cleanse10.CLI\Cleanse10.CLI.csproj --configuration Release --output publish

# PowerShell publish helper (GUI + CLI together)
.\publish-gui.ps1
```

---

## Testing

There are **no automated test projects** in this solution. The only testing infrastructure is a PowerShell smoke-test that launches the published GUI and checks for crashes:

```powershell
# Smoke-test the published GUI (requires a prior publish)
.\test-launch.ps1
```

If adding unit tests, use **xUnit** (`xunit`, `xunit.runner.visualstudio`) and target `net9.0-windows`. To run a single test:

```powershell
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"
dotnet test --filter "DisplayName=MyTestMethodName"
```

---

## Linting / Formatting

There is no `.editorconfig` at the solution root, no Roslyn analyzer packages, and no StyleCop configured. Adhere to the conventions described below. If adding an `.editorconfig`, model it on the implicit style already in use.

---

## Target Framework & Platform

- **Framework:** `net9.0-windows` (all projects)
- **Platform:** `win-x64`, Windows-only — no cross-platform intent
- **Nullable reference types:** `enable` in all projects
- **Implicit usings:** `enable` in all projects
- **Self-contained publish:** `true` for GUI and CLI; `PublishTrimmed=false` on GUI (WPF reflection)

---

## Code Style Guidelines

### Namespaces & File Layout

- Namespace mirrors the directory path: `Cleanse10.Core.Bloat`, `Cleanse10.ViewModels`, etc.
- One public type per file; file name matches the type name.
- File-scoped namespaces (`namespace Foo;`) are preferred over block-scoped.

### Using Directives

- List `using` directives in this order (no blank lines between groups):
  1. `System.*` BCL namespaces
  2. `Microsoft.*` / other framework namespaces
  3. Project-internal namespaces (`Cleanse10.*`)
- Even though `ImplicitUsings` is enabled, explicit `using` statements are fine and common; be consistent within a file.
- Avoid inline fully-qualified names unless unavoidable (e.g., `System.Windows.Application.Current.Dispatcher.Invoke` can be replaced with a proper `using`).

### Naming Conventions

| Symbol | Convention | Example |
|---|---|---|
| Classes, methods, properties, enums | `PascalCase` | `TweakApplicator`, `ApplyAsync` |
| Private/protected fields | `_camelCase` | `_wimPath`, `_isBusy`, `_cts` |
| Local variables & parameters | `camelCase` | `imagePath`, `ct` |
| Constants | `PascalCase` | `FidoUrl`, `ClawBootstrapScript` |
| Async methods | Suffix `Async` | `MountImageAsync`, `RunPresetAsync` |

### Types

- Enable and respect `Nullable` annotations — use `?` explicitly on nullable reference types.
- Prefer `record` for immutable data transfer objects (`TweakDefinition`, `PresetDefinition`).
- Use `init`-only properties on data-holding records/classes.
- Expose collections as `IReadOnlyList<T>`; accept collections as `IEnumerable<T>` in method parameters.
- Use collection initializer expressions (`[]`) in new code targeting .NET 9.
- Prefer `string.Empty` over `""` for empty-string initialization.
- Use range/slice syntax where it improves readability (`fullKey[5..]`).

### Async Patterns

- All public async methods return `Task` or `Task<T>` — **no `async void`** except for event handlers.
- Every public async method that can be cancelled must accept `CancellationToken ct = default`.
- Use `ct.ThrowIfCancellationRequested()` at the top of loops.
- Wrap event-based `Process` completions with `TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)`.
- Register cancellation via `ct.Register(() => { p.Kill(); tcs.TrySetCanceled(ct); })`.
- Use `IProgress<string>?` (nullable) for all user-visible status reporting. Callers pass `null` if not interested.

### Error Handling

- Always catch `OperationCanceledException` **before** the general `Exception` catch.
- Always use `finally` to guarantee cleanup (hive unload, `CancellationTokenSource.Dispose`, temp file removal).
- On cancellation: attempt best-effort cleanup (e.g., DISM unmount with `/Discard`), then set the appropriate exit code or status.
- Silent empty catches (`catch { }`) are acceptable **only** for truly fire-and-forget operations (crash log writes, process kill in cleanup, temp file deletion). Always add a comment: `// best effort` or `// nowhere left to report`.
- Never silently swallow exceptions that affect correctness.

### Progress Reporting

- Use `[TAG]` prefixes consistently: `[Bloat]`, `[WimManager]`, `[Preset]`, `[ERR]`, `[WARN]`.
- Phase-progress messages follow the pattern: `[Preset] Phase X/7 — Description`.
- Report errors via `progress?.Report("[ERR] ...")` before re-throwing or returning.

### Process Invocation

- Wrap all external process calls (`dism.exe`, `reg.exe`, `oscdimg.exe`, `powershell.exe`) in an async helper that uses `TaskCompletionSource` — do not block with `.WaitForExit()`.
- Redirect `StandardOutput` and `StandardError`; surface both via `IProgress<string>`.
- Treat DISM exit code `3010` (reboot required) as success.
- Always pass `CancellationToken` support via `ct.Register` to kill the process on cancellation.
- Avoid duplicating the process-wrapping boilerplate — route through a shared utility when one exists.

### WPF / MVVM (GUI project only)

- All ViewModels inherit `ViewModelBase : INotifyPropertyChanged` and use `SetField<T>` with `[CallerMemberName]`.
- Commands are `RelayCommand` (non-generic) or `RelayCommand<T>` (generic).
- Gate UI interactions behind an `IsBusy` boolean property.
- UI thread marshaling: use `System.Windows.Application.Current.Dispatcher.Invoke(...)` inside `Progress<T>` callbacks.
- Settings are persisted to `%APPDATA%\Cleanse10\settings.json` via `System.Text.Json`.

### Section Comments

Use consistent section-break comments for logical groupings within a file:

```csharp
// ──────────────────────────────────────────────────────────────────────
// ── Section Name ───────────────────────────────────────────────────────
// ──────────────────────────────────────────────────────────────────────
```

---

## Architecture Notes

- `Cleanse10.Core` must remain UI-agnostic — no WPF references, no `Console.*` calls.
- `PresetRunner10` orchestrates a 7-phase pipeline; keep new phases consistent with the `[Preset] Phase X/7` progress prefix pattern.
- `HiveManager` is the canonical offline-registry abstraction; prefer it over ad-hoc `reg.exe load/unload` calls.
- The "Claw" preset writes a first-boot script to `C:\Windows\Setup\Scripts\ClawSetup.ps1` and a `RunOnce` registry key — operations that require an online session belong there, not in the offline phase.
- Both GUI and CLI are thin shells over `Cleanse10.Core`; keep business logic out of both.
