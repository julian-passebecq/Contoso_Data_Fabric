# CODEX HANDOFF — Contoso Data Fabric Builder

**Date:** 2026-09-08
**Repository:** https://github.com/julian-passebecq/Contoso_Data_Fabric
**Active branch:** `feat/csharp-fabric-desktop-v1`
**Open PR:** https://github.com/julian-passebecq/Contoso_Data_Fabric/pull/2
**PR state:** open, draft, mergeable
**Current branch head:** `47a65b75e0ec126c0c8a0372a1c27709659aa317`
**Last functional code head with green CI:** `2a9488cabce23e237ceae2f7b75fc552c73f3c2f`

> The five commits after `2a9488ca...` only change `README.md` and `docs/FABRIC_BUILDER_NATIVE.md`. No functional source changed after the verified code head.

---

## 0. Codex operating instruction

Continue the existing native C#/.NET Microsoft Fabric application. **Do not restart the project, replace it with React/Python/Streamlit, or redesign the architecture before auditing what already exists.**

Before coding:

1. Checkout `feat/csharp-fabric-desktop-v1`.
2. Read this handoff completely.
3. Read, in order:
   - `README.md`
   - `docs/FABRIC_BUILDER_NATIVE.md`
   - `ContosoFabric.Core/Models/FabricProject.cs`
   - `ContosoFabric.Core/Planning/PipelinePlanner.cs`
   - `ContosoFabric.Fabric/Pipeline/FabricPipelineRunner.cs`
   - `ContosoFabric.Fabric/Api/FabricRestClient.cs`
   - `ContosoFabric.Fabric/Api/FabricDefinitionDeployer.cs`
   - `ContosoFabric.Fabric/Notebooks/NotebookDefinitionFactory.cs`
   - `ContosoFabric.Fabric/SemanticModel/SemanticModelDefinitionFactory.cs`
   - `ContosoFabric.Fabric/Reports/ReportDefinitionFactory.cs`
   - `ContosoFabric.Desktop/MainWindow.xaml`
   - `ContosoFabric.Desktop/MainWindow.xaml.cs`
   - `ContosoFabric.Desktop/MainWindow.Preflight.cs`
   - all tests under `ContosoFabric.Core.Tests/`
4. Run the full restore/build/test gate before changing behavior.
5. Never claim live Fabric behavior is proven until a real Fabric tenant test succeeds.

Primary rule: **preserve the existing C# generator as the data-generation motor and keep Fabric orchestration in C#.** PySpark is allowed inside generated Fabric notebooks; Python is not the application/orchestration layer.

---

## 1. Product purpose

The tool is a native Windows application that parameterizes a Contoso dataset and executes all or part of a Microsoft Fabric analytics pipeline.

```text
Choose project parameters
        ↓
Choose Start from
        ↓
Choose Stop after
        ↓
Preflight
        ↓
Run exactly the selected contiguous pipeline range
```

Current stage graph:

```text
Generate → Bronze → Silver → Gold → SemanticModel → Report
```

Examples of supported ranges:

```text
Generate      → Report
Generate      → Bronze
Bronze        → Bronze
Silver        → Gold
Gold          → Report
SemanticModel → Report
Report        → Report
```

Upstream artifacts outside the selected range are dependencies only; they are not regenerated/reexecuted.

This is **not** intended to become a DAX editor, Power BI report IDE, Tabular Editor replacement, or DAX Studio replacement. The value is Fabric pipeline/data-environment generation, orchestration, reproducible sample data, deployment, validation, lifecycle operations, and tasks awkward to perform manually.

---

## 2. Accepted architecture — do not regress it

The user explicitly rejected a React frontend for this project and asked to restart from the existing C# app.

```text
ContosoDGV2.sln
│
├── DatabaseGenerator
│   └── original .NET 8 Contoso generator
│
├── ContosoFabric.Core
│   ├── domain/project model
│   ├── stage planner
│   ├── .fabric.json persistence
│   ├── adapter into DatabaseGenerator.Engine
│   └── generator truth manifest
│
├── ContosoFabric.Fabric
│   ├── Fabric REST client
│   ├── OneLake uploader
│   ├── Bronze/Silver/Gold notebook factory
│   ├── pipeline runner + preflight
│   ├── TMDL semantic-model factory
│   ├── PBIR report factory
│   └── Fabric definition deployer
│
├── ContosoFabric.Desktop
│   └── native WPF .NET 8 Windows UI
│
└── ContosoFabric.Core.Tests
    └── planner/project/lineage/notebook/TMDL/PBIR contract tests
```

Do not introduce React, Node as required runtime, Streamlit, a Python API service, a duplicate generator, or Fabric MCP as a runtime dependency unless the user explicitly changes direction.

---

## 3. Current end-to-end vertical slice

```text
Original C# Contoso generator
        │
        ├── generated raw files
        └── truth_manifest.json
                │
                v
          OneLake Files/raw
                │
                v
Bronze notebook/materialization
        │
        ├── Delta source tables
        └── bronze_validation_summary
                │
                v
Silver notebook
        │
        ├── conformed facts/dimensions
        └── data_quality_summary
                │
                v
Gold notebook
        │
        ├── conformed Gold tables
        ├── analytical aggregates
        └── pipeline_validation_summary
                │
                v
Direct Lake TMDL semantic model
                │
                v
PBIR Sales Overview report
```

