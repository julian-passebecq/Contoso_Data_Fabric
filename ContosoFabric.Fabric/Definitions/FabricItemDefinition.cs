namespace ContosoFabric.Fabric.Definitions;

public sealed record FabricDefinitionPart(string Path, string Content);

public sealed record FabricItemDefinition(
    string Format,
    IReadOnlyList<FabricDefinitionPart> Parts)
{
    public FabricItemDefinition Validate()
    {
        if (string.IsNullOrWhiteSpace(Format))
            throw new InvalidOperationException("Fabric definition format is required.");
        if (Parts.Count == 0)
            throw new InvalidOperationException("Fabric definition must contain at least one part.");

        var duplicate = Parts
            .GroupBy(part => part.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate Fabric definition part: {duplicate.Key}");

        if (Parts.Any(part => string.IsNullOrWhiteSpace(part.Path) || string.IsNullOrWhiteSpace(part.Content)))
            throw new InvalidOperationException("Fabric definition parts require a path and content.");

        return this;
    }
}
