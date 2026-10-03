using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;

namespace TechBazar.Api.Extensions;

/// <summary>Makes <c>[controller]</c> route tokens lower-case/kebab-case: ProductsController -> /products.</summary>
public sealed partial class LowercaseRouteTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) =>
        value is null ? null : Kebab().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Kebab();
}
