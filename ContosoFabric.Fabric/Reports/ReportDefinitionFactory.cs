using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContosoFabric.Core.Models;
using ContosoFabric.Fabric.Definitions;

namespace ContosoFabric.Fabric.Reports;

public static class ReportDefinitionFactory
{
    private const string VisualSchema = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/visualContainer/2.9.0/schema.json";

    public static FabricItemDefinition Build(FabricProject project, string semanticModelId)
    {
        if (string.IsNullOrWhiteSpace(semanticModelId))
            throw new ArgumentException("Semantic model ID is required for a PBIR report binding.", nameof(semanticModelId));

        var pageId = StableHex($"{project.Name}:sales-overview", 20);
        var slicerId = StableHex($"{project.Name}:currency-slicer", 20);
        var cardId = StableHex($"{project.Name}:kpi-card", 20);
        var trendId = StableHex($"{project.Name}:revenue-trend", 20);
        var productId = StableHex($"{project.Name}:product-category", 20);
        var storeId = StableHex($"{project.Name}:store-country", 20);

        var definitionPbir = Json(new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json",
            ["version"] = "4.0",
            ["datasetReference"] = new JsonObject
            {
                ["byConnection"] = new JsonObject
                {
                    ["connectionString"] = $"semanticmodelid={semanticModelId}"
                }
            }
        });

