# Upstream PR review — 2026-09-24

Target: this fork at `a40f19122ca881d65418648b86fd324d32da3853` (RC2).
These decisions concern importing changes into this fork, not closing or approving
the authors' upstream PRs. All 12 open PRs were triaged from their diffs and relevant
callers. Only the selected changes received local build/regression validation.

| Upstream PR | Reviewed head | Decision for this fork | Reason |
| --- | --- | --- | --- |
| [528: plain-text copying and indentation](https://github.com/dnSpyEx/dnSpy/pull/528) | `22d2d57dbbe48e06adc559c2c893338732c3e6b5` | Import with corrections | Useful opt-in clipboard and indentation controls. Original export callback shares one mutable `Indenter` between parallel writers; create an independent one per output. |
| [524: saved debug launch options](https://github.com/dnSpyEx/dnSpy/pull/524) | `0fc303c5ab48f14ea4e10b211927e152f84fa082` | Import with correction | Persisting only `lastOptions` does not restore per-file options. The EXE initialization path creates fresh defaults, losing saved arguments. Persist and restore the filename key as well. Environment variables remain unpersisted as designed upstream. |
| [525: bulk tree-node removal](https://github.com/dnSpyEx/dnSpy/pull/525) | `aa6ca7b519d233dbe00833950d4c1a2cabcf8c12` | Defer | Promising, but adds about 240 lines of selection/focus logic and a public interface method. `GetSelectionAfterRemove` repeatedly uses `IndexOf` and `RemoveRange`, retaining quadratic work in large removals. Require deletion/undo/selection tests and a representative benchmark before adopting. No measured speed claim was verified here. |
| [522: Linux console build](https://github.com/dnSpyEx/dnSpy/pull/522) | `e3e4b82ab394babaf7a43eef2087f4c21314b842` | Defer | Expands target frameworks across shared libraries and adds a separate RESX implementation. The proposed CI cross-builds on Windows but does not execute the result on Linux. Needs a Linux smoke test for CLI startup, bundle decompilation and resource export. |
| [514: sequence-point preservation/display](https://github.com/dnSpyEx/dnSpy/pull/514) | `58bd764673ec46ba1aaf7559107338ff26231d6c` | Defer for correction | `CilBodyVM.CopyTo` constructs new instructions and locals but assigns the original `PdbMethod` directly. PDB scope boundaries still refer to old instructions. Requires scope/local/custom-debug-info remapping and a save/reload PDB regression before preserving the whole object. The display-only portion could be separated. |
| [500: reload before F5](https://github.com/dnSpyEx/dnSpy/pull/500) | `690ccf633194d324dec34fb92efedabaa5d14209` | Reject current implementation | Discards the supplied `IDsDocumentLoader`, ignores `Reload()` returning false, and reloads before the debug dialog is accepted. Needs an explicit reload policy and correct cancellation handling. |
| [453: numeric tooltips](https://github.com/dnSpyEx/dnSpy/pull/453) | `074333cec033253a547714c9a233f79ad199ab25` | Defer | Primarily a presentation preference: removes octal and replaces floating-point raw hex with little-endian byte strings. Preserve existing representations or make it configurable before importing; this is not a demonstrated performance fix. |
| [398: implemented-interface analyzer](https://github.com/dnSpyEx/dnSpy/pull/398) | `20fd62031fc910008ef57042ab6388031e0bea8e` | Defer for correction | Uses a where-used search to scan loaded modules. For a nested-private implementing type without explicit overrides, type-accessibility scoping restricts the search to the enclosing type, missing external interfaces. Walk implemented interfaces directly and test generic/inherited/default implementations. COM support is also explicitly absent. |
| [303: JIT attach signaling](https://github.com/dnSpyEx/dnSpy/pull/303) | `132344d40b575d76e620ccd7fd32c6b65d841675` | Reject current draft | Uses delayed, unawaited signaling and lacks a `finally` around attach/listener/handle cleanup. The author also identifies single-instance handle-transfer and attach-ordering problems. Too risky for the debugger lifecycle as submitted. |
| [114: inner exceptions](https://github.com/dnSpyEx/dnSpy/pull/114) | `07283c947b76208a6e754292c9f26da491992775` | Reject current draft | Recursive exception traversal has no cycle/depth guard, changes public debugger/metadata contracts, and special-cases nested debugger objects in lifetime handling. Also conflicts with current upstream. Needs a bounded data-model design first. |
| [113: keep last debug settings](https://github.com/dnSpyEx/dnSpy/pull/113) | `a25c5b764305947fd4c9d475db3118dd9b0d5c14` | Defer | Overlaps #524. Its fallback only uses the last settings when there is no per-file MRU entry, so selecting a previously debugged assembly can still switch settings despite the option. Requires a clearer policy/UI and interaction tests with persistence. |
| [49: alternate bundle implementation](https://github.com/dnSpyEx/dnSpy/pull/49) | `6d0f6095163c2b15506523ae804902b438757a86` | Reject wholesale import | Competes with this fork's existing bundle document/reader integration. Its parser allocates from unchecked entry counts and narrows 64-bit offsets/sizes to 32 bits; draft functionality is incomplete. Useful UI concepts can be considered separately without replacing the hardened reader. |

## Selected changes and validation

The four source commits from #528 and #524 are cherry-picked with original authors
and source hashes retained. Fork-specific corrections and tests are separate.

- `Tests/UpstreamReview` exercises independent/parallel output writers, tab/space
  formatting, executable/connection MRU restoration and cloning.
- Full Release solution build passed for .NET 10 and .NET Framework 4.8.
- All six new regression checks and all nine existing bundle checks passed on
  each runtime (30 successful test executions).
- Saving a tab also creates a fresh indenter per attempt, avoiding state left by
  a canceled or failed save.
- CI runs the new checks after building the .NET and .NET Framework variants.
- Clipboard UI, a full debugger restart/launch session, and UI appearance still
  need interactive smoke testing. A successful compile is not a substitute for it.

Run the tests after building the matching configuration:

```powershell
dotnet run --project Tests/UpstreamReview/UpstreamReview.Tests.csproj -c Release -f net10.0-windows
dotnet run --project Tests/UpstreamReview/UpstreamReview.Tests.csproj -c Release -f net48
```
