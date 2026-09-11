# Verification plan and independent oracles

The developer writes meaningful regression tests during implementation. The light tester
independently checks that they prove the acceptance criteria and adds adversarial fixtures
or test-only assertions where needed. Production fixes return to the medium developer.
String matching generated code is useful contract coverage but cannot prove Spark semantics.

Baseline commands from repository root, PowerShell without the user's profile if needed:

```powershell
dotnet restore ContosoDGV2.sln
dotnet build ContosoDGV2.sln -c Release --no-restore
dotnet test ContosoFabric.Core.Tests/ContosoFabric.Core.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=normal"
```

For portable test result capture after S01 implementation:

```powershell
dotnet test ContosoFabric.Core.Tests/ContosoFabric.Core.Tests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=S01.trx" --results-directory generated/verification/S01
```

The S01 smoke command does not exist yet. P4 must create and document it in its handoff.
Do not invent a successful command or rely on a harness in another session's Temp directory.

Every row below needs PASS / FAIL / BLOCKED / NOT_RUN, tested revision, exact invocation
or steps, fixture parameters, observed result, expected oracle, and evidence location.
Use fake dependencies for local tests and forbidden-call assertions for failure gates.

| ID | S01 test | Method and required oracle |
| --- | --- | --- |
| T01 | Project validation and compatibility | Load both committed examples; round-trip fields; invalid enum integers/strings, null workspace, names, years, negative/oversized orders and reversed ranges fail before any effect. Generate-only needs no tenant. Separate F24 finding if existing low-count policy needs lead decision. |
| T02 | Commit only completed datasets | Controlled success, exception before/after file output, exception during manifest/descriptor save. Success descriptor points to complete matching inventory; all other attempts leave previous pointer and dataset bytes unchanged. Full runner never substitutes previous dataset for failed generation. |
| T03 | Immutable identity and tamper detection | Generate A, resolve A, commit B before A uploads: upload still reads A. Change/truncate/remove a file, change format/fingerprint, corrupt descriptor, unsafe path and reparse escape: reject before remote calls. Test metadata version handling. |
| T04 | Legacy and format inventory | v1 manifest/manual generated/data with all eight tables; CSV header-only/truncated file cases, Parquet file validity, Delta log plus referenced parts. Missing table, empty Delta directory and manifest-only folder must fail. Manual no-manifest success visibly reports reduced provenance. Corrupt current descriptor never triggers legacy fallback. |
| T05 | Lifetime and cancellation | Two adapter instances with controlled engine: queue cancellation never starts second engine; active cancellation holds generation gate until engine exits, produces no committed descriptor and starts no upload. Logs close after success/failure. Inject cancellation at transition boundaries, not just before method invocation. |
| T06 | Runner ordering and effect gates | All 21 valid stage ranges; assert exact ordered effect trace and dependency reuse. Fail each stage; assert later stages have zero calls. Test Prepare's deferred BI behavior and Upload's range guard. Generate-only uses no credential. |
| T07 | Identity and preflight | Zero/one/two same-name items; mixed type/case; paginated results and delayed create visibility. Duplicate target means no mutation. Missing required dependency fails. Preflight emits no POST/PUT/PATCH/DELETE or upload; caller cancellation propagates. |
| T08 | Transport and time bounds | Fake HTTP + fake clock + credential: success/accepted operation, missing headers, failed/cancelled/unknown status, malformed body, 429 then success/exhaustion, Retry-After date/seconds, hung token/request, total deadline and caller cancellation. No real multi-minute waits. Lost mutation response must not replay mutation. Foreign URL / pagination cycle fails without sending credentials there. |
| T09 | Receipts and partial effects | Read durable snapshot immediately after each effect/transition; fail second provisioning or current job, retain first item/current job ID+stage. Check action kind for all four actions, resolved workspace/dataset identity and final terminal state. Poll chatter stays bounded. |
| T10 | Receipt faults and sensitive text | Inject initial/checkpoint/final write failure, interrupted replacement and late event. No effect on initial failure; preserve original exception on pipeline failure; completed remote action retains completed outcome plus warning on final write failure. Scan serialized output for injected sentinel token/API-body/exception text: absent. Old/incomplete receipts never imply success. |
| T11 | Desktop interaction | Launch WPF; Generate Tiny twice; resize/move window during work; request cancel during generation; observe draining message, eventual cancelled state and control re-enable; cancel during simulated polling and see remote-unknown warning; failure identifies receipt. Project displayed/receipt must match captured inputs. Repeat a valid action after failure. Record actual interaction or BLOCKED. |
| T12 | Real generation smoke | Repository harness runs Tiny 10,000 requested / one year / Parquet twice in one process, using isolated outputs. Sum Parquet row-group rows independently; match actual orders/orderrows manifest counts, valid receipt and descriptor inventory. Counts may differ from request. Record exact effective config, input/cache provenance and elapsed times; do not assert universal 9,466/22,704 across unknown inputs. |
| T13 | Integration and CI configuration | Release solution build, all fast tests; inspect workflow trigger inclusion for sprint code/harness changes. Record local result separately from actual CI status. Check docs/commands and safe ignored output directories. No requirement to reach an arbitrary test-count or coverage percentage. |