Critical sequencing rule: **when Gold is selected, Semantic Model and Report publication are deferred until Gold successfully executes and passes validation.** Do not publish BI metadata merely because the Gold Lakehouse object exists.

---

## 4. Git/PR state

```text
Repository: julian-passebecq/Contoso_Data_Fabric
Base:       main
Base SHA:   eaeb57a9eaa6ad0cdab4fb527552102685434ee0
Branch:     feat/csharp-fabric-desktop-v1
Head:       47a65b75e0ec126c0c8a0372a1c27709659aa317
PR:         #2
```

PR title:

```text
Native C# Fabric builder: Generate → Report with Direct Lake, PBIR and lineage gates
```

PR remains draft because CI does not mutate a real Fabric tenant.

Verified functional source head:

```text
2a9488cabce23e237ceae2f7b75fc552c73f3c2f
```

The current branch is five commits ahead of that verified head, but the only changed files are `README.md` and `docs/FABRIC_BUILDER_NATIVE.md`.

---

## 5. Build/run locally

Windows is the intended environment because the desktop app is WPF.

Prerequisites:

- .NET 8 SDK
- Windows
- Azure CLI
- for live Fabric: accessible capacity-backed Fabric workspace

Launch:

```powershell
.\run_fabric_builder.ps1
```

After first restore:

```powershell
.\run_fabric_builder.ps1 -NoRestore
```

For live Fabric operations:

```powershell
az login
```

Manual verification:

```powershell
dotnet restore ContosoDGV2.sln
dotnet build ContosoDGV2.sln -c Release --no-restore
dotnet test ContosoFabric.Core.Tests\ContosoFabric.Core.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=normal"
```

---

## 6. Verified CI

Workflow:

```text
.github/workflows/csharp-fabric-desktop.yml
```

Verified on functional head `2a9488ca...`:

```text
dotnet restore  PASS
dotnet build    PASS
0 warnings
0 errors

xUnit           PASS
24 / 24 tests
```

Do not reduce this gate. Live tenant mutation is intentionally absent from CI.

---

## 7. Project model and planner

Source:

```text
ContosoFabric.Core/Models/FabricProject.cs
ContosoFabric.Core/Planning/PipelinePlanner.cs
```

Enums:

```text
BusinessScenario: SalesBi, CustomerExperience, DataQuality, MlDissatisfaction
DataScale:        Tiny, Small, Medium, Large
RawFormat:        Parquet, Csv, Delta
PipelineStage:    Generate, Bronze, Silver, Gold, SemanticModel, Report
```

`FabricProject` stores:

```text
Name
Scenario
Scale
Years
RawFormat
StartFrom
StopAfter
Workspace.WorkspaceName
Workspace.WorkspaceId
BronzeLakehouse
SilverLakehouse
GoldLakehouse
SemanticModelName
ReportName
RequestedSeed
OrdersOverride
StartDate
```

Defaults:

```text
BronzeLakehouse    = Contoso_Bronze
SilverLakehouse    = Contoso_Silver
GoldLakehouse      = Contoso_Gold
SemanticModelName  = Contoso_Sales_Model
ReportName         = Contoso_Sales_Report
RequestedSeed      = 0
StartFrom          = Generate
StartDate fallback = 2014-01-01
```

Scale presets:

```text
Tiny    10,000 orders
Small   100,000
Medium  500,000
Large   2,000,000
```

`OrdersOverride` supersedes scale.

Planner validation includes project name, years 1–20, custom orders 1–50,000,000, start-date year 1990–2100, valid stage order, and required artifact names.

Planner warnings cover fixed seed, custom-order override, partial-run dependencies, Bronze reuse of `generated/data`, unsupported scenarios, and local-currency semantics.

---

## 8. Original generator — preserve it

Project:

```text
DatabaseGenerator/
```

Adapter:

```text
ContosoFabric.Core/Generation/LegacyGeneratorAdapter.cs
```

Baseline inputs:

```text
_test_data/IN/config_test.json
_test_data/IN/data_test.xlsx
```

Adapter calls the existing engine directly:

```csharp
new Engine(dataPath, outputFolder, cacheFolder, config).Exec()
```

Important original behavior:

```csharp
// DO NOT CHANGE SEED !!!
Random rng = new Random(0);
```

Therefore the app stores `RequestedSeed` but currently executes with effective seed `0`. Never imply arbitrary seed support works until deliberately implemented/tested.

Expected source tables:

```text
customer
store
product
date
currencyexchange
sales
orders
orderrows
```

Supported new-app raw landing formats:

```text
CSV
Parquet
Delta
```

---

## 9. Generator truth manifest

Source:

```text
ContosoFabric.Core/Generation/GenerationManifest.cs
```

After generation, the adapter reads `_log.log` and writes:

```text
generated/data/truth_manifest.json
```

Manifest fields:

