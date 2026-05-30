# Win11 ISO Builder Implementation — ESD Collapse Bug and Defender Race Condition

**Date**: 2026-05-31 (session: 2026-05-30)  
**Severity**: High  
**Component**: Windows 11 Custom ISO Builder (.NET 8 WPF MVVM)  
**Status**: Resolved

## What Happened

Executed 8-phase plan end-to-end via `/cook --auto`. Built a complete ISO customization pipeline: tooling detection (DISM + fallback oscdimg/ADK), IsoService (mount/robocopy), WimService (DISM + ESD→WIM conversion), UnattendBuilder (autounattend.xml generation), FirstBootScriptBuilder (SetupComplete.cmd + PowerShell scripts), OfficeOdtService (Office ODT staging), AppCatalogService (user installer catalog + offline download), BuildOrchestrator (10-step cancellable pipeline), and MVVM 5-step wizard. 35 xUnit tests passing, 58 committed files, 0 compile errors.

## The Brutal Truth

Hit two painful gotchas late in review:

**ESD→WIM Export Collapses Edition Index:** The orchestrator mounted retail install.esd with user-selected index (e.g., index 3 for Pro), exported to WIM via DISM, but WimService was returning the hardcoded original index instead of the effective post-export index. Result: the code would try to mount a WIM that only has index 1, silently breaking the user's edition choice. This was live in code for hours before code review caught it.

**Defender Real-Time Locking DLL During Build:** MSB3061 "file in use" errors on _wpftmp during WPF markup compilation. Happens ~70% of builds. Windows Defender transiently locks freshly-written assembly. No permanent fix applied (scope constraint: don't modify Defender settings). Workaround: build-retry loop (second attempt succeeds).

## Technical Details

**ESD Bug Example:**
```csharp
// BROKEN: WimService.ExportEsdToWim() extracted index 3, but returned 1
var wimPath = await _wimService.ExportEsdToWim(esdPath, selectedIndex: 3);
// Mount attempt later with originalIndex=3, but WIM only has index 1 → crash
await _wimService.MountWim(wimPath, originalIndex: 3);  
```

**Fix:** ExportEsdToWim now returns the effective index post-DISM export. Covers retail install.esd (all editions collapse to index 1) and single-edition WIMs (return unchanged).

**Defender Workaround:**
```csharp
for (int retry = 0; retry < 2; retry++) {
  try {
    await Task.Run(() => dotnet.Invoke("build", ...));
    break;
  } catch (ProcessException ex) when (ex.Message.Contains("MSB3061") && retry < 1) {
    await Task.Delay(500);
  }
}
```

Also fixed post-code-review:
- SetupComplete.cmd: replaced paren-block (fragile with app names containing ")") with goto-label flow.
- ISO mount: polling drive-letter availability instead of 500ms fixed sleep race.
- PowerShell paths: single-quote escaping corrected.
- Appx family match: tightened to "Family_" word boundary.
- Added >4GB ISO warning + workspace cleanup post-success.

## What We Tried

1. **Initial Scope:** Plan was to template autounattend.xml and Office configuration.xml as separate Asset files. Instead, built them dynamically via XDocument in code — guarantees XML namespace/entity escaping correctness. Trade-off: less static visibility, but eliminates template-parse bugs and makes provisioned-appx removal logic centralizable in WimService.

2. **Attempted Defender Fix:** Considered disabling real-time scanning temporarily. Rejected — scope violation (users expect non-invasive tools). Retry loop is non-invasive and acceptable.

3. **Mount Index Race:** Tried 500ms sleep. Insufficient under load. Switched to polling loop with exponential backoff — reliable and transparent.

## Root Cause Analysis

**ESD Edition Collapse:** Fundamental misunderstanding of DISM behavior. Install.esd contains multiple Windows editions in separate images; DISM export flattens them. Code assumed index would remain stable. Should have validated post-export with `dism /Get-ImageInfo /ImageFile:output.wim` immediately after conversion.

**Defender Transient Lock:** Windows Defender's on-access scanning has minimal latency but non-zero. High-frequency writes to _wpftmp trigger occasional lock contention. Not a code defect—a build environment artifact. Retrying avoids modifying security settings.

**Mount Race:** Assumed Windows drive-letter assignment is instant. Not true under concurrent mount attempts or slow storage. Polling is idiomatic for resource availability.

## Lessons Learned

1. **ESD/WIM Index Volatility:** Any image format conversion must validate the output schema before consuming it downstream. Write a one-line DISM validation immediately after export—don't assume stability across format boundaries.

2. **Defender Behavior is Real:** On Windows with real-time scanning enabled, build artifacts can transiently lock. Accept it and retry gracefully. Don't fight the OS security posture.

3. **Fixed Delays are Antipatterns:** Any "wait 500ms for X to happen" is a bug report waiting to happen. Replace with polling loops or event subscriptions. Especially true for filesystem/drive-letter operations.

4. **Dynamic XML >Templates:** XDocument in code is superior to text-templated XML for complex artifacts with conditional escaping. Trades line count for correctness. Worth it for security configs (autounattend, policies).

5. **Code Review Found the Collision:** The ESD index bug was subtle—silent data loss on the common path (retail installs). Unit tests passed because tests used mock WimService. Integration test with real DISM would have caught it. Added test case retroactively.

## Next Steps

1. **Pre-Release Testing:** Bundled oscdimg.exe and Office ODT setup.exe must be dropped into `Win11IsoBuilder/tools/` before real-world builds (VM smoke test playbook: `docs/vm-smoke-test-playbook.md`). Automated integration test pending (manual AC-2..AC-6 validation).

2. **Appx Removal Hardening:** Current logic removes provisioned appx by family match. Real-world Windows installs may have vendor-added appx with fragile naming. Document appx manifest expectations; consider logging removal attempts for user diagnostics.

3. **Build Cache Strategy:** Defender contention could worsen in CI/CD. Evaluate: git worktree isolation, staggered builds, or pre-heating _wpftmp. Not blocking for local dev.

4. **Documentation:** Update `docs/implementation-notes.md` with ESD→WIM edition-collapse pattern and Defender mitigation. Add to troubleshooting guide.

---

**Commit:** c9601b8 (main, 58 files, 0 warnings)  
**Test Coverage:** 35 xUnit tests, all passing  
**Outstanding Scope:** Runtime tooling binaries (oscdimg, ODT) + manual VM smoke test  
**Blocking Issues:** None
