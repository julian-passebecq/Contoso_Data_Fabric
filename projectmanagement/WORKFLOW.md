> **2026-09-11 takeover:** The user has ended the multi-agent sprint workflow for cost reasons. Start with [handover/README.md](../handover/README.md). The historical status/process below is retained as evidence; S01 remains unimplemented and unaccepted. The Pro successor owns continuation.

# Roles, autonomy, and handoffs

| Role | Owns | Must not substitute for |
| --- | --- | --- |
| Tech lead | Target architecture, invariants, priorities, detailed sprint scope, independent code-logic audit, acceptance and next sprint | Test evidence that was never executed |
| Medium developer | Production implementation, necessary regression tests, developer verification, migration notes, resumable checkpoints | Independent acceptance or architectural policy changes |
| Light tester | Independent tests, adversarial fixtures, focused diff inspection, reproduction, evidence summaries, branch/test/backlog upkeep | Lead judgment on business semantics or a production rewrite |
| User | Product direction, real tenant target/access, starting role sessions, feedback on pass size | Routine pass-by-pass supervision |

One sprint normally has three or four substantial development passes. Each pass should
deliver a coherent technical milestone, including its developer checks. A pass may take
several hours; hours are a planning envelope, not a quota. Do not stop because a nominal
duration elapsed, and do not pad work to consume time. S01 starts with roughly 2–5 hours
per pass as an unmeasured sizing assumption. Record actual active duration when known.

The developer executes P1 → P2 → P3 → P4 without waiting for “new pass.” At each boundary,
update a short checkpoint and continue. There is one independent test handoff after P4.
Necessary developer tests still run during implementation: delaying independent testing
does not mean carrying known build or logic failures across the whole sprint.

Use these states in STATUS.md:

| State | Next action |
| --- | --- |
| READY_FOR_DEV | Developer starts or resumes the active sprint |
| DEV_IN_PROGRESS | Developer continues remaining approved passes |
| READY_FOR_LIGHT_TEST | Light tester verifies the complete candidate |
| TEST_IN_PROGRESS | Tester runs the matrix and consolidates evidence |
| DEV_FIX_REQUIRED | Developer fixes the consolidated defect list, then returns to testing |
| READY_FOR_TECH_LEAD | Tester has enough evidence for lead acceptance review |
| TECH_LEAD_REQUIRED | A design conflict, serious risk, or ambiguous result needs lead judgment |
| BLOCKED_EXTERNAL | Required access/runtime/user input prevents a named gate; independent work may continue |
| ACCEPTED | Lead accepted the sprint; lead then creates the next approved sprint |

An unavailable required test is BLOCKED or NOT_RUN, never PASS. A blocked live gate need
not block S01, whose acceptance is local. Conversely, local success cannot close a live gate.

Escalation rules:

- Developer resolves ordinary implementation errors, missing test seams, and refactoring
  inside approved interfaces. Collect related questions rather than interrupting per file.
- Ask the lead when a fix changes data meaning, loses compatibility/data, requires a new
  platform/service, contradicts an invariant, or materially expands sprint scope. Give the
  concrete problem, evidence, considered options, recommended choice, and work still possible.
- Tester sends clear reproducible implementation defects to the developer in one report.
  Tester asks the lead for an unclear oracle, conflicting requirements, suspected silent
  data corruption, or an architectural issue. Do not guess the business rule.
- After two unsuccessful repair/retest cycles for the same underlying defect, request a
  lead review. This is a stop to repeated guessing, not permission to ignore other safe work.
- Every completed sprint returns to the lead even if all tests pass. Only the lead accepts
  the sprint and authorizes the next one. The light tester can assist evidence collection.

Developer checkpoints go into `handoffs/S01-dev.md` using the template, even while work
is incomplete. Record last completed pass, current change, remaining checks, exact branch
and revision, dirty-file scope, and next action. End a normal completed developer session with:

> READY_FOR_LIGHT_TEST — S01 implementation and developer checks are complete. Start the
> light tester with the prompt in projectmanagement/README.md. Handoff: projectmanagement/handoffs/S01-dev.md.

Tester ends with one clear route and a copyable prompt. For a lead review:

> READY_FOR_TECH_LEAD — S01 independent testing is complete. Use the tech-lead review prompt
> in projectmanagement/README.md. Report: projectmanagement/reports/S01-test.md. Outstanding limitations: [...].

If failures remain, use DEV_FIX_REQUIRED or TECH_LEAD_REQUIRED and name the blocking IDs.
Do not describe a sprint as finished just because testing finished.

Branch and evidence rules:

- Never discard, stash, rebase, or switch away from someone else's uncommitted changes
  as routine setup. The current stabilization changes are part of this candidate baseline.
- For S01, use the current checkout unless the user has selected an isolated checkout.
  Subsequent clean sprint branches use `codex/s02-...`, based on the accepted desktop line,
  not automatically `main`, which lacks the desktop implementation.
- Record actual branch names; proposed branches must say NOT_CREATED. No branch deletion,
  merging, pushing, PR creation, or cleanup is required by this planning task.
- Freeze the candidate while light testing runs. Test the actual candidate revision. If
  production changes afterward, record new revision/hashes and rerun impacted checks.
- A dirty checkout needs a source-file hash inventory and diff summary, since HEAD alone
  does not identify what was tested. Never stage unrelated work wholesale.
- Keep raw TRX, logs, and generated files under ignored `generated/verification/<sprint>/`.
  Commit concise sanitized evidence summaries and reproductions under projectmanagement.
  Local-only log paths are evidence locations, not portable proof; state that limitation.

The light tester updates backlog status from evidence and can add discovered issues.
It cannot reprioritize the product, waive acceptance, or mark lead-reviewed DONE without
the lead's record. The developer updates its pass checkpoint; administration belongs
primarily to the tester and lead.
