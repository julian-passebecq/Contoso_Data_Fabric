# Contoso Fabric Builder — native C# architecture

The Fabric edition is intentionally built around the existing .NET 8 `DatabaseGenerator` instead of replacing it.

## Current vertical slice

```text
ContosoFabric.Desktop (WPF)
        |
        v
ContosoFabric.Core
  - FabricProject
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

## What `Run selected pipeline` does

### Stop after Generate

1. Calls the existing C# Contoso generator directly.
2. Writes the requested CSV, Parquet or Delta output locally.
3. Does not authenticate to Fabric.

### Stop after Bronze

1. Resolves the selected workspace using the Fabric REST API.
2. Creates the Bronze Lakehouse if it does not exist.
3. Generates or updates the project-specific Bronze notebook.
4. Generates Contoso data locally.
5. Uploads the raw output to `Files/raw` in OneLake.
6. Executes the Bronze PySpark notebook.
7. Waits for the Fabric job instance to reach `Completed`.

### Stop after Silver

Runs Bronze, then:

1. Creates/reuses the Silver Lakehouse.
2. Generates/updates the Silver notebook.
3. Deduplicates core entities by business keys.
4. Writes conformed dimensions/facts as Delta.
5. Writes a `data_quality_summary` Delta table.
6. Waits for the notebook job to complete.

### Stop after Gold

Runs Bronze and Silver, then:

1. Creates/reuses the Gold Lakehouse.
2. Generates/updates the Gold notebook.
3. Copies conformed dimensions and the detailed sales fact into Gold.
4. Builds `fact_sales_enriched`.
5. Builds `sales_daily`, `sales_by_product`, `sales_by_store`, and `customer_value`.
6. Waits for the notebook job to complete.

Amounts stay grouped by `CurrencyCode` in the first Gold model. The app deliberately does not invent a currency-conversion convention before that business rule is explicitly defined.

## Idempotency

- Lakehouses are looked up by exact display name and reused.
- Notebooks are looked up by display name and their definitions are updated in place.
- Bronze, Silver and Gold tables are written using overwrite semantics for the current educational/demo workflow.
- The raw upload overwrites matching file paths.

## Authentication

The desktop application currently uses `AzureCliCredential`. Run:

```powershell
az login
```

The app requests Fabric API and OneLake tokens through the Azure SDK. It does not store a password, client secret or Fabric token.

## OneLake addressing

Live orchestration resolves workspace and Lakehouse GUIDs before upload/execution. Generated Spark code uses GUID-based ABFSS paths:

```text
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Files/...
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Tables/...
```

This avoids the special-character limitations of name-based ABFSS workspace paths.

## Current boundary

Implemented live: Generate, Bronze, Silver, Gold.

Still roadmap: Direct Lake semantic model, PBIR report, Terraform workspace/capacity creation, extra scenario generators, and post-run KPI reconciliation.
