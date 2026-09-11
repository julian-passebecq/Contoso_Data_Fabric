> **2026-09-11 takeover:** The user has ended the multi-agent sprint workflow for cost reasons. Start with [handover/README.md](../handover/README.md). The historical status/process below is retained as evidence; S01 remains unimplemented and unaccepted. The Pro successor owns continuation.

# Contoso Fabric Builder delivery hub

This folder is the persistent agreement between the user, tech lead, medium developer,
and light tester. It was established on 2026-09-08 after inspecting the actual checkout.

The tech lead owns architecture, business correctness, sprint acceptance, and sequencing.
The medium developer completes several substantial passes autonomously inside one sprint.
The light tester independently verifies the result and maintains the delivery records.
The user normally needs to initiate only development, testing, and the final lead review.

Start here, in this order:

1. [Current status and next action](STATUS.md)
2. [Working process and escalation rules](WORKFLOW.md)
3. [Product vision and roadmap](VISION.md)
4. [Architecture decisions and invariants](ARCHITECTURE.md)
5. [Active Sprint S01](sprints/S01.md)
6. [Acceptance test plan](TEST_PLAN.md)

Supporting records:

- [Feature backlog](BACKLOG.md)
- [Branch and test registers](REGISTERS.md)
- [Initial tech-lead code review](audits/2026-09-08-initial.md)
- [Evidence index](evidence/BASELINE.md)
- [Development handoff template](templates/DEV_HANDOFF.md)
- [Independent test report template](templates/TEST_REPORT.md)
- [Lead review template](templates/LEAD_REVIEW.md)

Copy this instruction to the medium developer:

> Act as the medium developer for this repo. Read AGENTS.md and projectmanagement/README.md,
> STATUS.md, WORKFLOW.md, ARCHITECTURE.md, sprints/S01.md, and TEST_PLAN.md. Complete every
> approved S01 pass in sequence without asking me to start each pass. Preserve the existing
> stabilization changes. Write checkpoints at pass boundaries and continue automatically.
> Run developer checks and fix failures within scope. At the completed sprint boundary,
> produce projectmanagement/handoffs/S01-dev.md and tell me READY_FOR_LIGHT_TEST with the
> exact next prompt. Escalate only under the documented rules. Do not start S02.

Copy this instruction to the light tester after the development handoff:

> Act as the independent light tester and backlog custodian. Read projectmanagement/README.md,
> STATUS.md, sprints/S01.md, TEST_PLAN.md, and handoffs/S01-dev.md. Verify the actual candidate
> revision and working tree, execute the S01 matrix, inspect the relevant diff, and report
> evidence and gaps. Maintain BACKLOG.md, REGISTERS.md, and STATUS.md. Do not fix production
> code or weaken acceptance criteria. Write projectmanagement/reports/S01-test.md. Route
> reproducible implementation failures to the developer; otherwise tell me READY_FOR_TECH_LEAD
> or TECH_LEAD_REQUIRED, explaining why and providing the exact next prompt.

Copy this instruction to the tech lead after testing:

> Review Sprint S01 as tech lead. Read projectmanagement/STATUS.md, ARCHITECTURE.md,
> sprints/S01.md, handoffs/S01-dev.md, and reports/S01-test.md. Audit the actual code and
> the complete sprint diff, including the inherited stabilization changes. Check business
> logic and failure paths independently of test results. Record findings and accept or
> return the sprint in projectmanagement/audits/S01-lead.md. Update the backlog and status.
> Only after acceptance, refine and authorize the next sprint with coding passes and tests.

These prompts assume S01 is still active. Later handoffs must replace the sprint ID with
the one in STATUS.md. A missing handoff or report is an explicit missing artifact, not
permission to invent its results. Reports and evidence stay in this repository; generated
data, caches, secrets, and raw tenant responses do not belong in this folder.
