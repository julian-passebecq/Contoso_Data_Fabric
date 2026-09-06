using System.Text.Json;
using ContosoFabric.Core.Models;

namespace ContosoFabric.Fabric.Notebooks;

public static class NotebookDefinitionFactory
{
    public static string Bronze(FabricProject project, string workspaceId, string bronzeLakehouseId)
    {
        var code = """
from pyspark.sql import functions as F

workspace_id = "__WORKSPACE_ID__"
bronze_lakehouse_id = "__BRONZE_ID__"
landing_format = "__FORMAT__"
raw_root = f"abfss://{workspace_id}@onelake.dfs.fabric.microsoft.com/{bronze_lakehouse_id}/Files/raw"
table_root = f"abfss://{workspace_id}@onelake.dfs.fabric.microsoft.com/{bronze_lakehouse_id}/Tables"

tables = ["customer", "store", "product", "date", "currencyexchange", "sales", "orders", "orderrows"]

for table in tables:
    if landing_format == "parquet":
        df = spark.read.parquet(f"{raw_root}/{table}.parquet")
    elif landing_format == "csv":
        df = spark.read.option("header", True).option("inferSchema", True).csv(f"{raw_root}/{table}.csv")
    elif landing_format == "delta":
        df = spark.read.format("delta").load(f"{raw_root}/{table}")
    else:
        raise ValueError(f"Unsupported landing format: {landing_format}")

    df = df.withColumn("__ingested_at_utc", F.current_timestamp())
    df.write.format("delta").mode("overwrite").option("overwriteSchema", "true").save(f"{table_root}/{table}")
    print(f"Bronze {table}: {df.count()} rows")
"""
            .Replace("__WORKSPACE_ID__", workspaceId)
            .Replace("__BRONZE_ID__", bronzeLakehouseId)
            .Replace("__FORMAT__", project.RawFormat.ToString().ToLowerInvariant());

        return BuildNotebook(code);
    }

    public static string Silver(string workspaceId, string bronzeLakehouseId, string silverLakehouseId)
    {
        var code = """
from pyspark.sql import functions as F

workspace_id = "__WORKSPACE_ID__"
bronze_lakehouse_id = "__BRONZE_ID__"
silver_lakehouse_id = "__SILVER_ID__"
bronze_root = f"abfss://{workspace_id}@onelake.dfs.fabric.microsoft.com/{bronze_lakehouse_id}/Tables"
silver_root = f"abfss://{workspace_id}@onelake.dfs.fabric.microsoft.com/{silver_lakehouse_id}/Tables"

entities = {
    "customer": ("dim_customer", ["CustomerKey"]),
    "store": ("dim_store", ["StoreKey"]),
    "product": ("dim_product", ["ProductKey"]),
    "date": ("dim_date", ["Date"]),
    "currencyexchange": ("dim_currencyexchange", ["Date", "FromCurrency", "ToCurrency"]),
    "sales": ("fact_sales", ["OrderKey", "LineNumber"]),
    "orders": ("fact_orders", ["OrderKey"]),
    "orderrows": ("fact_order_rows", ["OrderKey", "LineNumber"]),
}

quality_rows = []
for source, (target, requested_keys) in entities.items():
    df = spark.read.format("delta").load(f"{bronze_root}/{source}")
    row_count = df.count()
    keys = [key for key in requested_keys if key in df.columns]
    duplicate_count = 0
    if keys:
        clean = df.dropDuplicates(keys)
        clean_count = clean.count()
        duplicate_count = row_count - clean_count
        df = clean

    df = df.withColumn("__silver_loaded_at_utc", F.current_timestamp())
    df.write.format("delta").mode("overwrite").option("overwriteSchema", "true").save(f"{silver_root}/{target}")
    quality_rows.append((source, target, row_count, duplicate_count, len(df.columns)))
    print(f"Silver {target}: {row_count} input rows, {duplicate_count} duplicate rows removed")

quality = spark.createDataFrame(
    quality_rows,
    ["SourceTable", "TargetTable", "InputRows", "DuplicatesRemoved", "ColumnCount"]
).withColumn("CheckedAtUtc", F.current_timestamp())
quality.write.format("delta").mode("overwrite").save(f"{silver_root}/data_quality_summary")
"""
            .Replace("__WORKSPACE_ID__", workspaceId)
            .Replace("__BRONZE_ID__", bronzeLakehouseId)
            .Replace("__SILVER_ID__", silverLakehouseId);

        return BuildNotebook(code);
    }

