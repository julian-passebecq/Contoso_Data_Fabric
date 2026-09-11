# <SPRINT> tech-lead acceptance review

Date: <UTC>. Candidate: <branch, full HEAD, diff base and dirty identity if applicable>.
Input reports: <developer handoff, independent report, evidence>.
Decision / STATUS.md state: ACCEPTED | DEV_FIX_REQUIRED | BLOCKED_EXTERNAL | TECH_LEAD_REQUIRED.
Use DEV_FIX_REQUIRED for implementation repairs and TECH_LEAD_REQUIRED for unresolved
architecture decisions. Record the next actor and concrete action using WORKFLOW.md.

Actual audit scope: <files/code paths reviewed; inherited changes; exclusions>.

| Invariant | Code reasoning | Independent evidence | Verdict |
| --- | --- | --- | --- |
| <ADR/invariant> | <why code satisfies or violates it> | <test/result> | <pass/finding/unknown> |

Findings: <severity, concrete trigger, impact, file/line, requested correction, owner>.
Tests personally run vs relied on: <clear provenance>.
Unresolved limitations: <especially UI/live/data-publication boundaries>.
Backlog dispositions: <accepted features and retained/open issues with rationale>.
Sprint acceptance rationale: <why the promised outcome is or is not achieved>.

If accepted: refine the next sprint's scope, dependencies, coherent passes and exact test
oracles, create its sprint document, update STATUS.md and routing prompts, and identify
the next developer action. If not accepted: give one consolidated repair instruction.
Do not authorize the next sprint merely because the current test run finished.
