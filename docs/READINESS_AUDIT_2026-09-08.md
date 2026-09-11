# App readiness audit — 2026-09-08

Audited branch: feat/csharp-fabric-desktop-v1, commit 47a65b75e0ec126c0c8a0372a1c27709659aa317. The checkout was originally on main; switched to the existing desktop branch. No application source was changed. The supplied handoff was treated as background, not as authorization to implement its backlog.

## Executed checks

- Full solution restore: PASS.
- Release build: PASS, 0 warnings, 0 errors; MSBuild elapsed 12.24 seconds.
- Existing xUnit suite: PASS, 24/24; test-run elapsed 5.626 seconds.
- Local Generate-only preflight: PASS.
- Actual LegacyGeneratorAdapter execution: FAIL after 178.33 seconds, with Tiny / 10,000 requested orders / one year / Parquet / clean reference cache.
- Generator itself reached THE END: 9,466 actual orders, 22,704 order rows. The requested order parameter is a target; the original generator applies daily weighting/randomness.
- WPF interactive behavior and live Fabric execution: NOT TESTED.

## Release blocker reproduced

GenerationManifest.FromGeneratorLogAsync uses File.ReadAllTextAsync at ContosoFabric.Core/Generation/GenerationManifest.cs:37. DatabaseGenerator/Logger.cs keeps a static StreamWriter open after Engine.Exec completes. On Windows the manifest read fails with IOException: the process cannot access _log.log because it is being used by another process. No truth_manifest.json is produced. This blocks a normal Generate-to-Fabric run even though contract tests are green.

First repair should address logger lifetime and repeated runs, not just suppress the exception. Add regression coverage with an actual open writer and two consecutive generation runs. Confirm manifest counts against the emitted Parquet files.

## Other findings

- Azure CLI was not found in PATH or the standard CLI2 installation paths. FabricRestClient defaults to AzureCliCredential. No target test workspace was provided, so tenant access and capacity were not verified.
- Existing tests cover planning, project persistence and generated contracts. They do not exercise actual generation, Fabric transport/runner behavior, service schema acceptance or report rendering.
- Generation cancellation is only checked before and after Engine.Exec. Synchronous reference downloads/preparation are called from the WPF UI path; this presents a responsiveness risk that needs an interactive test.
- The cold run processed 2,099,808 reference customer records. Its 178-second measurement includes reference download/setup and the final failure; it is not a successful end-to-end pipeline runtime.
- Structured persisted run receipts remain absent. Capture stages, timing, item/job IDs, safe errors and outcomes to make tenant failures diagnosable.

## Recommended sequence and planning estimate

These are engineering estimates for one developer, not measured delivery guarantees. Access delays are excluded.

1. Repair local generation/log lifetime; add real generation and rerun regression coverage: 0.5–1 working day.
2. Add durable diagnostics and verify desktop responsiveness/cancellation: 1–2 working days.
3. Configure Azure CLI sign-in and a dedicated capacity-backed test workspace. Run Tiny Generate→Bronze, Silver→Gold, SemanticModel→Report, then full Generate→Report and a rerun. Validate actual counts, notebook outcomes, model relationships/measures and rendered report currency behavior: 1–3 working days including service-driven fixes.
4. Broaden to CSV/Delta, partial ranges, failures/retries and packaging/cleanup as needed: another 3–5 working days.

Provisional target: a validated SalesBi demo in 3–6 working days after access is ready; a more dependable v1 in roughly 1–2 working weeks. Re-estimate after the first successful Gold/model/report deployment. Additional scenarios and provisioning are outside this estimate.

## Evidence

Temporary harness and captured generator output:
C:/Users/julia/AppData/Local/Temp/contoso-audit-20260908/Program.cs
C:/Users/julia/AppData/Local/Temp/contoso-audit-20260908/run.log
C:/Users/julia/AppData/Local/Temp/contoso-audit-20260908/bin/Release/net8.0/data

Build/test commands:
```
dotnet restore ContosoDGV2.sln
dotnet build ContosoDGV2.sln -c Release --no-restore
dotnet test ContosoFabric.Core.Tests/ContosoFabric.Core.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=normal"
```