```text
schemaVersion
projectName
generatedAtUtc
requestedOrders
actualOrders
actualOrderRows
startDate
years
rawFormat
effectiveSeed
expectedTables
```

The parser is intentionally anchored to the original logger payload boundary:

```text
> Orders:
> OrdersRows:
```

This matters because the log also contains `Online orders:`. A loose parser could confuse online orders with total orders. Tests explicitly cover this ambiguity. Do not weaken this parser.

---

## 10. Authentication

Current auth is developer-first:

```csharp
AzureCliCredential
```

Fabric REST scope:

```text
https://api.fabric.microsoft.com/.default
```

Current live prerequisite:

```powershell
az login
```

Potential future improvement:

- `InteractiveBrowserCredential`
- or carefully configured `DefaultAzureCredential`
- visible login/account state in WPF

Do not remove Azure CLI auth until a replacement works for both Fabric REST and OneLake. Never persist client secrets, passwords, or raw access tokens in project files/logs.

---

## 11. Fabric REST client

Source:

```text
ContosoFabric.Fabric/Api/FabricRestClient.cs
```

Responsibilities:

- list/resolve workspaces
- list Fabric items
- ensure Lakehouses
- ensure/update Notebooks
- run notebooks
- poll notebook jobs
- long-running operation handling
- cancellation/timeouts
- 429/Retry-After handling

Important safety behavior:

- prefer workspace ID when known
- reject ambiguous duplicate workspace names
- require Fabric capacity before mutations
- retain resolved workspace ID in projects when possible

---

## 12. OneLake upload

Source:

```text
ContosoFabric.Fabric/OneLake/OneLakeRawUploader.cs
```

Endpoint:

```text
https://onelake.dfs.fabric.microsoft.com
```

SDK:

```text
Azure.Storage.Files.DataLake
```

The uploader:

- uses workspace/Lakehouse GUIDs in live orchestration
- writes only below the Fabric-managed Lakehouse `Files/raw` subtree
- uploads recursively
- supports Delta trees including `_delta_log/*.json`
- supports Parquet and CSV
- uploads `truth_manifest.json` regardless of raw format
- overwrites matching paths

Preferred GUID path style:

```text
abfss://<workspace-guid>@onelake.dfs.fabric.microsoft.com/<lakehouse-guid>/Files/...
```

Do not reintroduce name-based ABFSS addressing where GUIDs are available.

---

## 13. Bronze stage

Factory:

```text
ContosoFabric.Fabric/Notebooks/NotebookDefinitionFactory.cs
```

Bronze behavior:

1. Create/reuse Bronze Lakehouse.
2. Create/update project-specific Bronze notebook.
3. Upload raw files to `Files/raw`.
4. Read CSV/Parquet/Delta according to project format.
5. Add `__ingested_at_utc`.
6. Materialize source entities as Delta tables.
7. Count rows.
8. If `truth_manifest.json` exists, compare:
   - Bronze `orders` count vs manifest `actualOrders`
   - Bronze `orderrows` count vs manifest `actualOrderRows`
9. Write `bronze_validation_summary`.
10. Fail the notebook if truth reconciliation fails.

Legacy/manual Bronze folders without a manifest remain supported, but validation must explicitly report that generator reconciliation was skipped.

---

## 14. Silver stage

Current mapping:

```text
customer         → dim_customer
store            → dim_store
product          → dim_product
date             → dim_date
currencyexchange → dim_currencyexchange
sales            → fact_sales
orders           → fact_orders
orderrows         → fact_order_rows
```

Silver deduplicates using available business keys and writes:

```text
data_quality_summary
```

Silver is a conformance/quality layer; do not turn it into a BI-measure layer.

---

## 15. Gold stage

Gold currently produces/preserves:

```text
dim_customer
dim_store
dim_product
dim_date
dim_currencyexchange
fact_sales
fact_sales_enriched
sales_daily
sales_by_product
sales_by_store
customer_value
pipeline_validation_summary
```

`fact_sales_enriched` includes:

```text
OrderDay
NetRevenueLocal
CostLocal
GrossMarginLocal
```

Monetary aggregates remain grouped by `CurrencyCode`.

**Do not invent a reporting currency.**

Gold validation checks:

- Silver vs Gold sales row-count preservation
- duplicate Product keys
- duplicate Store keys
- duplicate Customer keys
- orphan Product references
- orphan Store references
- orphan Customer references
- detailed revenue vs `sales_daily` per currency
- detailed margin vs `sales_daily` per currency

Failures raise `RuntimeError`, which blocks downstream BI publication in a full run.

---

## 16. Direct Lake semantic model

Source:

```text
ContosoFabric.Fabric/SemanticModel/SemanticModelDefinitionFactory.cs
```

Definition format:

```text
TMDL
```

Generated parts:

```text
definition.pbism
definition/database.tmdl
definition/model.tmdl
definition/expressions.tmdl
definition/tables/Sales.tmdl
definition/tables/Product.tmdl
definition/tables/Store.tmdl
definition/tables/Customer.tmdl
definition/tables/Date.tmdl
definition/relationships.tmdl
```