    public static string Gold(string workspaceId, string silverLakehouseId, string goldLakehouseId)
    {
        var code = """
from pyspark.sql import functions as F

workspace_id = "__WORKSPACE_ID__"
silver_lakehouse_id = "__SILVER_ID__"
gold_lakehouse_id = "__GOLD_ID__"
silver_root = f"abfss://{workspace_id}@onelake.dfs.fabric.microsoft.com/{silver_lakehouse_id}/Tables"
gold_root = f"abfss://{workspace_id}@onelake.dfs.fabric.microsoft.com/{gold_lakehouse_id}/Tables"

# Preserve conformed dimensions and the detailed sales fact in Gold.
for table in ["dim_customer", "dim_store", "dim_product", "dim_date", "dim_currencyexchange", "fact_sales"]:
    df = spark.read.format("delta").load(f"{silver_root}/{table}")
    df.write.format("delta").mode("overwrite").option("overwriteSchema", "true").save(f"{gold_root}/{table}")

sales = spark.read.format("delta").load(f"{silver_root}/fact_sales")
sales_enriched = (
    sales
    .withColumn("OrderDay", F.to_date("OrderDate"))
    .withColumn("NetRevenueLocal", F.col("Quantity") * F.col("NetPrice"))
    .withColumn("CostLocal", F.col("Quantity") * F.col("UnitCost"))
    .withColumn("GrossMarginLocal", F.col("Quantity") * (F.col("NetPrice") - F.col("UnitCost")))
)

sales_enriched.write.format("delta").mode("overwrite").option("overwriteSchema", "true").save(f"{gold_root}/fact_sales_enriched")

sales_daily = (
    sales_enriched
    .groupBy("OrderDay", "CurrencyCode")
    .agg(
        F.sum("NetRevenueLocal").alias("NetRevenueLocal"),
        F.sum("GrossMarginLocal").alias("GrossMarginLocal"),
        F.sum("Quantity").alias("Units"),
        F.countDistinct("OrderKey").alias("Orders"),
        F.countDistinct("CustomerKey").alias("Customers")
    )
)
sales_daily.write.format("delta").mode("overwrite").save(f"{gold_root}/sales_daily")

sales_by_product = (
    sales_enriched
    .groupBy("ProductKey", "CurrencyCode")
    .agg(
        F.sum("NetRevenueLocal").alias("NetRevenueLocal"),
        F.sum("GrossMarginLocal").alias("GrossMarginLocal"),
        F.sum("Quantity").alias("Units"),
        F.countDistinct("OrderKey").alias("Orders")
    )
)
sales_by_product.write.format("delta").mode("overwrite").save(f"{gold_root}/sales_by_product")

sales_by_store = (
    sales_enriched
    .groupBy("StoreKey", "CurrencyCode")
    .agg(
        F.sum("NetRevenueLocal").alias("NetRevenueLocal"),
        F.sum("GrossMarginLocal").alias("GrossMarginLocal"),
        F.sum("Quantity").alias("Units"),
        F.countDistinct("OrderKey").alias("Orders")
    )
)
sales_by_store.write.format("delta").mode("overwrite").save(f"{gold_root}/sales_by_store")

customer_value = (
    sales_enriched
    .groupBy("CustomerKey", "CurrencyCode")
    .agg(
        F.sum("NetRevenueLocal").alias("LifetimeRevenueLocal"),
        F.sum("GrossMarginLocal").alias("LifetimeGrossMarginLocal"),
        F.countDistinct("OrderKey").alias("Orders"),
        F.max("OrderDate").alias("LastOrderDate")
    )
)
customer_value.write.format("delta").mode("overwrite").save(f"{gold_root}/customer_value")

print(f"Gold sales_daily: {sales_daily.count()} rows")
print(f"Gold sales_by_product: {sales_by_product.count()} rows")
print(f"Gold sales_by_store: {sales_by_store.count()} rows")
print(f"Gold customer_value: {customer_value.count()} rows")
"""
            .Replace("__WORKSPACE_ID__", workspaceId)
            .Replace("__SILVER_ID__", silverLakehouseId)
            .Replace("__GOLD_ID__", goldLakehouseId);

        return BuildNotebook(code);
    }

    public static string NotebookName(FabricProject project, PipelineStage stage)
    {
        var safeProject = new string(project.Name
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
        if (safeProject.Length > 80)
            safeProject = safeProject[..80];
        return $"CF_{safeProject}_{stage}";
    }

    private static string BuildNotebook(string code)
    {
        var source = code
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line + "\n")
            .ToArray();

        var notebook = new
        {
            nbformat = 4,
            nbformat_minor = 5,
            cells = new object[]
            {
                new
                {
                    cell_type = "markdown",
                    metadata = new { },
                    source = new[] { "# Generated by Contoso Fabric Builder\n", "This notebook is idempotently regenerated from the desktop application.\n" }
                },
                new
                {
                    cell_type = "code",
                    execution_count = (int?)null,
                    metadata = new
                    {
                        microsoft = new { language = "python", language_group = "synapse_pyspark" }
                    },
                    outputs = Array.Empty<object>(),
                    source
                }
            },
            metadata = new
            {
                kernelspec = new { display_name = "Synapse PySpark", language = "Python", name = "synapse_pyspark" },
                language_info = new { name = "python" }
            }
        };

        return JsonSerializer.Serialize(notebook, new JsonSerializerOptions { WriteIndented = true });
    }
}
