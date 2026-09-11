# Generation stabilization — 2026-09-08

This change addresses the log-file blocker reproduced in READINESS_AUDIT_2026-09-08.md.
That report is historical evidence from before this fix.

## Changes

- DatabaseGenerator.Logger now closes and releases its writer; Engine.Exec invokes Close in finally, including failure paths. Reinitialization disposes an earlier writer and resets elapsed-log timing.
- LegacyGeneratorAdapter runs preparation/generation on a worker thread. A process-wide semaphore serializes adapter runs through manifest creation so the shared upstream logger is not reused concurrently.
- Cancellation never abandons an active engine or releases its semaphore early. A queued run can be cancelled; an active engine finishes before cancellation is reported. Its cancelled run does not publish a manifest or start downstream stages.
- Generate and Run selected range create per-run JSON receipts under generated/runs/<run-id>/receipt.json. The activity log shows the path on success, failure and cancellation.
- Receipts record project/workspace target, selected range, timestamps, outcome, last stage, stage transitions, returned provisioning objects, completed job identifiers/root activity IDs, and error type/HTTP status. Earlier completed jobs and returned provisioning are retained if a later stage fails.
- Raw exception messages, progress messages and API response bodies are excluded from receipts. Final receipt-write failures preserve the original pipeline exception; a successful deployment instead returns a diagnostic warning, so it is not incorrectly reported as a failed deployment.
- Local generated data/cache/receipts are ignored by Git.

## Verification

Release build: 0 warnings, 0 errors. xUnit: 31/31 passed.

New tests cover reading/reusing real generator logs, log release after an engine failure, receipt state/identifiers, failure/cancellation serialization, omission of arbitrary error text, and receipt-write failures preserving the pipeline outcome.

Two actual Tiny / SalesBi / one-year / Parquet runs used the same process, output folder and existing reference cache:

| Run | Elapsed including manifest/receipt and row-count checks | Actual orders | Actual order rows |
| --- | --- | --- | --- |
| 1 | 46.76 seconds | 9,466 | 22,704 |
| 2 | 35.16 seconds | 9,466 | 22,704 |

Both completed through the pipeline runner, produced truth_manifest.json and a completed receipt, and matched manifest counts against the orders/orderrows Parquet row-group metadata. The original 10,000-order input is a generation target; the upstream daily weighting/randomness produces an actual count that may differ. These checks establish row-count reconciliation, not byte-for-byte reproducibility.

The initial failing cold run took 178 seconds including downloads/reference setup. These warm measurements are local generation times, not Fabric runtime estimates.

Temporary smoke-test evidence is in C:/Users/julia/AppData/Local/Temp/contoso-audit-20260908/fixed-run.log; the harness is Program.cs in that directory.

## Remaining limits and next test

Receipts are saved at start and normal finalization, not at every progress update. A crash can leave a running receipt without the final events. Item IDs are recorded after Prepare returns; partial provisioning failures and the currently failed notebook job may not yet expose their IDs. Separate Prepare/Upload actions are not wrapped in receipts yet.

Desktop generation has moved off its UI thread, but interactive WPF behavior has not been manually verified. Immediate cancellation inside the original engine is not implemented. Live Fabric behavior remains unverified.

Next: authenticate Azure CLI, select a dedicated capacity-backed workspace, and run Tiny Generate→Bronze, Silver→Gold, then SemanticModel→Report. Verify tables, reconciliation gates, model relationships/measures and report rendering, followed by a complete rerun. Use the saved receipts to guide any service-specific corrections.
