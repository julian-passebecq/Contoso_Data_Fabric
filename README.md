# Contoso Data Generator V2 + Fabric Builder

This repository keeps the original **Contoso Data Generator V2** C# engine and adds a native .NET 8 desktop application for parameterized Microsoft Fabric pipelines.

## Fabric Builder

Active implementation:

- `DatabaseGenerator` — original deterministic C# data motor
- `ContosoFabric.Core` — project files, validation and range planning
- `ContosoFabric.Fabric` — Fabric REST, OneLake, notebooks, Direct Lake TMDL and PBIR
- `ContosoFabric.Desktop` — native WPF application
- `ContosoFabric.Core.Tests` — planner/definition/notebook contract tests

The current Sales/BI path is:

```text
Generate -> Bronze -> Silver -> Gold -> Direct Lake semantic model -> Power BI report
```

Both **Start from** and **Stop after** are selectable, so the same application can run the entire path or only a contiguous slice such as `Bronze -> Bronze`, `Silver -> Gold`, `SemanticModel -> Report`, or `Report -> Report` when the required upstream artifacts already exist.

No React frontend or Python orchestration service is used. The application calls the existing `DatabaseGenerator.Engine` directly and keeps Fabric orchestration in C#.

### Quick start on Windows

```powershell
# Required only for live Fabric operations
az login

# Restore and launch the WPF application
.\run_fabric_builder.ps1
```

After the first restore:

```powershell
.\run_fabric_builder.ps1 -NoRestore
```

Then:

1. Open or create a `.fabric.json` project.
2. Choose scale or an exact order count, start date, years and raw format.
3. Choose the Fabric workspace and Bronze/Silver/Gold Lakehouse names.
4. Choose the Direct Lake semantic-model and report names.
5. Select **Start from** and **Stop after**.
6. Run **Preflight** before live changes.
7. Use **Run selected range**, or the separate Generate / Prepare / Upload actions when you need only part of the workflow.

Examples:

- `examples/sales-small-bronze.fabric.json`
- `examples/sales-small-full-bi.fabric.json`

See [`docs/FABRIC_BUILDER_NATIVE.md`](docs/FABRIC_BUILDER_NATIVE.md) for the architecture and exact behavior.

## Current BI output

### Gold

Gold produces conformed dimensions/facts and analytics tables including `fact_sales_enriched`, `sales_daily`, product/store aggregates and `customer_value`.

Before the BI layer is published, the Gold notebook writes `pipeline_validation_summary` and checks row-count preservation, duplicate dimension keys, orphan dimension references, and detailed-vs-daily revenue/margin reconciliation by currency. Failed validation raises an error and blocks semantic-model/report publication.

### Direct Lake semantic model

The application generates and idempotently deploys a complete **TMDL** semantic-model definition over the Gold Lakehouse. The first model exposes Sales, Product, Store, Customer and Date with explicit relationships and measures.

The generated source is multi-currency. `Revenue Local` and `Gross Margin Local` intentionally return blank when multiple `CurrencyCode` values are in filter context rather than adding unlike currencies.

### PBIR report

The application generates a deterministic **PBIR** Sales Overview report bound to the deployed semantic model by semantic-model ID. The first page includes a currency slicer, KPI card, revenue trend, product-category view and country view.

## Verification

Windows GitHub Actions restores and builds the complete solution and runs xUnit contract tests. The latest functional pass builds with **0 warnings / 0 errors** and passes **21 / 21 tests**.

The tests cover range planning, project JSON persistence, Direct Lake TMDL construction, PBIR structure/binding, deterministic visual IDs, the multi-currency guard and Gold reconciliation/failure-gate generation.

CI deliberately does **not** mutate a real Fabric tenant. Live validation still requires an authenticated tenant and a capacity-backed Fabric workspace.

## Original generator

DataGenerator generates sample data ready to be imported into Power BI or Fabric OneLake for analysis. This repository is based on the V2 evolution of the original SQLBI Contoso generator.

Supported output formats include:

- Parquet
- Delta Table files
- CSV
- CSV multi-file
- CSV multi-file gzip
- SQL Server via bulk-import scripts

The original generator requires:

- a configuration JSON file
- a data Excel file
- an output folder
- a cache folder
- optional command-line parameter overrides

```text
databasegenerator.exe configfile datafile outputfolder cachefolder [param:OrdersCount=nnnn]
```

The source engine remains under `DatabaseGenerator/` and is reused directly by the Fabric application rather than forked or rewritten.