Compatibility level:

```text
1702
```

Direct Lake expression:

```text
AzureStorage.DataLake(
  "https://onelake.dfs.fabric.microsoft.com/<workspace-guid>/<gold-lakehouse-guid>",
  [HierarchicalNavigation=true]
)
```

Entity mapping:

```text
Sales    → fact_sales_enriched
Product  → dim_product
Store    → dim_store
Customer → dim_customer
Date     → dim_date
```

Relationships:

```text
Sales.ProductKey  → Product.ProductKey
Sales.StoreKey    → Store.StoreKey
Sales.CustomerKey → Customer.CustomerKey
Sales.OrderDay    → Date.Date
```

Measures:

```text
Revenue Local
Gross Margin Local
Gross Margin %
Orders
Customers
Units
Average Order Value Local
```

Multi-currency guard:

```DAX
Revenue Local =
IF(
    HASONEVALUE(Sales[CurrencyCode]),
    SUM(Sales[NetRevenueLocal]),
    BLANK()
)
```

`Gross Margin Local` follows the same rule. Never remove this safeguard unless an explicit reporting-currency conversion policy is implemented.

---

## 17. PBIR report

Source:

```text
ContosoFabric.Fabric/Reports/ReportDefinitionFactory.cs
```

Definition format:

```text
PBIR
```

The report is deterministic. Stable page/visual IDs are derived from SHA-256 of project-specific strings. Do not replace them with random GUIDs.

Dataset binding:

```text
definition.pbir
  datasetReference
    byConnection
      connectionString = semanticmodelid=<semanticModelId>
```

This is intentional for Fabric REST deployment. Do not use local PBIP `byPath` binding here.

Current PBIR parts:

```text
definition.pbir
definition/report.json
definition/version.json
definition/pages/pages.json
definition/pages/<page-id>/page.json
definition/pages/<page-id>/visuals/<id>/visual.json ...
```

Current page:

```text
Sales Overview
1280 × 720
FitToPage
```

Visuals:

1. Currency slicer (`slicer`, dropdown)
2. KPI card (`cardVisual`)
3. Revenue trend (`lineChart`)
4. Revenue by Product Category (`clusteredBarChart`)
5. Revenue + Gross Margin by Store Country (`clusteredColumnChart`)

Shared Power BI base-theme metadata currently uses:

```text
CY26SU02
```

No custom theme package is bundled yet. Keep the first live report deliberately simple until live tenant acceptance is proven.

---

## 18. Fabric definition deployment

Source:

```text
ContosoFabric.Fabric/Api/FabricDefinitionDeployer.cs
```

Supported item types:

```text
SemanticModel
Report
```

Behavior:

- validate definition before send
- Base64 encode every text part
- `payloadType = InlineBase64`
- semantic model endpoint = `semanticModels`
- report endpoint = `reports`
- exact display-name idempotency
- duplicate same-name items cause failure
- existing item → `updateDefinition`
- missing item → create with full definition
- poll 202 long-running operations
- require operation ID for 202
- honor `Retry-After`
- bounded retries for 429
- 30-minute operation timeout

Critical rule: **`updateDefinition` is treated as complete-definition replacement. Always send every required part. Do not patch only changed files.**

---

## 19. Pipeline orchestration behavior

Source:

```text
ContosoFabric.Fabric/Pipeline/FabricPipelineRunner.cs
```

Main responsibilities:

- planner validation
- read-only preflight
- prepare selected Fabric items
- upload raw data
- execute selected range
- publish semantic model/report at the correct time
- report progress states to WPF

Execution states:

```text
Pending
Prepared
Running
Completed
Failed
Skipped
```

Preflight states:

```text
Pass
Warning
Fail
```

Preflight checks depend on selected range and include:

- project validity
- generator source files
- local raw files for Bronze-start
- scenario implementation
- workspace resolution
- capacity assignment
- workspace type
- Fabric item API readability
- Bronze dependency for Silver-start
- Silver dependency for Gold-start
- Gold dependency for SemanticModel-start
- SemanticModel dependency for Report-start
- currency semantics rule

Prepare behavior:

- prepares only selected stage outputs/dependencies
- if Gold and downstream BI stages are selected, SemanticModel/Report preparation is deferred until Gold executes
- if starting at SemanticModel or Report, existing upstream items are resolved directly

Do not collapse **Prepared** into **Completed**.

---

## 20. Desktop UX

Project:

```text
ContosoFabric.Desktop
```

Framework:

```text
WPF
net8.0-windows
```

Windows-only is intentional for current v1.

Current parameter surface:

```text
Project name
Business scenario
Scale preset
Exact/custom orders
Start date
History years
Raw format
Start from
Stop after
Workspace
Bronze Lakehouse
Silver Lakehouse
Gold Lakehouse
Semantic model name
Report name
```

Actions:

```text
Plan
Preflight
Generate locally
Prepare selected stages
Upload raw
Run selected range
Cancel
```

UI also provides:

- pipeline preview
- stage runtime states
- warnings
- activity log
- New/Open/Save project workflow

Keep UI operational and clear. Do not turn it into a code editor or Power BI designer.

