# Contoso Fabric Builder — native C# architecture

The Fabric edition is intentionally built around the existing .NET 8 `DatabaseGenerator` instead of replacing it.

## Start the desktop app

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

## Current native BI path

```text
Contoso C# generator
        |
        v
Bronze Lakehouse + notebook
        |
        v
Silver Lakehouse + quality notebook
        |
        v
Gold Lakehouse + reconciliation
        |
        v
Direct Lake TMDL semantic model
        |
        v
PBIR Sales Overview report
```

Application structure:

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
  - Fabric REST v1 catalog/client
  - OneLake uploader
  - Fabric definition deployer
  - generated PySpark notebooks
  - Direct Lake TMDL factory
  - PBIR report factory
  - preflight + pipeline runner
```

## Reusable `.fabric.json` projects

Projects are designed to be stored in Git. The file contains data-generation parameters, exact stage range and Fabric BI target names.

See:

- `examples/sales-small-bronze.fabric.json`
- `examples/sales-small-full-bi.fabric.json`

Full BI example:

```json
{
  "name": "contoso-sales-full-bi",
  "scenario": "salesBi",
  "scale": "small",
  "years": 3,
  "rawFormat": "parquet",
  "startFrom": "generate",
  "stopAfter": "report",
  "workspace": {
    "workspaceName": "YOUR FABRIC WORKSPACE",
    "workspaceId": null
  },
  "bronzeLakehouse": "Contoso_Bronze",
  "silverLakehouse": "Contoso_Silver",
  "goldLakehouse": "Contoso_Gold",
  "semanticModelName": "Contoso_Sales_Model",
  "reportName": "Contoso_Sales_Report",
  "requestedSeed": 0,
  "ordersOverride": null,
  "startDate": "2014-01-01T00:00:00"
}
```

### Generation parameters

- `scale`: `tiny`, `small`, `medium`, `large`.
- `ordersOverride`: optional exact order count; overrides the scale preset.
- `startDate`: first date for generated history.
- `years`: 1–20.
- `rawFormat`: `csv`, `parquet`, or `delta`.
- `requestedSeed`: persisted, but the original generator remains intentionally deterministic with seed `0`.

## Execution ranges

`startFrom` and `stopAfter` define one contiguous range across:

```text
Generate -> Bronze -> Silver -> Gold -> SemanticModel -> Report
```

Examples:

```text
Generate      -> Report         full current BI pipeline
Generate      -> Bronze         generation + Bronze only
Bronze        -> Bronze         reuse local generated/data
Silver        -> Gold           require existing Bronze
Gold          -> Report         rebuild Gold, model and report only
SemanticModel -> Report         require existing Gold
Report        -> Report         require existing semantic model
```

Upstream stages outside the range are dependencies only; they are not recreated or rerun.

## Read-only preflight

Preflight does not mutate Fabric. Depending on the selected range it checks:

- project/range validity
- local generator inputs
- existing local raw data for Bronze-start
- Azure CLI and Fabric workspace access
- Fabric capacity assignment
- workspace type
- item API readability
- existing Bronze dependency for Silver-start
- existing Silver dependency for Gold-start
- existing Gold dependency for SemanticModel-start
- existing semantic-model dependency for Report-start
- local-currency semantic-model rule

## Data stages

### Generate

Calls the existing C# `DatabaseGenerator.Engine` directly.

### Bronze

Creates/reuses the Bronze Lakehouse, uploads raw files to `Files/raw`, deploys the generated notebook, materializes Delta tables and waits for job completion.

### Silver

Creates conformed facts/dimensions, deduplicates business keys and writes `data_quality_summary`.

### Gold

Gold builds:

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
- `pipeline_validation_summary`

Before the Gold notebook completes it reconciles:

- Silver vs Gold sales row count
- duplicate Product/Store/Customer dimension keys
- orphan Product/Store/Customer foreign keys
- detailed fact revenue/margin vs `sales_daily`, per currency

Any failed check raises an exception. Semantic-model/report publication is sequenced after Gold, so BI artifacts are not published after a failed Gold reconciliation.

## Direct Lake semantic model

The semantic model is generated as a complete TMDL definition and deployed through the Fabric REST definition API.

Current model tables:

- `Sales` -> `fact_sales_enriched`
- `Product` -> `dim_product`
- `Store` -> `dim_store`
- `Customer` -> `dim_customer`
- `Date` -> `dim_date`

Relationships:

```text
Sales.ProductKey  -> Product.ProductKey
Sales.StoreKey    -> Store.StoreKey
Sales.CustomerKey -> Customer.CustomerKey
Sales.OrderDay    -> Date.Date
```

Measures:

- `Revenue Local`
- `Gross Margin Local`
- `Gross Margin %`
- `Orders`
- `Customers`
- `Units`
- `Average Order Value Local`

The Direct Lake source is a shared `AzureStorage.DataLake` expression using resolved workspace and Gold Lakehouse GUIDs.

### Currency rule

The source is multi-currency. The tool deliberately does not invent a reporting currency.

`Revenue Local` and `Gross Margin Local` therefore use `HASONEVALUE(Sales[CurrencyCode])`: when more than one currency is in filter context the measure returns blank instead of adding unlike currencies.

## PBIR Sales Overview

The generated report is bound to the deployed semantic model through `definition.pbir` `byConnection` using the semantic-model ID.

The first page contains:

- Currency dropdown slicer
- multi-measure `cardVisual`
- Revenue Local trend by OrderDay
- Revenue Local by Product category
- Revenue Local and Gross Margin Local by Store country

PBIR page and visual IDs are deterministic, so repeated generation updates the same logical report structure rather than creating random definitions.

## Definition deployment semantics

Semantic models and reports are reused by exact display name and updated in place.

The deployer always sends the complete definition:

- Semantic model format: `TMDL`
- Report format: `PBIR`
- Part payloads: `InlineBase64`
- LROs: poll operation status and honor `Retry-After`
- throttling: retry HTTP 429

Duplicate same-name items are rejected instead of selecting one arbitrarily.

## Separate UI actions

- **Plan** — calculate the range only.
- **Preflight** — read-only dependency/environment validation.
- **Generate locally** — run only the original generator.
- **Prepare selected stages** — idempotently create/update definitions; BI definitions are deferred when selected Gold has not yet executed.
- **Upload raw** — upload existing local raw files when Bronze is selected.
- **Run selected range** — execute exactly `startFrom -> stopAfter`.
- **Cancel** — cancel local/API polling where supported.

Runtime states are `Ready`, `Prepared`, `Running`, `Completed`, `Failed` and `Skipped`. The activity log records transitions and preflight results.

## Authentication and OneLake

The desktop app uses `AzureCliCredential` and stores no passwords, client secrets or Fabric tokens.

Live orchestration resolves workspace/Lakehouse GUIDs. Generated Spark uses GUID-based paths:

```text
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Files/...
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Tables/...
```

## Verification boundary

CI validates compilation and local contracts. It does not mutate a real Fabric tenant.

The live code path now covers Generate through Report, but a real `az login` + capacity-backed Fabric workspace run is still required before the PR should be considered tenant-verified.

Still roadmap after this slice:

- Terraform workspace/capacity provisioning
- Customer Experience / Data Quality / ML scenario generators
- optional reporting-currency conversion policy
- richer multi-page PBIR templates
- live tenant integration tests / cleanup mode
