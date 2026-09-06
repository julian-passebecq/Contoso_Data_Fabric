# Contoso Fabric Builder — native C# architecture

The Fabric edition is intentionally built around the existing .NET 8 `DatabaseGenerator` instead of replacing it.

## Start the desktop app

From PowerShell on Windows:

```powershell
.\run_fabric_builder.ps1
```

After the first restore:

```powershell
.\run_fabric_builder.ps1 -NoRestore
```

For live Fabric operations:

```powershell
az login
```

## Current vertical slice

```text
ContosoFabric.Desktop (WPF)
        |
        v
ContosoFabric.Core
  - FabricProject / .fabric.json
  - ProjectFileService
  - PipelinePlanner
  - LegacyGeneratorAdapter
        |
        +---------------------> DatabaseGenerator.Engine
        |
        v
ContosoFabric.Fabric
  - Fabric REST v1 client
  - OneLake uploader
  - generated PySpark notebooks
  - preflight + pipeline runner
        |
        v
Microsoft Fabric
  Bronze -> Silver -> Gold
```

## Reusable `.fabric.json` projects

Projects are intended to be versioned in Git. The file stores both the data-generation contract and the selected execution range.

```json
{
  "name": "contoso-sales-bronze-demo",
  "scenario": "salesBi",
  "scale": "small",
  "years": 3,
  "rawFormat": "parquet",
  "startFrom": "generate",
  "stopAfter": "bronze",
  "workspace": {
    "workspaceName": "YOUR FABRIC WORKSPACE",
    "workspaceId": null
  },
  "bronzeLakehouse": "Contoso_Bronze",
  "silverLakehouse": "Contoso_Silver",
  "goldLakehouse": "Contoso_Gold",
  "requestedSeed": 0,
  "ordersOverride": null,
  "startDate": "2014-01-01T00:00:00"
}
```

Example: `examples/sales-small-bronze.fabric.json`.

### Generation parameters

- `scale`: `tiny`, `small`, `medium`, `large`.
- `ordersOverride`: optional exact order count; overrides the scale preset.
- `startDate`: first date for generated history.
- `years`: 1–20.
- `rawFormat`: `csv`, `parquet`, or `delta`.
- `requestedSeed`: preserved, but the legacy generator still intentionally executes with seed `0`.

### Execution range

`startFrom` and `stopAfter` define a contiguous range.

Examples:

```text
Generate -> Gold   full native pipeline
Generate -> Bronze generate + land/materialize Bronze
Bronze   -> Bronze reuse generated/data; run only Bronze
Silver   -> Gold   do not rerun Bronze; execute Silver then Gold
Gold     -> Gold   run only Gold against existing Silver
```

Rules:

- `startFrom=generate` creates fresh local data.
- `startFrom=bronze` skips generation and requires a compatible local `generated/data` folder.
- `startFrom=silver` requires the named Bronze Lakehouse to already exist and does not recreate or rerun Bronze.
- `startFrom=gold` requires the named Silver Lakehouse to already exist and does not touch Bronze/Silver execution.
- `startFrom` cannot be later than `stopAfter`.
- Semantic Model and Report can be shown as roadmap endpoints but cannot be used as start stages yet.

This is the mechanism for running a full pipeline or only one/two selected stages without unnecessarily replaying upstream work.

## Read-only preflight

**Preflight** does not mutate Fabric. It checks:

- project validity and stage range
- local generator inputs when Generate is selected
- existing local raw tables for Bronze-start runs
- Azure CLI / Fabric workspace access
- workspace capacity assignment
- workspace type
- Fabric item API readability
- required existing Bronze for Silver-start
- required existing Silver for Gold-start

A failed upstream/capacity check therefore occurs before Lakehouse creation or notebook execution.

## Stage behavior

### Generate

Calls the existing C# `DatabaseGenerator.Engine` directly and applies exact order count/scale, start date, years and raw format.

### Bronze

1. Create/reuse Bronze Lakehouse.
2. Create/update project-specific Bronze notebook.
3. Upload local raw output recursively to `Files/raw`.
4. Execute Bronze notebook.
5. Materialize source tables as Delta and wait for the Fabric job to complete.

### Silver

1. Use Bronze Delta tables.
2. Create/reuse Silver Lakehouse.
3. Create/update Silver notebook.
4. Deduplicate core entities by business keys.
5. Write conformed facts/dimensions.
6. Write `data_quality_summary`.
7. Wait for completion.

### Gold

Gold contains:

- `dim_customer`
- `dim_store`
- `dim_product`
- `dim_date`
- `dim_currencyexchange`
- `fact_sales`
- `fact_sales_enriched`
- `sales_daily`
- `sales_by_product`
- `sales_by_store`
- `customer_value`

Monetary aggregates remain grouped by `CurrencyCode`; the tool does not invent a reporting-currency convention.

## Separate UI actions

- **Plan** — calculate the exact selected range; no Fabric mutation.
- **Preflight** — read-only environment/dependency validation.
- **Generate locally** — run only the original C# data motor regardless of the saved range.
- **Prepare selected stages** — create/reuse only selected output Lakehouses and create/update selected notebooks; required upstream Lakehouses are read, not recreated.
- **Upload raw** — upload existing local raw files when Bronze is in the selected range.
- **Run selected range** — execute exactly `startFrom -> stopAfter`.
- **Cancel** — cancel local work/API polling where cancellation is supported.

The activity log records status changes and preflight PASS/WARN/FAIL results.

A stage shown as **Prepared** has its item definitions ready but its transformation notebook has not completed. **Completed** means the stage notebook completed successfully.

## Idempotency and safety

- Lakehouses are reused by exact display name.
- Notebooks are updated in place by exact display name.
- Bronze/Silver/Gold tables currently use overwrite semantics for the educational/demo workflow.
- Raw uploads overwrite matching paths.
- Delta raw output uploads recursively, including `_delta_log` JSON.
- OneLake directory creation is limited to the Fabric-managed Lakehouse `Files/raw` subtree.
- Duplicate workspace names are rejected unless the workspace ID is known.
- Fabric 429 responses honor `Retry-After`.
- REST long-running operations and notebook jobs are polled with explicit timeouts and cancellation.

## Authentication

The desktop app uses `AzureCliCredential`. It does not store client secrets, passwords or Fabric access tokens.

## OneLake addressing

Live orchestration resolves workspace and Lakehouse GUIDs. Generated Spark uses GUID-based paths:

```text
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Files/...
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Tables/...
```

This avoids name/special-character problems in ABFSS paths.

## Verification

Windows CI restores and builds the complete solution and runs xUnit tests for:

- stage range selection
- scale presets
- exact order overrides
- invalid configuration
- fixed-seed warning behavior
- `.fabric.json` serialization/round-trip

Live tenant mutation is not performed in CI.

## Current boundary

Implemented live code path: Generate, Bronze, Silver, Gold, including partial stage ranges.

Still roadmap: Direct Lake semantic model, PBIR report, Terraform workspace/capacity creation, additional scenario generators, and post-run truth/KPI reconciliation.