Independent review procedure:

1. Verify handoff completeness and candidate identity before running tests. Retain the
   initial baseline differences. Do not accept tests run against a different commit.
2. Read changed code relevant to each row; identify at least one failing counterexample
   each for dataset commit, selected-stage order, transport retries and receipts.
3. Confirm the tests would catch those errors (explicit negative assertions or a controlled
   test-only fault). Avoid a sweeping mutation-testing tool project in this sprint.
4. Execute full fast suite once on the frozen candidate and focused/new checks as needed.
   Repeat only after relevant changes or flaky/unexplained results. Do not rerun an expensive
   smoke repeatedly to fill time.
5. Consolidate defects and unexecuted gates in reports/S01-test.md. Update registers/backlog
   with evidence. Use the workflow route; lead audit is mandatory at sprint completion.

Future gates, to be refined when their sprint is approved:

| ID | Gate | Required oracle |
| --- | --- | --- |
| T20 | Executed Silver fixtures | Missing one composite key cannot collapse distinct rows; null/conflicting keys fail; exact duplicates counted; original and cleaned counts reconcile. Execute generated transformation code in compatible Spark, not a rewritten imitation. |
| T21 | Executed Gold fixtures | Hand-calculated small dataset with two currencies, date orphan, dimension/fact duplicates, null/non-finite numbers, deliberately bad aggregates. Read persisted results independently; verify all published aggregates and data-quality outcomes. |
| T22 | Publication/landing failure | Interrupted upload and failed validation/promotion do not become a current successful dataset. Existing model and later metadata-only run behavior tested against the lead-approved promotion contract. |
| T30 | Live Bronze | Dedicated workspace; Tiny Generate→Bronze, truth counts and dataset identity checked against materialized tables; mismatch case blocks Silver. |
| T31 | Live Silver/Gold | Silver→Gold with source/quality/reconciliation evidence and deliberate bad input; no BI deployment after failure; inspect previously bound reader behavior. |
| T32 | Live model | SemanticModel→Report dependencies; inspect actual relationships including Date and evaluate hand-checked measures in zero/one/multiple currency contexts. Record service acceptance and query results. |
| T33 | Live report/full rerun | Render slicer, cards, trend, category/country visuals; filter interactions; full Generate→Report plus rerun retains intended item IDs and report binding; record failures and unknown remote jobs. |
| T34 | Format/scale/recovery | CSV/Parquet/Delta selected/full ranges, format switches, small/medium observed resource usage, failed/rerun/cancel scenarios. Define performance budgets from measurements. |
| T35 | Distribution | Clean supported Windows environment, documented prerequisites, launch/project load/local generation, authenticated live smoke when available. |

Live tests require a concrete designated workspace and authorized item scope. Do not run
against an arbitrary discovered workspace, install a capacity, or delete unrelated items.
This access gate is a missing task target, not a request to reconfirm already authorized work.