Useful UX improvements **after live correctness**:

- native login/account indicator
- workspace/capacity detail panel
- Fabric item links
- deployment receipt/result panel
- safe cleanup/delete flow
- stage-specific run controls
- validation summary viewer

---

## 21. Reusable `.fabric.json` projects

Persistence:

```text
ContosoFabric.Core/Projects/ProjectFileService.cs
```

Examples:

```text
examples/sales-small-bronze.fabric.json
examples/sales-small-full-bi.fabric.json
```

Full example shape:

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

Keep serialization backward-compatible when adding optional fields. Do not serialize computed properties such as `IncludesGold` or `EffectiveStartDate`.

---

## 22. Current automated tests

Test project:

```text
ContosoFabric.Core.Tests
```

It references:

```text
ContosoFabric.Core
ContosoFabric.Fabric
```

Current test files:

```text
FabricDefinitionFactoryTests.cs
GenerationManifestTests.cs
NotebookDefinitionTests.cs
PipelinePlannerTests.cs
ProjectFileServiceTests.cs
```

Verified count:

```text
24 tests
24 passed
```

Coverage themes:

### Planner

- Bronze stop from Generate
- complete Generate→Report path
- Silver→Gold
- Gold-only
- SemanticModel→Report
- Report-only
- invalid ranges
- scale presets
- exact order override
- fixed-seed warning
- required semantic/report names

### Project persistence

- string enums
- generation parameters
- BI names
- round-trip equality
- invalid project rejection

### TMDL

- required definition parts
- Direct Lake GUID expression
- entity partitions
- relationships
- measures
- multi-currency guard

### PBIR

- required definition parts
- deterministic IDs/output
- semantic-model `byConnection`
- modern `cardVisual`
- visual queries
- currency slicer paired with local-currency measures

### Notebooks/validation

- Gold reconciliation contract
- Gold failure gate
- Bronze truth-manifest validation contract

### Truth manifest

- parse final generator counters
- do not confuse `Online orders:`
- JSON structure
- missing-counter failure

Do not weaken assertions simply to make CI green.

---

## 23. Files changed by PR #2

```text
.github/workflows/csharp-fabric-desktop.yml
ContosoDGV2.sln

ContosoFabric.Core.Tests/ContosoFabric.Core.Tests.csproj
ContosoFabric.Core.Tests/FabricDefinitionFactoryTests.cs
ContosoFabric.Core.Tests/GenerationManifestTests.cs
ContosoFabric.Core.Tests/GlobalUsings.cs
ContosoFabric.Core.Tests/NotebookDefinitionTests.cs
ContosoFabric.Core.Tests/PipelinePlannerTests.cs
ContosoFabric.Core.Tests/ProjectFileServiceTests.cs

ContosoFabric.Core/ContosoFabric.Core.csproj
ContosoFabric.Core/Generation/GenerationManifest.cs
ContosoFabric.Core/Generation/LegacyGeneratorAdapter.cs
ContosoFabric.Core/Models/FabricProject.cs
ContosoFabric.Core/Planning/PipelinePlanner.cs
ContosoFabric.Core/Projects/ProjectFileService.cs

ContosoFabric.Desktop/App.xaml
ContosoFabric.Desktop/App.xaml.cs
ContosoFabric.Desktop/ContosoFabric.Desktop.csproj
ContosoFabric.Desktop/GlobalUsings.cs
ContosoFabric.Desktop/MainWindow.Preflight.cs
ContosoFabric.Desktop/MainWindow.xaml
ContosoFabric.Desktop/MainWindow.xaml.cs

ContosoFabric.Fabric/Api/FabricDefinitionDeployer.cs
ContosoFabric.Fabric/Api/FabricModels.cs
ContosoFabric.Fabric/Api/FabricRestClient.cs
ContosoFabric.Fabric/ContosoFabric.Fabric.csproj
ContosoFabric.Fabric/Definitions/FabricItemDefinition.cs
ContosoFabric.Fabric/Notebooks/NotebookDefinitionFactory.cs
ContosoFabric.Fabric/OneLake/OneLakeRawUploader.cs
ContosoFabric.Fabric/Pipeline/FabricPipelineRunner.cs
ContosoFabric.Fabric/Reports/ReportDefinitionFactory.cs
ContosoFabric.Fabric/SemanticModel/SemanticModelDefinitionFactory.cs

README.md
docs/FABRIC_BUILDER_NATIVE.md
examples/sales-small-bronze.fabric.json
examples/sales-small-full-bi.fabric.json
run_fabric_builder.ps1
```

Use this as the feature-slice map.

---

## 24. Proven vs not proven

### Proven by build/tests

- complete .NET solution builds on Windows
- WPF compiles
- planner supports all stage ranges through Report
- project serialization works
- fixed-seed behavior is explicit
- truth-manifest parser is tested, including `Online orders:` ambiguity
- Bronze/Gold validation code is generated
- TMDL definition is structurally generated as expected
- PBIR definition is structurally generated as expected
- report output is deterministic
- report uses semantic-model `byConnection`
- definition deployer sends all Base64 parts
- duplicate display-name protection exists

