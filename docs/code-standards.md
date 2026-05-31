# Code Standards — Windows 11 Custom ISO Builder

## Overview

This document defines the naming, structure, and quality conventions for the Windows 11 Custom ISO Builder codebase. All developers and contributors must follow these standards to maintain consistency and readability.

---

## File & Class Naming

### C# Files (PascalCase)

All `.cs` files use **PascalCase** matching the primary public type name.

| Category | Naming | Example |
|----------|--------|---------|
| Model/POCO | `{Entity}.cs` | `BuildConfig.cs`, `WindowsEdition.cs`, `AppEntry.cs` |
| Service | `{Domain}Service.cs` | `IsoService.cs`, `WimService.cs`, `LogService.cs` |
| VM (ViewModel) | `{Step}ViewModel.cs` | `MainViewModel.cs`, `SelectIsoViewModel.cs`, `ReviewBuildViewModel.cs` |
| View (XAML) | `{Step}View.xaml` | `MainWindow.xaml`, `SelectIsoView.xaml`, `OfficeView.xaml` |
| Builder | `{Domain}Builder.cs` | `UnattendBuilder.cs`, `FirstBootScriptBuilder.cs` |
| Parser | `{Domain}Parser.cs` | `DismOutputParser.cs` |
| Runner | `{Domain}Runner.cs` or static | `HeadlessBuildRunner.cs` (static methods) |

### Model Properties (PascalCase)

All public properties in POCOs use **PascalCase**.

```csharp
// BuildConfig.cs
public string? SourceIsoPath { get; set; }
public string WorkDir { get; set; }
public WindowsEdition? SelectedEdition { get; set; }
public IList<AppEntry> SelectedApps { get; set; }
```

### Local Variables, Parameters (camelCase)

```csharp
var sourceIsoPath = cfg.SourceIsoPath;
var mediaDir = cfg.MediaDir;
await iso.ExtractIsoAsync(cfg.SourceIsoPath!, cfg.MediaDir, ct);
```

### Constants (PascalCase or UPPER_SNAKE_CASE)

- **Private/internal constants:** UPPER_SNAKE_CASE
- **Public constants:** PascalCase (rare in services)

```csharp
private const string DefaultWorkDir = "...";
private const int DefaultTimeoutMs = 300_000;
```

---

## File Organization & Size Limits

### Maximum File Size: 200 Lines of Code

**Rationale:** Cognitive load, context windows, easier testing.

Every `.cs` file should be ≤~200 LOC (excluding license headers, blank lines, and auto-generated code).

| File Type | Typical Size | Hard Limit |
|-----------|------|-----------|
| Service class | 80–120 LOC | 200 LOC |
| ViewModel | 60–100 LOC | 200 LOC |
| Model/POCO | 10–50 LOC | 200 LOC |
| Test class | 50–150 LOC | 250 LOC |

**If a file approaches 200 LOC:**
- Extract helper methods into separate static-utility classes
- Split large services into domain-specific smaller services
- Move complex parsing/building logic into dedicated builder/parser classes

Example: If `FirstBootScriptBuilder` grew beyond 200 LOC, extract script-template loading into `ScriptTemplateLoader` class.

### Folder Structure

```
Win11IsoBuilder/
├── Models/              (8 POCOs: all <50 LOC each)
├── Services/            (11 services: all <200 LOC each)
│   └── Dism/            (Parsers: all <100 LOC each)
├── ViewModels/          (7 VMs: all <150 LOC each)
├── Views/               (6 XAML + MainWindow)
└── Assets/              (JSON catalog, text templates)
```

---

## Naming Conventions by Domain

### Services

All services are **stateless** and **UI-agnostic**. They accept `BuildConfig` and `ILogSink`, perform work, and return results.

**Naming pattern:** `{Domain}Service.cs`

```csharp
public class IsoService { }           // ISO mount/extract/repack
public class WimService { }           // WIM mount/unmount/DISM
public class OfficeOdtService { }     // Office ODT configuration
public class AppCatalogService { }    // App catalog + installer acquisition
public class ProcessRunner { }        // Subprocess execution wrapper
public class LogService : ILogSink { }  // Logging implementation
```

### Builders

Builders generate artifacts (XML, scripts, configs). No side effects; pure construction.

```csharp
public class UnattendBuilder
{
    public XDocument Build() { }  // Returns XML (no file I/O)
}

public class FirstBootScriptBuilder
{
    public async Task<string> BuildSetupCompleteCmd() { }
    public async Task<string> BuildComputerNameScript() { }
}
```

### Parsers

Parsers extract structured data from unstructured text. **No subprocess calls.**

```csharp
public class DismOutputParser
{
    public static List<string> ParseProvisionedAppxList(string output) { }
    public static (int Index, string Name) ParseImageInfo(string output) { }
}
```

