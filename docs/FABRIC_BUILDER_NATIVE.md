# Contoso Fabric Builder — native C# architecture

The Fabric edition is intentionally built around the existing .NET 8 `DatabaseGenerator` instead of replacing it.

## Start the desktop app

From PowerShell on Windows:

```powershell
.\run_fabric_builder.ps1
```

After the first restore, the launcher can skip restore:

```powershell
.\run_fabric_builder.ps1 -NoRestore
```

For live Fabric operations, authenticate first:

```powershell
az login
```

## Current vertical slice

```text
ContosoFabric.Desktop (WPF)
        |
        v
ContosoFabric.Core
  - FabricProject
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
  - pipeline runner
        |
        v
Microsoft Fabric
  Bronze -> Silver -> Gold
```

## Reusable `.fabric.json` projects

The desktop app can create, open and save project JSON files. These files are intended to be versioned in Git and make the parameterization explicit rather than hiding it in UI state.

Example: `examples/sales-small-bronze.fabric.json`.

```json
{
  "name": "contoso-sales-bronze-demo",
  "scenario": "salesBi",
  "scale": "small",
  "years": 3,
  "rawFormat": "parquet",
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

### Generation parameters

- `scale`: `tiny`, `small`, `medium`, `large`.
- `ordersOverride`: optional exact order count; when set, it overrides the scale preset.
- `startDate`: first date for generated history.
- `years`: number of generated years, 1–20.
- `rawFormat`: `csv`, `parquet`, or `delta`.
- `requestedSeed`: preserved in the project contract, but the legacy generator still intentionally executes with seed `0`.

### Fabric parameters

- workspace name and, after discovery, workspace ID.
- Bronze, Silver and Gold Lakehouse names.
- `stopAfter`: `generate`, `bronze`, `silver`, `gold`, `semanticModel`, or `report`.

Only stages through Gold are executable in the current native vertical slice; later stages remain visibly marked as roadmap.

## What `Run selected pipeline` does

### Stop after Generate

1. Calls the existing C# Contoso generator directly.
2. Applies the project start date, years, scale/custom order count and raw format.
3. Writes the requested CSV, Parquet or Delta output locally.
4. Does not authenticate to Fabric.

### Stop after Bronze

1. Resolves the selected workspace using the Fabric REST API.
2. Creates the Bronze Lakehouse if it does not exist.
3. Generates or updates the project-specific Bronze notebook.
4. Generates Contoso data locally.
5. Uploads the raw output to `Files/raw` in OneLake.
6. Executes the Bronze PySpark notebook.
7. Waits for the Fabric job instance to reach `Completed`.

### Stop after Silver

Runs Bronze, then creates/reuses Silver, deploys the cleaning notebook, deduplicates the core entities by business key, writes conformed Delta facts/dimensions, writes `data_quality_summary`, and waits for the notebook job to complete.

### Stop after Gold

Runs Bronze and Silver, then creates/reuses Gold and builds:

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

Amounts stay grouped by `CurrencyCode` in the first Gold model. The app deliberately does not invent a currency-conversion convention before that business rule is explicitly defined.

## Separate actions

The UI deliberately separates:

- **Plan** — calculate what will happen; no Fabric mutation.
- **Generate locally** — run only the C# data motor.
- **Prepare Fabric** — create/reuse Lakehouses and create/update notebooks, but do not execute them.
- **Upload raw** — land already-generated files in Bronze without running transformations.
- **Run selected pipeline** — execute from generation through the chosen endpoint.
- **Cancel** — cancel local work/API polling where cancellation is supported.

A stage shown as **Prepared** means its Fabric item definitions exist but its transformation notebook has not completed. **Completed** means the stage notebook run completed successfully.

## Idempotency

- Lakehouses are looked up by exact display name and reused.
- Notebooks are looked up by display name and their definitions are updated in place.
- Bronze, Silver and Gold tables use overwrite semantics for the current educational/demo workflow.
- The raw upload overwrites matching file paths.
- Delta raw output is uploaded recursively, including `_delta_log` JSON files.
- The uploader creates only directories beneath the Fabric-managed Lakehouse `Files/raw` root.

## Authentication

The desktop application currently uses `AzureCliCredential`.

The app requests Fabric API and OneLake tokens through the Azure SDK. It does not store a password, client secret or Fabric token.

## OneLake addressing

Live orchestration resolves workspace and Lakehouse GUIDs before upload/execution. Generated Spark code uses GUID-based ABFSS paths:

```text
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Files/...
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Tables/...
```

Using GUIDs avoids the special-character limitations of name-based ABFSS workspace paths.

## Fabric API behavior covered

The C# REST client handles:

- workspace discovery
- item listing and exact-name reuse
- Lakehouse creation
- Notebook creation and definition update
- Fabric long-running operations (`202`, `x-ms-operation-id`, `Retry-After`)
- run-on-demand notebook execution with `beta=false`
- job-instance polling
- `429 Too Many Requests` retry handling
- cancellation and explicit timeouts

## Current boundary

Implemented live: Generate, Bronze, Silver, Gold.

Still roadmap: Direct Lake semantic model, PBIR report, Terraform workspace/capacity creation, extra scenario generators, and post-run truth/KPI reconciliation.