### Not yet proven in a real tenant

Do **not** claim these are live-verified yet:

- Bronze/Silver/Gold notebook definitions accepted by the user's Fabric tenant
- notebooks successfully execute on the user's capacity
- OneLake permissions for the user's account
- TMDL create/update accepted live
- Direct Lake source expression resolves live
- TMDL column names/types all match actual Gold Delta schema live
- PBIR create/update accepted live
- `CY26SU02` shared theme metadata accepted/rendered live
- all PBIR visuals render correctly in Power BI Service
- timing/idempotency of repeated semantic/report deployment
- safe cleanup after repeated tests

The next milestone is **live tenant verification**, not another architecture rewrite.

---

## 25. Known risks / landmines

### Fixed seed
Original engine intentionally uses `Random(0)`. Do not expose arbitrary seed as if active.

### SalesBi only
Other scenarios are enum/catalog entries only. Preflight correctly blocks live execution. Do not label them implemented.

### Multi-currency
Gold monetary values are local-currency values. Do not globally sum currencies.

### TMDL/PBIR are contract-tested, not tenant-tested
The service may reveal schema/version/runtime constraints. Fix narrowly from actual API errors without discarding the current factory design.

### Full-definition updates
Do not send only changed files to `updateDefinition`.

### Prepare vs Run
Prepared means definitions/items are ready; it does not mean notebooks/data completed.

### WPF is Windows-only
Intentional. Do not migrate to Avalonia/MAUI unless explicitly requested.

### Auth UX is developer-oriented
`az login` is acceptable for v1 validation. Do not block correctness on perfect auth UX.

### No cleanup mode yet
Repeated live tests can leave Fabric items. Add a safe project-scoped cleanup flow after the first successful deployment.

### Capacity provisioning not implemented
The app currently expects an existing capacity-backed workspace. Terraform is roadmap.

---

## 26. Immediate next milestone — live Fabric verification

This is the highest-value next pass.

### Phase A — preflight only

1. `az login`
2. launch app
3. select a normal Fabric workspace on supported capacity
4. run **Preflight**
5. verify workspace resolution, capacity ID, and item API access

Do not mutate if preflight fails.

### Phase B — Tiny local generation

Use:

```text
Scenario  = SalesBi
Scale     = Tiny
RawFormat = Parquet
StartFrom = Generate
StopAfter = Generate
```

Verify:

```text
generated/data/*
truth_manifest.json
_log.log
```

Manifest counters must match final generator log counters.

### Phase C — Generate → Bronze

Use Tiny + Parquet.

Verify:

- Bronze Lakehouse create/reuse
- Bronze notebook create/update
- files visible in `Files/raw`
- truth manifest uploaded
- Bronze notebook completes
- expected Delta tables exist
- `bronze_validation_summary` exists
- orders/orderrows counts reconcile

### Phase D — Silver → Gold

Once Bronze is proven, test:

```text
StartFrom = Silver
StopAfter = Gold
```

This verifies partial execution without rerunning Bronze.

Check:

- Bronze is dependency-only
- Silver/Gold items create/reuse correctly
- `data_quality_summary` exists
- `pipeline_validation_summary` exists
- no duplicate/orphan/aggregate mismatch failures

### Phase E — SemanticModel only

Use validated Gold:

```text
StartFrom = SemanticModel
StopAfter = SemanticModel
```

Verify:

- semantic model create/update accepted
- model opens
- Direct Lake tables work
- relationships/measures exist
- single-currency measures work
- mixed-currency Revenue/Margin do not silently aggregate

### Phase F — Report only

Use existing semantic model:

```text
StartFrom = Report
StopAfter = Report
```

Verify:

- PBIR create/update accepted
- report opens
- all five visuals render
- currency slicer filters card/charts
- no visual schema/query errors
- redeploy updates same report rather than creating duplicate same-name report

### Phase G — full Tiny path

Finally test:

```text
Generate → Report
```

Do not start live debugging with 2,000,000 orders.

---

## 27. Add deployment receipts before/while live testing

A strong next implementation is a structured per-run receipt instead of relying on transient UI logs.

Recommended shape:

```json
{
  "runId": "...",
  "project": "...",
  "startedAtUtc": "...",
  "endedAtUtc": "...",
  "workspaceId": "...",
  "range": "Generate->Report",
  "items": {
    "bronzeLakehouse": {"id": "...", "name": "..."},
    "silverLakehouse": {"id": "...", "name": "..."},
    "goldLakehouse": {"id": "...", "name": "..."},
    "semanticModel": {"id": "...", "name": "..."},
    "report": {"id": "...", "name": "..."}
  },
  "jobs": [...],
  "validation": {...},
  "status": "completed|failed|cancelled",
  "error": null
}
```

Suggested location:

```text
generated/runs/<run-id>/receipt.json
```

Never store access tokens.

Useful diagnostics to capture:

- workspace ID/name
- Fabric item IDs/names/types
- notebook job instance IDs
- root activity IDs
- start/end timestamps
- stage where failure occurred
- HTTP status
- safe API response body/error code
- generated local folder
- validation outcome

