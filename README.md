# Contoso Data Generator V2 + Fabric Builder

This repository keeps the original **Contoso Data Generator V2** C# engine and adds a native .NET 8 desktop application for parameterized Microsoft Fabric pipelines.

## Fabric Builder

Active implementation: `ContosoFabric.Desktop` + `ContosoFabric.Core` + `ContosoFabric.Fabric`.

The desktop application can now run the Sales/BI vertical slice through:

```text
Generate locally -> Bronze -> Silver -> Gold
```

Each endpoint is selectable with **Stop after**, so the same tool can generate files only, build Bronze only, or execute the complete medallion path through Gold.

The Fabric path is native C#: Fabric REST APIs for workspace/item/notebook orchestration and the OneLake ADLS-compatible .NET SDK for raw-file upload. The existing `DatabaseGenerator.Engine` remains the data-generation motor.

See [`docs/FABRIC_BUILDER_NATIVE.md`](docs/FABRIC_BUILDER_NATIVE.md) for the architecture, current stage behavior and setup.

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