### ViewModels

Inherit from `WizardStepViewModel` base. Use **CommunityToolkit.Mvvm** attributes.

```csharp
public partial class SelectIsoViewModel : WizardStepViewModel
{
    [ObservableProperty] private string? selectedIsoPath;
    [RelayCommand] private async Task SelectFile() { }
}
```

---

## XML Artifact Construction

### Use XDocument (Not String Templates)

**All XML artifacts must be built via `XDocument`** to ensure well-formed, properly-escaped output.

✅ **Correct (autounattend.xml via XDocument):**

```csharp
var doc = new XDocument(
    new XDeclaration("1.0", "utf-8", null),
    new XElement("unattend",
        new XAttribute("xmlns", "urn:schemas-microsoft-com:unattend"),
        new XElement("settings", new XAttribute("pass", "specialize"),
            // ... elements
        )
    )
);
doc.Save(outputPath);
```

❌ **Wrong (string concatenation):**
```csharp
var xml = "<unattend><settings>" + userInput + "</settings></unattend>";
// Escaping bugs, injection risks, hard to read
```

### Shell Artifacts (Scripts) Use Text Templates

First-boot PowerShell and batch scripts are **text templates** in `Assets/`:

- `Assets/SetupComplete.cmd` — batch template with `{{PLACEHOLDER}}` substitutions
- `Assets/set-computername.ps1` — PowerShell template

At runtime, `FirstBootScriptBuilder` reads these templates, performs string substitutions (app list, serial, payload path), and writes to `$OEM$/1$` directory.

**Rationale:** Shell script syntax is complex to build dynamically; templates are clearer and safer.

---

## Logging & Observability

### ILogSink Interface

All services accept `ILogSink` for logging. No direct `Console.WriteLine()` or file I/O.

```csharp
public interface ILogSink
{
    void Log(LogLevel level, string message);
    event EventHandler<LogEntry>? EntryLogged;
}
```

### Service Constructor Pattern

```csharp
public class IsoService
{
    private readonly ProcessRunner _runner;
    private readonly ILogSink _log;

    public IsoService(ProcessRunner runner, ILogSink log)
    {
        _runner = runner;
        _log = log;
    }

    public async Task ExtractIsoAsync(string sourcePath, string destDir, CancellationToken ct)
    {
        _log.Log(LogLevel.Info, $"Extracting ISO: {sourcePath}");
        // ... work ...
        _log.Log(LogLevel.Info, "Extraction complete");
    }
}
```

### LogService Implementation

`LogService` implements `ILogSink` and provides:
- File logging (WorkDir/Logs/)
- Event emission (`EntryLogged` event for UI binding)
- Level filtering (Info, Warn, Error)

---

## Resource Management & Cleanup

### WIM Mount: Always Unmount in Finally

The WIM mount is a critical resource. It **must** be released even on error.

✅ **Correct (try/finally pattern):**

```csharp
private bool _wimMounted = false;

public async Task MountAsync(string wimPath, int index, string mountDir, CancellationToken ct)
{
    try
    {
        // Mount via DISM
        await _runner.RunAsync("dism", new[] { "/Mount-Wim", ... }, ..., ct);
        _wimMounted = true;
    }
    catch
    {
        throw;
    }
}

public async Task UnmountAsync(CancellationToken ct, bool discard = false)
{
    if (!_wimMounted) return;
    try
    {
        var args = discard ? "/Unmount-Wim /Discard" : "/Unmount-Wim /Commit";
        await _runner.RunAsync("dism", new[] { args, ... }, ..., ct);
    }
    finally
    {
        _wimMounted = false;
    }
}
```

**In BuildOrchestrator:**

```csharp
try
{
    wim = new WimService(_runner, _log, cfg.Tools.DismPath!);
    await wim.MountAsync(wimPath, index, cfg.MountDir, ct);
    mounted = true;
    // ... work on mounted WIM ...
}
catch
{
    // On error, unmount with /Discard to avoid stuck mount
    if (mounted && wim != null) await wim.UnmountAsync(ct, discard: true);
    throw;
}
finally
{
    if (mounted && wim != null) await wim.UnmountAsync(ct, discard: false);
}
```

---

## Code Comments: Focus on WHY, Not What

### DO Comment
- **Invariants:** "WIM must be unmounted in finally to avoid stuck mount."
- **Race conditions:** "Poll for drive letter; USB mount takes ~100ms."
- **Non-obvious logic:** "LabConfig keys bypass TPM check in Setup; community-validated."
- **Workarounds:** "ESD export collapses all editions to index 1; adjust caller's index."