---

## 28. Prioritized backlog

### P0 — preserve baseline

- checkout active branch
- run restore/build/tests
- confirm green baseline
- do not refactor architecture first

### P1 — live-test observability

Add:

- structured deployment receipts
- better error detail in WPF
- item IDs in result/activity log
- job IDs/root activity IDs
- stage + HTTP status/error detail
- copy-to-clipboard diagnostics

Keep secrets out.

### P2 — first real Fabric integration pass

Execute the phased Tiny tests above. Patch from actual service errors. Add regression tests where possible.

### P3 — safe cleanup mode

After successful live deployment, add project-scoped cleanup.

Desired behavior:

```text
Preview generated items
  ↓
show workspace + exact item IDs
  ↓
explicit confirmation
  ↓
delete only selected/project-owned artifacts
```

Requirements:

- dry-run/preview by default
- no broad prefix-based mass deletion
- allow cleanup using saved receipt
- do not delete unrelated existing items

### P4 — native auth UX

After pipeline correctness stabilizes, consider:

- `InteractiveBrowserCredential`
- `DefaultAzureCredential` with explicit behavior

Need correct token audiences for both Fabric REST and OneLake.

### P5 — workspace/capacity provisioning

Terraform or supported Fabric APIs only after content deployment is stable.

Keep separation:

```text
Project intent
  ↓
Provisioning plan
  ↓
Terraform/Fabric infrastructure APIs
  ↓
Existing content deployment path
```

Do not entangle Terraform state with notebook/TMDL/PBIR factories.

### P6 — reporting currency policy

Potential project field:

```text
ReportingCurrency = null | USD | EUR | CHF | ...
```

If implemented:

- use `currencyexchange`
- define rate date semantics explicitly
- keep Local measures separately
- add reconciliation tests
- never silently default to a currency

### P7 — richer PBIR templates

Only after current report works live.

Possible pages:

- Executive Sales
- Product
- Store/Geography
- Customer
- Data Quality / Pipeline Validation

Keep IDs deterministic and factories source-controlled.

### P8 — additional scenarios

Suggested order:

1. Customer Experience & Delivery
2. Data Engineering / Data Quality
3. ML Customer Dissatisfaction

Each scenario needs a real contract:

- source/generation behavior
- scenario-specific data entities/features
- validation rules
- Silver/Gold output
- model/report outputs where appropriate
- tests

Do not merely enable enum values in the UI.

---

## 29. Maintainable source ownership

The user specifically wants future updates to remain easy to maintain and wants to know which feature comes from where.

Keep ownership boundaries explicit:

```text
DatabaseGenerator/*
= upstream/original generator logic

ContosoFabric.Core/*
= app-owned domain/planning/project/generator-adapter/lineage

ContosoFabric.Fabric/Api/*
= Fabric REST transport/catalog/definition deployment

ContosoFabric.Fabric/OneLake/*
= OneLake transfer

ContosoFabric.Fabric/Notebooks/*
= generated Bronze/Silver/Gold Fabric notebooks

ContosoFabric.Fabric/SemanticModel/*
= generated TMDL

ContosoFabric.Fabric/Reports/*
= generated PBIR

ContosoFabric.Desktop/*
= WPF UI only

ContosoFabric.Core.Tests/*
= executable contract evidence
```

If borrowing from another repo later:

- record repo URL
- identify exact file/concept reused
- isolate adaptation in an appropriate component
- do not scatter copied code
- add `docs/UPSTREAM_SOURCES.md` if reuse grows

---

## 30. Things explicitly not to do next

Do **not**:

- replace WPF with React
- add a web server just to host this tool
- rewrite generator in Python
- add Fabric MCP as mandatory runtime
- build DAX editing features
- turn app into Tabular Editor/DAX Studio replacement
- add many report pages before PBIR v1 works live
- add Terraform before content deployment works live
- add three scenarios at once
- remove multi-currency safeguards for prettier totals
- weaken tests to make CI pass
- claim a Fabric stage works live because C# compiles
- merge PR #2 before a deliberate live Fabric validation pass unless user explicitly chooses to

---

## 31. Branch/commit discipline

Use PR #2 for fixes required to prove the current v1 slice.

Suitable examples:

```text
fix(fabric): correct semantic model definition after live API test
fix(report): align PBIR visual roles with service schema
feat(observability): persist deployment receipt
feat(cleanup): add safe cleanup preview
```

Use separate branches for large independent features:

```text
feat/terraform-provisioning-v1
feat/customer-experience-scenario-v1
feat/native-auth-v1
```

Do not mix large unrelated roadmap work into PR #2 while v1 live behavior is still being validated.

---

## 32. Definition of done for PR #2

### Build/test

- full solution restores/builds
- 0 errors
- preferably 0 warnings
- all automated tests pass

### Live Fabric, Tiny SalesBi

- Preflight passes
- Generate succeeds
- truth manifest is created
- raw upload succeeds
- Bronze notebook succeeds
- Bronze validation passes
- Silver notebook succeeds
- Silver quality summary exists
- Gold notebook succeeds
- Gold reconciliation passes
- Direct Lake semantic model creates/updates and opens
- relationships/measures work
- PBIR report creates/updates and opens
- slicer prevents meaningless mixed-currency totals
- rerun is idempotent enough not to create duplicate same-name items

