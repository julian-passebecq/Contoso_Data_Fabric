# Pro AI takeover — 11 September 2026

Start here. This branch preserves the complete desktop candidate plus previously uncommitted stabilization, tests and planning records. It is **not a finished or release-accepted app**.

The user is ending the costly multi-agent workflow. Continue with one Pro model, concise checkpoints and selective reading. The old medium-developer/light-tester/lead routing is historical, not a requirement to recreate that process. Decide implementation yourself; this handover states the outcomes and evidence still needed.

## Product and constraints

Deliver a dependable Windows WPF/.NET 8 Contoso SalesBi generator-to-report app: reusable `.fabric.json` configuration, local generation without credentials, read-only preflight, selectable contiguous Generate → Bronze → Silver → Gold → SemanticModel → Report ranges, trustworthy data, usable Direct Lake model/PBIR report, understandable failures and safe reruns.

Preserve the original C# generator and C# orchestration. Generated PySpark notebooks are part of Fabric execution. The prior user direction rejects replacing the app with React/Python or turning it into a DAX editor. Effective generator seed is 0; requested orders are a target, not an exact count. Local-currency measures must not add unlike currencies.

## Where we actually stopped

- Existing code covers the entire six-stage path, WPF project editing, preflight, REST/OneLake integration, notebook/TMDL/PBIR generation and contract tests. This is implementation coverage, not proof the cloud path works.
- Local stabilization fixes logger disposal, runs generation off the UI thread, serializes adapter calls, and adds initial JSON execution receipts plus lifetime/receipt tests. These were uncommitted until this handover.
- S01 was **planned, not executed or accepted**. There is no S01 developer completion handoff, independent test report or lead acceptance. S02–S04 are forecasts.
- Fresh local Release build on 11 September: **PASS, 0 warnings/errors**. Tests: **31 passed, 0 failed/skipped**. No WPF interaction, fresh real dataset generation, Spark execution, Fabric mutation or report rendering was performed during this handover.
- Prior real warm Parquet runs succeeded twice and reconciled 9,466 orders / 22,704 lines. The earlier log-lock failure was fixed; do not reopen it merely because the historical readiness audit says FAIL.
- Release blockers remain in data quality, publication isolation, local/remote dataset identity, transport bounds and evidence coverage. See [remaining outcomes](REMAINING.md).

## Read only what you need

1. **Essential (~5 minutes):** this file, [REMAINING.md](REMAINING.md), [GIT_AND_PROVENANCE.md](GIT_AND_PROVENANCE.md).
2. **Before changing a subsystem:** [architecture/invariants](../projectmanagement/ARCHITECTURE.md), [initial findings A01–A08](../projectmanagement/audits/2026-09-08-initial.md), and its source files. [TEST_PLAN.md](../projectmanagement/TEST_PLAN.md) supplies existing acceptance IDs; [BACKLOG.md](../projectmanagement/BACKLOG.md) preserves F00–F26.
3. **Only for deeper history:** [original pre-Codex handoff](evidence/ORIGINAL_HANDOFF_2026-09-08.md), historical sprint/process templates and stabilization notes. That original handoff contains stale status, optimistic implementation wording and old suggested instructions. Current evidence and the user's new single-model direction take precedence.

## Source map

| Area | Location |
| --- | --- |
| Original engine and format writers | `DatabaseGenerator/` |
| Project model, planner, adapter, truth manifest | `ContosoFabric.Core/` |
| Remote orchestration and receipts | `ContosoFabric.Fabric/Pipeline/` |
| REST catalog/auth and definition deployment | `ContosoFabric.Fabric/Api/` |
| Upload and generated transformations | `ContosoFabric.Fabric/OneLake/`, `Notebooks/` |
| Analytical metadata | `ContosoFabric.Fabric/SemanticModel/`, `Reports/` |
| Desktop and preflight UX | `ContosoFabric.Desktop/` |
| Current tests | `ContosoFabric.Core.Tests/` |

## Paste to the successor

> Take over Contoso_Data_Fabric from branch codex/pro-ai-handover-2026-09-11. Read handover/README.md, REMAINING.md and GIT_AND_PROVENANCE.md first; read deeper files only when needed. Preserve the existing native C# architecture and work. Use one model and concise durable checkpoints. Complete the remaining SalesBi v1 outcomes with your own implementation decisions. Distinguish code present, locally tested, live verified and release accepted. Do not repeat historical planning or treat S01 as completed. No live workspace has been designated; get the concrete test target before remote mutations.