### DON'T Comment
- **Self-evident code:** `var mediaDir = cfg.MediaDir;` (obvious)
- **Plan references:** "Per F13, fix advisory-lock issue" (use commit message instead)
- **Pseudo-documentation:** "TODO: fix this later" (too vague; use GitHub Issues)

### Example

```csharp
// Mount always unmounts in finally to avoid stuck WIM (resource leak).
// On error, /Discard prevents corruption; on success, /Commit saves changes.
public async Task UnmountAsync(CancellationToken ct, bool discard = false)
{
    if (!_wimMounted) return;
    try
    {
        var args = discard ? "/Unmount-Wim /Discard" : "/Unmount-Wim /Commit";
        await _runner.RunAsync("dism", $"... {args} ...", ..., ct);
    }
    finally
    {
        _wimMounted = false;  // Clear flag even if DISM fails (cleanup will retry).
    }
}
```

---

## Contract: BuildConfig as Single Source of Truth

### Pattern

Every service receives `BuildConfig` as the **primary input**. Services are **not** allowed to maintain parallel state.

```csharp
public class WimService
{
    public async Task<(string WimPath, int Index)> EnsureEditableWimAsync(
        string sourcesDir,
        int requestedEditionIndex,
        CancellationToken ct)
    {
        // Derived paths computed from sourcesDir only; no side effects.
        var wimPath = Path.Combine(sourcesDir, "install.wim");
        var esdPath = Path.Combine(sourcesDir, "install.esd");

        if (File.Exists(wimPath)) return (wimPath, requestedEditionIndex);

        if (!File.Exists(esdPath))
            throw new FileNotFoundException("install.wim or install.esd not found");

        // Export ESD; return adjusted index.
        return await ExportEsdAsync(esdPath, wimPath, requestedEditionIndex, ct);
    }
}
```

### Callers Update BuildConfig

```csharp
// In BuildOrchestrator
var (wimPath, index) = await wim.EnsureEditableWimAsync(
    Path.Combine(cfg.MediaDir, "sources"),
    cfg.SelectedEdition?.Index ?? 1,
    ct);
cfg.SelectedEdition = new WindowsEdition { Index = index, Name = "..." };
```

---

## Testing Approach

### Unit Tests (xUnit, 35 tests, all passing)

#### Test Doubles (No Real Subprocess, Filesystem)

Create `TestDoubles.cs` with mock implementations:

```csharp
public class MockProcessRunner : ProcessRunner
{
    private readonly Dictionary<string, string> _outputs = new();

    public MockProcessRunner WithOutput(string args, string output)
    {
        _outputs[args] = output;
        return this;
    }

    public override async Task<ProcessResult> RunAsync(...)
    {
        if (_outputs.TryGetValue(args, out var output))
            return new ProcessResult { ExitCode = 0, StdOut = output };
        throw new InvalidOperationException($"Unexpected subprocess: {args}");
    }
}

public class MockLogSink : ILogSink
{
    public List<LogEntry> Entries { get; } = new();
    public void Log(LogLevel level, string message) => Entries.Add(new LogEntry(level, message));
    public event EventHandler<LogEntry>? EntryLogged;
}
```

#### Builder Tests (Pure Construction)

```csharp
[Fact]
public void UnattendBuilder_GeneratesLabConfigBypassKeys()
{
    var builder = new UnattendBuilder
    {
        BypassTpm = true,
        BypassSecureBoot = true
    };

    var doc = builder.Build();
    var labConfigKey = doc.Root?.Descendants("LabConfig").FirstOrDefault();

    Assert.NotNull(labConfigKey);
    Assert.Equal("1", labConfigKey?.Attribute("BypassTPMCheck")?.Value);
}
```

#### Parser Tests (Sample Output)

```csharp
[Fact]
public void DismOutputParser_ParsesProvisionedAppxList()
{
    var output = """
        Package Identity : Microsoft.BingNews_1.0.0_x64__8wekyb3d8bbwe
        Package Identity : Microsoft.BingWeather_1.0.0_x64__8wekyb3d8bbwe
        """;

    var packages = DismOutputParser.ParseProvisionedAppxList(output);

    Assert.Equal(2, packages.Count);
    Assert.Contains("Microsoft.BingNews", packages[0]);
}
```

### Acceptance Tests (Manual Playbook)

AC-1..AC-6 are validated manually via [`vm-smoke-test-playbook.md`](vm-smoke-test-playbook.md):
- Build a real ISO from a Windows 11 25H2 source
- Boot in Hyper-V/VirtualBox
- Verify unattended install, local account, serial→computer name, offline apps, Office sign-in

---

## CLI Argument Parsing (Headless Mode)

### Command-Line Interface

```
Win11IsoBuilder.exe --build --iso <path> --out <dir>
                    [--name <filename.iso>]
                    [--edition N]
                    [--debloat "Package1,Package2"]
                    [--office]
                    [--apps "app1,app2,app3"]
```