### Diagnostics

- failures expose stage/item/job/API context
- no credentials/tokens persisted

### Documentation

- README and architecture doc match actual behavior
- examples match JSON schema
- no roadmap feature described as implemented

---

## 33. Recommended first Codex task

Use this exact bounded task after reading/auditing the repo:

> Audit `feat/csharp-fabric-desktop-v1` without redesigning it. Run the existing Windows/.NET restore, Release build, and test suite. Then improve live-test readiness for the existing SalesBi Generate→Report path by adding a structured per-run deployment receipt and stronger diagnostic surfacing: workspace/item IDs, job IDs/root activity IDs, stage, HTTP status/error body where safe. Preserve the existing C# generator, WPF UI, Fabric REST/OneLake architecture, deterministic PBIR IDs, full-definition TMDL/PBIR deployment, truth-manifest validation, Gold publication gate, and multi-currency guard. Add tests for new non-UI logic. Do not implement Terraform or new scenarios yet. After the change, rerun the full build/tests and document exactly how to perform the first Tiny live Fabric validation.

This is intentionally bounded. The objective is to make the first real Fabric execution produce actionable evidence.

---

## 34. Quick status table

| Area | State | Notes |
|---|---|---|
| Original C# generator reuse | Implemented | Direct Engine call; preserve |
| WPF desktop | Implemented | Windows .NET 8 |
| Project JSON | Implemented | New/Open/Save + examples |
| Scale/exact orders/date/years | Implemented | Planner validated |
| CSV raw | Implemented in code | Live tenant still unverified |
| Parquet raw | Implemented in code | Preferred first live test |
| Delta raw | Implemented in code | Recursive upload incl. `_delta_log` |
| StartFrom / StopAfter | Implemented | All six stages |
| Workspace discovery | Implemented | REST + ambiguity protection |
| Capacity preflight | Implemented | Existing capacity required |
| OneLake upload | Implemented | ADLS .NET SDK |
| Bronze | Implemented in code | Live execution pending |
| Truth manifest | Implemented/tested | Bronze reconciliation |
| Silver | Implemented in code | Live execution pending |
| Silver quality summary | Implemented | Notebook generated |
| Gold | Implemented in code | Live execution pending |
| Gold reconciliation | Implemented/tested contract | BI publication gate |
| Direct Lake TMDL | Implemented/tested contract | Live Fabric acceptance pending |
| PBIR report | Implemented/tested contract | Live rendering pending |
| SemanticModel→Report / Report→Report | Implemented | Partial metadata runs |
| Azure CLI auth | Implemented | Developer-first |
| Native sign-in | Roadmap | After live stabilization |
| Deployment receipt | Recommended next | High priority |
| Cleanup mode | Roadmap/high priority | After first live success |
| Terraform provisioning | Roadmap | Separate concern |
| Reporting currency conversion | Roadmap | Explicit policy only |
| CustomerExperience | Catalogued only | Not live implemented |
| DataQuality scenario | Catalogued only | Not live implemented |
| ML dissatisfaction | Catalogued only | Not live implemented |
| Rich multi-page PBIR | Roadmap | After PBIR v1 live test |

---

## 35. Final instruction to Codex

Continue from the working system; do not restart from an imagined cleaner architecture.

Current project value already exists across:

```text
original deterministic C# generator
        +
native WPF configuration
        +
partial Fabric stage orchestration
        +
OneLake landing
        +
Bronze/Silver/Gold notebooks
        +
truth/quality/reconciliation gates
        +
Direct Lake TMDL generation
        +
PBIR generation
```

The next engineering value is:

```text
prove live
→ improve diagnostics from actual evidence
→ add safe cleanup
→ stabilize auth
→ then extend provisioning/scenarios/reports
```

Use evidence-driven development:

```text
implement
→ build
→ test
→ live verify where required
→ capture exact failure/evidence
→ patch narrowly
→ add regression test
→ update docs
```

Never report a Fabric stage as working merely because local C# compiles.

---

## 36. Key links

Repository:
https://github.com/julian-passebecq/Contoso_Data_Fabric

Active PR:
https://github.com/julian-passebecq/Contoso_Data_Fabric/pull/2

Active branch:
https://github.com/julian-passebecq/Contoso_Data_Fabric/tree/feat/csharp-fabric-desktop-v1

Architecture doc:
https://github.com/julian-passebecq/Contoso_Data_Fabric/blob/feat/csharp-fabric-desktop-v1/docs/FABRIC_BUILDER_NATIVE.md

Verified functional source head:
https://github.com/julian-passebecq/Contoso_Data_Fabric/commit/2a9488cabce23e237ceae2f7b75fc552c73f3c2f

Current branch head at handoff:
https://github.com/julian-passebecq/Contoso_Data_Fabric/commit/47a65b75e0ec126c0c8a0372a1c27709659aa317

---

**End of handoff.**
