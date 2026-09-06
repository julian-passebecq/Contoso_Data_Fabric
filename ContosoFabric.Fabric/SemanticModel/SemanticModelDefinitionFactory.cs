using System.Text.Json;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Definitions;

namespace ContosoFabric.Fabric.SemanticModel;

public static class SemanticModelDefinitionFactory
{
    public static FabricItemDefinition Build(FabricProject project, string workspaceId, string goldLakehouseId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID is required for Direct Lake TMDL.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(goldLakehouseId))
            throw new ArgumentException("Gold Lakehouse ID is required for Direct Lake TMDL.", nameof(goldLakehouseId));

        var pbism = JsonSerializer.Serialize(new
        {
            schema = "https://developer.microsoft.com/json-schemas/fabric/item/semanticModel/definitionProperties/1.0.0/schema.json",
            version = "4.2",
            settings = new { qnaEnabled = true }
        }, new JsonSerializerOptions { WriteIndented = true })
        .Replace("\"schema\"", "\"$schema\"");

        var database = """
database
	compatibilityLevel: 1702
	compatibilityMode: powerBI
	language: 1033
""";

        var model = """
model Model
	culture: en-US
	sourceQueryCulture: en-US

ref expression DirectLakeSource
ref table Sales
ref table Product
ref table Store
ref table Customer
ref table Date
""";

        var expression = $$"""
expression DirectLakeSource =
	let
		Source = AzureStorage.DataLake("https://onelake.dfs.fabric.microsoft.com/{{workspaceId}}/{{goldLakehouseId}}", [HierarchicalNavigation=true])
	in
		Source
""";

        var sales = """
table Sales

	/// Revenue in the currently selected local currency. Returns blank when multiple currencies are in filter context.
	measure 'Revenue Local' = IF(HASONEVALUE(Sales[CurrencyCode]), SUM(Sales[NetRevenueLocal]), BLANK())
		formatString: #,##0.00

	/// Gross margin in the currently selected local currency. Returns blank when multiple currencies are in filter context.
	measure 'Gross Margin Local' = IF(HASONEVALUE(Sales[CurrencyCode]), SUM(Sales[GrossMarginLocal]), BLANK())
		formatString: #,##0.00

	measure 'Gross Margin %' = DIVIDE([Gross Margin Local], [Revenue Local])
		formatString: 0.00%

	measure Orders = DISTINCTCOUNT(Sales[OrderKey])
		formatString: #,##0

	measure Customers = DISTINCTCOUNT(Sales[CustomerKey])
		formatString: #,##0

	measure Units = SUM(Sales[Quantity])
		formatString: #,##0

	measure 'Average Order Value Local' = DIVIDE([Revenue Local], [Orders])
		formatString: #,##0.00

	column OrderKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: OrderKey

	column LineNumber
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: LineNumber

	column OrderDate
		dataType: dateTime
		summarizeBy: none
		sourceColumn: OrderDate

	column OrderDay
		dataType: dateTime
		summarizeBy: none
		sourceColumn: OrderDay

	column DeliveryDate
		dataType: dateTime
		summarizeBy: none
		sourceColumn: DeliveryDate

	column CustomerKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: CustomerKey

	column StoreKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: StoreKey

	column ProductKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: ProductKey

	column Quantity
		dataType: int64
		summarizeBy: sum
		sourceColumn: Quantity

	column CurrencyCode
		dataType: string
		summarizeBy: none
		sourceColumn: CurrencyCode

	column NetRevenueLocal
		dataType: decimal
		summarizeBy: sum
		sourceColumn: NetRevenueLocal

	column CostLocal
		dataType: decimal
		summarizeBy: sum
		sourceColumn: CostLocal

	column GrossMarginLocal
		dataType: decimal
		summarizeBy: sum
		sourceColumn: GrossMarginLocal

	partition Sales = entity
		mode: directLake
		source
			entityName: fact_sales_enriched
			expressionSource: DirectLakeSource
""";

