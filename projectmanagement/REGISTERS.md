# Branch, candidate, and test registers

Custodian: light tester. Snapshot date: 2026-09-08. These are local Git observations;
remote-tracking refs were not fetched and do not prove current remote/PR/CI status.

| Ref / candidate | Observed revision | Role / relation | Test and review status | Next action |
| --- | --- | --- | --- | --- |
| feat/csharp-fabric-desktop-v1 | 47a65b7 | Current desktop implementation; 84 commits ahead of origin/main | Dirty candidate build and 31 tests pass; no sprint acceptance | Develop S01 on preserved candidate |
| origin/feat/csharp-fabric-desktop-v1 | 47a65b7 | Tracking snapshot of current branch | No current remote CI inspected | Record actual CI if later available |
| main / origin/main | eaeb57a | Original generator baseline; merge base with desktop branch | Not tested in this session | Do not base desktop sprint on this by accident |
| origin/feat/fabric-pipeline-builder-v1 | e3c408e | Divergent earlier pipeline branch; 1 exclusive commit vs 84 on current branch | Not reviewed/tested here | Inventory/reconcile before any future merge; do not merge blindly |
| upstream/main | eaeb57a | Upstream tracking snapshot | Not tested here | Reference only |
| upstream/dev | f9bf6b8 | Upstream development snapshot | Not tested here | Reference only |
| codex/s02-data-contracts | NOT_CREATED | Proposed later branch from accepted S01 desktop line | No code/tests | Lead authorizes after S01 acceptance |

Initial working-tree changes that predate this planning task:

- Modified: `.gitignore`, `ContosoFabric.Core/Generation/LegacyGeneratorAdapter.cs`,
  `ContosoFabric.Desktop/MainWindow.xaml.cs`, `ContosoFabric.Fabric/Pipeline/FabricPipelineRunner.cs`,
  `DatabaseGenerator/Engine.cs`, `DatabaseGenerator/Logger.cs`, `README.md`.
- Untracked: `ContosoFabric.Core.Tests/GeneratorLifetimeTests.cs`,
  `ContosoFabric.Core.Tests/PipelineRunReceiptTests.cs`,
  `ContosoFabric.Fabric/Pipeline/PipelineRunReceipt.cs`,
  `docs/GENERATION_STABILIZATION_2026-09-08.md`, `docs/READINESS_AUDIT_2026-09-08.md`.
- Added by this planning task: root AGENTS.md and projectmanagement/ documents/evidence.
  No application source was modified by this planning task.

Candidate register (append, do not overwrite):

| Candidate ID | Revision identity | Kind | Result | Evidence |
| --- | --- | --- | --- | --- |
| B00 | 47a65b7 + original dirty files; see source-hashes.json | Initial lead baseline | Build PASS; tests 31/31 | evidence/BASELINE.md |
| S01-dev | NOT_YET_CREATED | Integrated development handoff | NOT_RUN | Future handoffs/S01-dev.md |
| S01-test | NOT_YET_CREATED | Independent acceptance candidate | NOT_RUN | Future reports/S01-test.md |

Test ledger:

| Run ID | Candidate | Check | Result / count | Provenance |
| --- | --- | --- | --- | --- |
| B00-build | B00 | Release full solution build, no restore | PASS, 0 warnings/errors | Executed by lead 2026-09-08 |
| B00-unit | B00 | Current xUnit suite | PASS, 31/31 cases, 25 methods | Executed by lead; inventory independently collected by light agent |
| H00-smoke | Prior stabilization working tree, exact hash inventory absent | Two warm real Tiny Parquet runs | Reported PASS | Historical docs; not rerun now |
| S01-matrix | Future S01 candidate | T01–T13 | NOT_RUN | Independent tester to populate |
| WPF-live | No candidate verified | Interactive desktop | NOT_RUN | Required S01 T11 evidence |
| Fabric-live | No candidate verified | T30–T33 | BLOCKED_EXTERNAL / NOT_RUN | Azure CLI absent on local PATH; no designated workspace |

Coverage inventory at B00: planner 15 cases; definitions 3; receipts 5; project files 2;
manifest 2; lifetime 2; notebooks 2. Notebook cases assert generated-source contracts.
There are no executed runner/transport/uploader tests, no actual Spark execution tests,
and no configured coverage collector/threshold. Test count is not a correctness target.

Environment notes: light inventory observed SDK 9.0.101 and .NET 8 runtime 8.0.11;
project target is .NET 8 and CI uses 8.0.x. No global.json. Azure CLI was not found on PATH.
The PowerShell login profile emitted unrelated Conda missing-module errors; using a shell
without the profile allowed normal repository commands. Do not repair the user's Conda
environment as part of this project sprint.

For each later test candidate record: branch, full HEAD, merge/diff base, clean/dirty status,
source hash inventory if dirty, changed-file scope, date, test command, expected/actual,
result counts, evidence path, owner, open defect IDs, and whether remote CI was observed.
Record any new branch and its purpose here; do not infer branch completion from its name.