        var report = Json(new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/report/3.3.0/schema.json",
            ["themeCollection"] = new JsonObject
            {
                ["baseTheme"] = new JsonObject
                {
                    ["name"] = "CY24SU10",
                    ["reportVersionAtImport"] = new JsonObject
                    {
                        ["visual"] = "2.6.0",
                        ["report"] = "3.1.0",
                        ["page"] = "2.3.0"
                    },
                    ["type"] = "SharedResources"
                }
            },
            ["reportSource"] = "Default"
        });

        var version = Json(new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/versionMetadata/1.0.0/schema.json",
            ["version"] = "2.0.0"
        });

        var pages = Json(new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/pagesMetadata/1.1.0/schema.json",
            ["pageOrder"] = new JsonArray(pageId),
            ["activePageName"] = pageId
        });

        var page = Json(new JsonObject
        {
            ["$schema"] = "https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.1.0/schema.json",
            ["name"] = pageId,
            ["displayName"] = "Sales Overview",
            ["displayOption"] = "FitToPage",
            ["height"] = 720,
            ["width"] = 1280
        });

        var slicer = Visual(
            slicerId, 20, 24, 80, 200, 1000,
            new JsonObject
            {
                ["visualType"] = "slicer",
                ["query"] = QueryState(new Dictionary<string, JsonArray>
                {
                    ["Values"] = new JsonArray(ColumnProjection("Sales", "CurrencyCode"))
                }),
                ["objects"] = new JsonObject
                {
                    ["data"] = new JsonArray(new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["mode"] = Literal("'Dropdown'")
                        }
                    }),
                    ["header"] = new JsonArray(new JsonObject
                    {
                        ["properties"] = new JsonObject
                        {
                            ["show"] = Literal("true"),
                            ["text"] = Literal("'Currency'")
                        }
                    })
                }
            });

        var card = Visual(
            cardId, 240, 24, 120, 1020, 2000,
            new JsonObject
            {
                ["visualType"] = "cardVisual",
                ["query"] = QueryState(new Dictionary<string, JsonArray>
                {
                    ["Data"] = new JsonArray(
                        MeasureProjection("Sales", "Revenue Local"),
                        MeasureProjection("Sales", "Gross Margin Local"),
                        MeasureProjection("Sales", "Gross Margin %"),
                        MeasureProjection("Sales", "Orders"),
                        MeasureProjection("Sales", "Customers"),
                        MeasureProjection("Sales", "Units"))
                })
            });

        var trend = Visual(
            trendId, 20, 168, 300, 760, 3000,
            new JsonObject
            {
                ["visualType"] = "lineChart",
                ["query"] = QueryState(new Dictionary<string, JsonArray>
                {
                    ["Category"] = new JsonArray(ColumnProjection("Sales", "OrderDay", active: true)),
                    ["Y"] = new JsonArray(MeasureProjection("Sales", "Revenue Local"))
                })
            });

        var byProduct = Visual(
            productId, 800, 168, 300, 460, 4000,
            new JsonObject
            {
                ["visualType"] = "clusteredBarChart",
                ["query"] = QueryState(new Dictionary<string, JsonArray>
                {
                    ["Category"] = new JsonArray(ColumnProjection("Product", "CategoryName", active: true)),
                    ["Y"] = new JsonArray(MeasureProjection("Sales", "Revenue Local"))
                })
            });

        var byStore = Visual(
            storeId, 20, 488, 210, 1240, 5000,
            new JsonObject
            {
                ["visualType"] = "clusteredColumnChart",
                ["query"] = QueryState(new Dictionary<string, JsonArray>
                {
                    ["Category"] = new JsonArray(ColumnProjection("Store", "CountryName", active: true)),
                    ["Y"] = new JsonArray(
                        MeasureProjection("Sales", "Revenue Local"),
                        MeasureProjection("Sales", "Gross Margin Local"))
                })
            });

        return new FabricItemDefinition("PBIR",
        [
            new("definition.pbir", definitionPbir),
            new("definition/report.json", report),
            new("definition/version.json", version),
            new("definition/pages/pages.json", pages),
            new($"definition/pages/{pageId}/page.json", page),
            new($"definition/pages/{pageId}/visuals/{slicerId}/visual.json", slicer),
            new($"definition/pages/{pageId}/visuals/{cardId}/visual.json", card),
            new($"definition/pages/{pageId}/visuals/{trendId}/visual.json", trend),
            new($"definition/pages/{pageId}/visuals/{productId}/visual.json", byProduct),
            new($"definition/pages/{pageId}/visuals/{storeId}/visual.json", byStore)
        ]).Validate();
    }

    private static string Visual(string name, int x, int y, int height, int width, int z, JsonObject visual)
        => Json(new JsonObject
        {
            ["$schema"] = VisualSchema,
            ["name"] = name,
            ["position"] = new JsonObject
            {
                ["x"] = x,
                ["y"] = y,
                ["z"] = z,
                ["height"] = height,
                ["width"] = width,
                ["tabOrder"] = z
            },
            ["visual"] = visual
        });

    private static JsonObject QueryState(IReadOnlyDictionary<string, JsonArray> roles)
    {
        var state = new JsonObject();
        foreach (var role in roles)
            state[role.Key] = new JsonObject { ["projections"] = role.Value };
        return new JsonObject { ["queryState"] = state };
    }

    private static JsonObject MeasureProjection(string entity, string measure)
        => new()
        {
            ["field"] = new JsonObject
            {
                ["Measure"] = new JsonObject
                {
                    ["Expression"] = SourceRef(entity),
                    ["Property"] = measure
                }
            },
            ["queryRef"] = $"{entity}.{measure}",
            ["nativeQueryRef"] = measure
        };

    private static JsonObject ColumnProjection(string entity, string column, bool active = false)
    {
        var projection = new JsonObject
        {
            ["field"] = new JsonObject
            {
                ["Column"] = new JsonObject
                {
                    ["Expression"] = SourceRef(entity),
                    ["Property"] = column
                }
            },
            ["queryRef"] = $"{entity}.{column}",
            ["nativeQueryRef"] = column
        };
        if (active)
            projection["active"] = true;
        return projection;
    }

    private static JsonObject SourceRef(string entity)
        => new() { ["SourceRef"] = new JsonObject { ["Entity"] = entity } };

    private static JsonObject Literal(string value)
        => new() { ["expr"] = new JsonObject { ["Literal"] = new JsonObject { ["Value"] = value } } };

    private static string StableHex(string value, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant()[..length];
    }

    private static string Json(JsonNode node)
        => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
