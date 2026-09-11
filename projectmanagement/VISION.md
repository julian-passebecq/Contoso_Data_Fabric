# Product vision and delivery sequence

Working product assumption, grounded in the existing repo: a Windows desktop tool lets
a developer, trainer, or demo builder produce realistic Contoso Sales data and a usable
Microsoft Fabric / Power BI demonstration from a reusable project file. The valuable
outcome is a repeatable, explainable pipeline whose numbers can be trusted and whose
failures can be diagnosed. More artifact types alone do not establish that outcome.

This vision is provisional product direction from the code and README, not a claim that
the user supplied a complete product specification. It can be refined without delaying S01.

V1 user journey:

1. Create/open a version-compatible `.fabric.json` project and understand the generation
   target, date interval, fixed seed, format, stage range, and workspace target.
2. Generate locally without Fabric credentials. Inspect actual generated counts and a
   committed dataset identity. Repeating a run must not silently mix old and new data.
3. Use read-only preflight to identify missing dependencies before making remote changes.
4. Run a complete pipeline or selected contiguous range. Resolve dependencies explicitly;
   do not rebuild upstream stages outside that range.
5. Inspect Bronze truth reconciliation, Silver data quality, Gold business reconciliation,
   the model, and a rendered report whose currency behavior is explicit.
6. Find a durable receipt explaining completed work, failed/unknown work, and the next
   recovery step. Rerun deliberately without duplicating same-name resources.

| Capability | Why it matters | Exit evidence |
| --- | --- | --- |
| Reliable local generation | A broken foundation prevents every demo | Real repeated generation, failure/cancellation isolation, file/count validation |
| Explicit project and stage contracts | Users can reuse configurations and run slices safely | Validation tests and all valid ranges checked |
| Predictable Fabric orchestration | Long jobs need reliable identity, bounds, and diagnostics | Transport fault tests plus live item/job evidence |
| Governed data transformations | Plausible charts can conceal incorrect numbers | Executed bad-data fixtures and independent numeric reconciliation |
| Working semantic model and report | The user needs an analytical result | Service acceptance, measure queries, relationship checks, rendered visual evidence |
| Recovery and dependable reruns | Real users retry after partial failures | No duplicate items, dataset provenance, failure/restart cases |
| Practical desktop distribution | A demo must work outside the developer checkout | Clean-machine launch, documented prerequisites, smoke checks |

Delivery order (only S01 is currently approved):

| Sprint | Outcome | Depends on | Gate |
| --- | --- | --- | --- |
| S01 | Trustworthy local datasets, testable/bounded orchestration, durable execution evidence | Current stabilization candidate | Local independent test + lead code audit |
| S02 forecast | Executed data contracts, isolated remote landing, validation/publication policy | Accepted S01 and lead refinement | Adversarial data tests + lead audit |
| S03 forecast | Demonstrated Tiny SalesBi through a real Fabric report and rerun | Accepted S02; designated tenant workspace | Live evidence and lead review |
| S04 forecast | V1 hardening, format/scale coverage, recovery UX, distribution | Accepted live vertical slice | Release matrix and user-facing smoke |

Avoid promising calendar dates before S01 measures throughput and S03 measures actual
service behavior. Review scope at each sprint boundary. If S01 passes are too short,
increase coherence and batch size in S02; do not remove acceptance checks.

After v1, keep these as separate product decisions:

- Reporting currency conversion: requires an explicit rate-date, missing-rate, rounding,
  and currency policy before adding converted measures.
- Richer reports and scenario templates: grow only from verified Gold/model contracts.
- Customer Experience, Data Quality, ML Dissatisfaction: enums currently catalogue these;
  they are not implemented scenarios. Each needs its own data meaning and acceptance oracle.
- Workspace/capacity provisioning, deployment profiles, and automated cleanup: need a
  resource ownership/cost lifecycle design. Existing workspace use comes first.
- Advanced scale/performance: optimize measured bottlenecks while preserving counts,
  determinism contracts, cancellation boundaries, and provenance.

No web frontend replacement, new Python orchestration service, arbitrary seed support,
exact row-count generation guarantee, or general workflow engine is in the approved v1 scope.