        var product = """
table Product

	column ProductKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: ProductKey

	column ProductCode
		dataType: string
		sourceColumn: ProductCode

	column ProductName
		dataType: string
		sourceColumn: ProductName

	column Manufacturer
		dataType: string
		sourceColumn: Manufacturer

	column Brand
		dataType: string
		sourceColumn: Brand

	column Color
		dataType: string
		sourceColumn: Color

	column CategoryName
		dataType: string
		sourceColumn: CategoryName

	column SubCategoryName
		dataType: string
		sourceColumn: SubCategoryName

	partition Product = entity
		mode: directLake
		source
			entityName: dim_product
			expressionSource: DirectLakeSource
""";

        var store = """
table Store

	column StoreKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: StoreKey

	column StoreCode
		dataType: int64
		summarizeBy: none
		sourceColumn: StoreCode

	column CountryCode
		dataType: string
		sourceColumn: CountryCode

	column CountryName
		dataType: string
		sourceColumn: CountryName

	column State
		dataType: string
		sourceColumn: State

	column Description
		dataType: string
		sourceColumn: Description

	column Status
		dataType: string
		sourceColumn: Status

	partition Store = entity
		mode: directLake
		source
			entityName: dim_store
			expressionSource: DirectLakeSource
""";

        var customer = """
table Customer

	column CustomerKey
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: CustomerKey

	column Continent
		dataType: string
		sourceColumn: Continent

	column Gender
		dataType: string
		sourceColumn: Gender

	column GivenName
		dataType: string
		sourceColumn: GivenName

	column Surname
		dataType: string
		sourceColumn: Surname

	column City
		dataType: string
		sourceColumn: City

	column State
		dataType: string
		sourceColumn: State

	column Country
		dataType: string
		sourceColumn: Country

	column CountryFull
		dataType: string
		sourceColumn: CountryFull

	column Age
		dataType: int64
		summarizeBy: none
		sourceColumn: Age

	column Occupation
		dataType: string
		sourceColumn: Occupation

	partition Customer = entity
		mode: directLake
		source
			entityName: dim_customer
			expressionSource: DirectLakeSource
""";

        var date = """
table Date

	column Date
		dataType: dateTime
		summarizeBy: none
		sourceColumn: Date

	column Year
		dataType: int64
		summarizeBy: none
		sourceColumn: Year

	column YearQuarter
		dataType: string
		sourceColumn: YearQuarter

	column Quarter
		dataType: string
		sourceColumn: Quarter

	column YearMonth
		dataType: string
		sourceColumn: YearMonth

	column YearMonthNumber
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: YearMonthNumber

	column Month
		dataType: string
		sourceColumn: Month

	column MonthNumber
		dataType: int64
		isHidden
		summarizeBy: none
		sourceColumn: MonthNumber

	partition Date = entity
		mode: directLake
		source
			entityName: dim_date
			expressionSource: DirectLakeSource
""";

        var relationships = """
relationship 'Sales Product'
	fromColumn: Sales.ProductKey
	toColumn: Product.ProductKey

relationship 'Sales Store'
	fromColumn: Sales.StoreKey
	toColumn: Store.StoreKey

relationship 'Sales Customer'
	fromColumn: Sales.CustomerKey
	toColumn: Customer.CustomerKey

relationship 'Sales Date'
	fromColumn: Sales.OrderDay
	toColumn: Date.Date
""";

        return new FabricItemDefinition("TMDL",
        [
            new("definition.pbism", pbism),
            new("definition/database.tmdl", database),
            new("definition/model.tmdl", model),
            new("definition/expressions.tmdl", expression),
            new("definition/tables/Sales.tmdl", sales),
            new("definition/tables/Product.tmdl", product),
            new("definition/tables/Store.tmdl", store),
            new("definition/tables/Customer.tmdl", customer),
            new("definition/tables/Date.tmdl", date),
            new("definition/relationships.tmdl", relationships)
        ]).Validate();
    }
}
