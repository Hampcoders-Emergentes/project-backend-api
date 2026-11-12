namespace Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

/// <summary>Summary/description/operation id of an endpoint, applied to the OpenAPI document by <see cref="SwaggerAnnotationsOperationTransformer"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApiDocOperationAttribute : Attribute
{
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public string? OperationId { get; init; }
}

/// <summary>Documents one response of an endpoint.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class ApiDocResponseAttribute(int statusCode, string? description = null, Type? type = null) : Attribute
{
    public int StatusCode { get; } = statusCode;
    public string? Description { get; } = description;
    public Type? Type { get; } = type;
}

/// <summary>Group description of a controller.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ApiDocTagAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
