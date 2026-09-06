# Contoso Fabric Builder V1

This is the orchestration layer around the existing SQLBI Contoso Data Generator V2 in the repository.

The product model is:

`business scenario -> scale/format -> Fabric depth -> generated bundle -> optional deployment`

## Implemented now

- `sales-bi` business scenario.
- Sizes: `tiny`, `small`, `medium`, `large`.
- Raw formats: Parquet, CSV, legacy Delta-table output.
- Partial depth: `generate`, `bronze`, `silver`, `gold`.
- Generates Fabric Git-compatible Lakehouse and Notebook item folders.
- Runs the existing deterministic C# generator (`Random(0)`).
- Uploads raw files to OneLake Bronze using ADLS-compatible APIs.
- Publishes generated Lakehouse/Notebook items using Microsoft's `fabric-cicd` library.
- Dry-run plan before doing anything to Fabric.

## Deliberately not claimed as implemented yet

- Direct Lake semantic-model/TMDL compilation.
- PBIR report compilation.
- Notebook execution/orchestration after deployment.
- Truth-manifest KPI reconciliation.
- Terraform apply from the UI.
- Customer Experience, Data Quality and ML scenario generators.

These stages already exist in the planner so we can add them without changing the project contract.

## Install

From repository root:

```powershell
py -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e .\fabric_builder
```

For live Fabric deployment/upload:

```powershell
pip install -e ".\fabric_builder[fabric]"
az login
```

## First run

```powershell
Copy-Item .\fabric_builder\examples\project.sales-bi.json .\project.json
contoso-fabric --project .\project.json plan
contoso-fabric --project .\project.json compile --output .\generated --stop-after bronze
contoso-fabric --project .\project.json generate --repo-root . --output .\generated\data
```

Nothing is sent to Fabric by `plan`, `compile` or `generate`.

To land raw files only:

```powershell
contoso-fabric --project .\project.json upload-bronze --data .\generated\data
```

To publish the generated Lakehouse and Notebook items:

```powershell
contoso-fabric --project .\project.json deploy-items --fabric-dir .\generated\fabric
```

## How partial pipelines work

`stopAfter=bronze` creates only Bronze Lakehouse + Bronze load notebook. `stopAfter=silver` adds Silver. `stopAfter=gold` adds the Gold aggregate. The same project file therefore supports a one-step demo or a longer medallion pipeline without separate applications.

## Authentication

V1 uses `AzureCliCredential` for live operations, so the developer runs `az login` first. No secrets belong in `project.json` or Git.
