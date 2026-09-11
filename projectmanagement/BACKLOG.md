# Feature and defect backlog

Updated: 2026-09-08. Priority P0 blocks release safety; P1 blocks dependable v1;
P2 is later hardening; P3 is an unapproved expansion. Status DONE requires lead acceptance.
IMPLEMENTED_UNACCEPTED means code exists but the full gate has not passed.

| ID | Priority | Feature / reason | Status | Owner | Target / dependencies | Acceptance evidence |
| --- | --- | --- | --- | --- | --- | --- |
| F00 | P1 | Logger lifetime, worker-thread generation, initial receipts | IMPLEMENTED_UNACCEPTED | Medium; lead reviews | S01 inherited baseline | Existing 31 tests + real rerun + cancellation checks |
| F01 | P1 | Committed local dataset identity and isolation; avoid consuming a failed or overwritten run | READY | Medium | S01 P1; ADR-001 | T02–T05 |
| F02 | P1 | Complete project/input validation and truthful order/seed wording | READY | Medium | S01 P1 | T01, T04 |
| F03 | P1 | Injectable runner and transport boundaries; prove effect ordering | READY | Medium | S01 P2 | T06–T08 |
| F04 | P1 | Consistent unique item resolution; never pick an arbitrary target | READY | Medium | S01 P2 | T07 |
| F05 | P1 | Bounded HTTP/polling and explicit timeout/cancel/unknown outcomes | READY | Medium | S01 P2 | T08 |
| F06 | P1 | Durable action receipts with partial item/job evidence | READY | Medium | S01 P3 | T09–T10 |
| F07 | P1 | Responsive desktop and honest cancellation/recovery messages | READY | Medium | S01 P3–P4 | T05, T11 |
| F08 | P1 | Reusable local smoke harness and CI path/branch coverage | READY | Medium | S01 P4 | T12–T13 |
| F09 | P0 | Silver required-schema, null-key and conflicting-duplicate rules | PLANNED | Lead then medium | S02; F03 | T20, explicit business-key policy |
| F10 | P0 | Gold persisted-data validation, fact grain, Date and all aggregate checks | PLANNED | Lead then medium | S02; F09 | T21 |
| F11 | P0 | Safe publication and validated dependency reuse; existing readers must not silently consume partial failed output | DESIGN_REQUIRED | Lead | S02; ADR-004 refinement | Failed promotion / existing model / metadata-only start evidence |
| F12 | P0 | Dataset-specific remote landing and completion metadata | PLANNED | Medium after lead brief | S02; F01 | Interrupted upload and format/rerun cases |
| F13 | P1 | Real Tiny Generate→Report, service contracts and idempotent rerun | BLOCKED_EXTERNAL | Light evidence + medium fixes | S03; F09–F12; workspace/access | T30–T33 |
| F14 | P1 | Actual TMDL queries, currency behavior and rendered PBIR | PLANNED | Light + lead semantic audit | S03; F13 | T32–T33 |
| F15 | P2 | CSV/Delta full-path matrix and measured larger scales | PLANNED | Medium + light | S04; live Parquet accepted | T34; resource/runtime observations |
| F16 | P2 | Recovery UX, retention and owned-artifact cleanup | DESIGN_REQUIRED | Lead | S04; receipt/provenance contracts | Previewed ownership-safe recovery/cleanup tests |
| F17 | P1 | Clean-machine distribution and prerequisite guide | PLANNED | Medium + light | S04 | T35 |
| F18 | P3 | Explicit reporting currency conversion policy | PRODUCT_DECISION | User + lead | After SalesBi v1 | Rate-date / rounding / missing-rate test oracle |
| F19 | P3 | Richer multi-page analytical report templates | PRODUCT_DECISION | User + lead | After F14 | User-visible utility and rendered acceptance |
| F20 | P3 | Customer Experience scenario | PRODUCT_DECISION | User + lead | After v1 | Separate generator and business contract |
| F21 | P3 | Data Quality scenario | PRODUCT_DECISION | User + lead | After v1 | Controlled fault generation and detection oracle |
| F22 | P3 | ML Dissatisfaction scenario | PRODUCT_DECISION | User + lead | After v1 | Target meaning, leakage prevention, evaluation design |
| F23 | P3 | Workspace/capacity provisioning | PRODUCT_DECISION | User + lead | After v1 | Ownership, cost and teardown lifecycle |
| F24 | P2 | Legacy engine low-volume/date-range boundary behavior | INVESTIGATE | Medium evidence; lead policy | S01 P1 reproduce; change policy only with evidence | Orders below days, interval boundaries, no silent malformed keys |
| F25 | P2 | Reference input provenance and reproducibility across environments | PLANNED | Lead + medium | S01 record local inputs; later freeze external references | Pin/hash source fixtures and compare defined content, not timestamps |
| F26 | P1 | Complete supported SalesBi logic audit; avoid reviewing only newly changed code | PLANNED | Lead, with light evidence support | Across S01–S03; required before v1 acceptance | Explicit coverage of generator grain/calculations, format writers, transforms, orchestration and BI semantics |

Audit findings A01–A08 in the initial review map to these features. Add new defects as
`D-S01-001`, etc., with severity, reproduction, expected/actual behavior, evidence, owner,
linked feature and affected candidate. Keep failed tests open until retested; retain the
original failing evidence after closure. Do not overwrite historical test results.
