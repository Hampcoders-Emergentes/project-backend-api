using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

namespace Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi;

/// <summary>Declares the JWT bearer scheme and requires it by default (Scalar shows an auth box).</summary>
public sealed class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Hampcoders.ElectrolinkPlatform.API",
            Version = "v1",
            Description = "Hampcoders Electrolink Platform API",
            Contact = new OpenApiContact { Name = "Hampcoders", Email = "contact@Hampcoders.com" },
            License = new OpenApiLicense { Name = "Apache 2.0", Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0") }
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the JWT access token"
        };
        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
        return Task.CompletedTask;
    }
}

/// <summary>
///     Keeps the existing [SwaggerOperation]/[SwaggerResponse] annotations meaningful: they are plain attributes,
///     so this maps them onto the generated OpenAPI operation instead of rewriting ~150 usages.
/// </summary>
public sealed class SwaggerAnnotationsOperationTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var info = metadata.OfType<ApiDocOperationAttribute>().FirstOrDefault();
        if (info is not null)
        {
            if (!string.IsNullOrWhiteSpace(info.Summary)) operation.Summary = info.Summary;
            if (!string.IsNullOrWhiteSpace(info.Description)) operation.Description = info.Description;
            if (!string.IsNullOrWhiteSpace(info.OperationId)) operation.OperationId = info.OperationId;
        }
        foreach (var response in metadata.OfType<ApiDocResponseAttribute>())
        {
            operation.Responses ??= new OpenApiResponses();
            var code = response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(code, out var existing) || existing is not OpenApiResponse typed)
                operation.Responses[code] = typed = new OpenApiResponse { Description = response.Description ?? string.Empty };
            else if (response.Description is not null) typed.Description = response.Description;
            if (response.Type is not null && typed.Content is null)
                typed.Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType { Schema = await context.GetOrCreateSchemaAsync(response.Type, null, cancellationToken) }
                };
        }
    }
}
