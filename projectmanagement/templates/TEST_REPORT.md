# <SPRINT> independent test report

State: TEST_IN_PROGRESS | DEV_FIX_REQUIRED | READY_FOR_TECH_LEAD | TECH_LEAD_REQUIRED
Tested by: <role/model>. Date: <UTC>.
Candidate: <branch, full HEAD, diff base, dirty scope/hashes>.
Candidate remained frozen: <yes/no; any changes and invalidated tests>.
Environment: <OS, SDK/runtime, available services and missing prerequisites>.

| Test ID | Expected oracle | Exact command/steps | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| <ID> | <independent expected value/forbidden effect> | <steps> | <actual> | NOT_RUN | <path> |

Independent code/fixture review: <files inspected; counterexamples; assertions checked>.
Build/test/smoke counts: <measured; separate previous reports from checks executed now>.
UI/live/cloud evidence: <executed checks or explicit BLOCKED/NOT_RUN>.

| Defect | Severity | Reproduction | Expected vs actual | Code/evidence | Owner / route |
| --- | --- | --- | --- | --- | --- |
| D-<SPRINT>-001 | <P0–P3> | <minimal steps> | <difference> | <path> | <developer/lead> |

Registers updated: <backlog, branches, candidate/test ledger, status>.
Residual risk and missing evidence: <never convert missing into pass>.
Recommendation: <return to developer / request lead decision / ready for lead acceptance>.
Next actor and exact copyable prompt: <reference actual handoff/report and defect IDs>.
This report recommends; only the tech lead accepts the sprint.