### Parsing Convention

- **Flags:** `--flag value` (space-separated)
- **Commas:** For list values (no spaces)
- **Default edition:** Auto-select Pro SKU (index 1) if `--edition` omitted
- **Default output name:** "Win11-Custom.iso"

**Implementation:** `HeadlessBuildRunner.Parse(string[] args)` uses simple string parsing (not a heavy CLI library).

---

## Async/Await Patterns

### Always Use ConfigureAwait(false)

In services (non-UI), always append `.ConfigureAwait(false)` to avoid UI thread marshaling.

```csharp
// Service layer (no UI affinity)
await _runner.RunAsync(...).ConfigureAwait(false);
await iso.ExtractIsoAsync(...).ConfigureAwait(false);
```

### UI Layer (ViewModel) Uses Default Context

In ViewModels, **omit** `.ConfigureAwait(false)` to preserve UI thread context:

```csharp
// ViewModel (UI thread needed)
await _orchestrator.RunAsync(cfg, progress, ct);  // No ConfigureAwait
// Completion fires on UI thread
CanProceed = true;
```

### CancellationToken Required for Long Ops

All long-running operations accept `CancellationToken ct` for graceful cancellation.

```csharp
public async Task ExtractIsoAsync(string source, string dest, CancellationToken ct)
{
    await _runner.RunAsync("robocopy", args, timeoutMs: 600_000, ct);
}
```

---

## Error Handling

### Exceptions Over Return Codes

Throw **specific exceptions** for error conditions. Services don't return `(success, error)` tuples.

```csharp
// ✅ Correct
if (!File.Exists(wimPath))
    throw new FileNotFoundException($"WIM not found: {wimPath}");

// ❌ Avoid
return new Result { Success = false, Error = "WIM not found" };
```

### ProcessRunner Exit Code Mapping

`ProcessRunner` throws on non-zero exit:

```csharp
var result = await _runner.RunAsync("dism", args, ..., ct);
if (result.ExitCode != 0)
    throw new InvalidOperationException($"DISM failed: {result.StdErr}");
```

### Cleanup on Exception

Ensure resources are released even on error:

```csharp
try
{
    await wim.MountAsync(..., ct);
    await wim.GetProvisionedAppxAsync/RemoveProvisionedAppxAsync(..., ct);
}
catch
{
    await wim.UnmountAsync(ct, discard: true);  // Cleanup before throwing
    throw;
}
```

---

## No Plan/Audit References in Code

### DO NOT Reference Plan Artifacts in Code

❌ **Wrong:**
```csharp
// Per F13 advisory-lock fix (phase 2)
await retry.WithBackoffAsync(...);

// In test name: TestAppInstall_PerFinding_SHA256Verify
[Fact] public void TestAppInstall_F7_SHA256Verify() { }
```

✅ **Correct:**
```csharp
// Retry with backoff: USB boot files sometimes transiently locked during cleanup.
await retry.WithBackoffAsync(...);

[Fact]
public void AppCatalogService_VerifiesDownloadedAppSha256()
{
    // Test that SHA-256 hashes are validated before staging
}
```

**Rationale:** Code lives longer than plans. Plans are renamed/archived; code references become unresolvable noise.

**Allowed code references:** File names, variable names, function signatures in the same codebase. Stable external IDs: RFC numbers, CVE IDs, Microsoft SQLSTATE codes.

---

## Public Repository Standards

Since the repo is **public** (github.com/vqtuan90/Win11IsoBuilder):

1. **No secrets in code** — No API keys, credentials, or paths to local machines.
2. **No bundled large files** — `tools/` is gitignored; document where to source them.
3. **Clear README** — Explain build, test, run; link to docs.
4. **CI passing** — GitHub Actions must pass on every commit to main.
5. **License** — Ensure all dependencies and redistributables are license-compliant (oscdimg, ODT are Microsoft tools; document terms).

---

## Summary Checklist

- [ ] File < 200 LOC (split if needed)
- [ ] PascalCase for C# type names; camelCase for locals
- [ ] All XML via XDocument (never string concat)
- [ ] All shell scripts as text templates (Assets/)
- [ ] Services accept ILogSink; no direct Console/File I/O
- [ ] WIM mount in try/finally; always unmount
- [ ] Comments explain WHY, not WHAT
- [ ] No plan references (F1, phase-2, audit-A4) in code
- [ ] All tests use test doubles; no real subprocess/file I/O
- [ ] Async methods use ConfigureAwait(false) in services; default in UI
- [ ] Exceptions for errors; no (bool, error) tuples
- [ ] Public repo: no secrets, document bundled tools, CI passing

---

**Last Updated:** 2026-05-31 | **Status:** Current | **Authority:** Development Lead
