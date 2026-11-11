using Hampcoders.Electrolink.API.Assets.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using MediatR; 
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Extensions;
using Hampcoders.Electrolink.API.Monitoring.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Persistence.JSON;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Npgsql;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Email;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Auditing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting(options => options.LowercaseUrls = true);
builder.Services.AddControllers(options => options.Conventions.Add(new KebabCaseRouteNamingConvention()))
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new ESpecialtyJsonConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

NpgsqlConnection.GlobalTypeMapper.EnableDynamicJson();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (connectionString == null) throw new InvalidOperationException("Connection string not found");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, o => o.UseNetTopologySuite());

    if (builder.Environment.IsDevelopment())
        options.LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging()
            .EnableDetailedErrors();
    else
        options.LogTo(Console.WriteLine, LogLevel.Error);
});

builder.AddStoreManagementContextService(connectionString);

// OpenAPI document (built in) rendered by Scalar at /scalar/v1; spec at /openapi/v1.json
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer<Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi.BearerSecurityDocumentTransformer>();
    options.AddOperationTransformer<Hampcoders.Electrolink.API.Shared.Infrastructure.OpenApi.SwaggerAnnotationsOperationTransformer>();
});

// CORS: explicit frontend origins from configuration (Cors:AllowedOrigins), default Angular dev server.
const string FrontendCorsPolicy = "FrontendPolicy";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
    .Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().TrimEnd('/')).ToArray() ?? [];
if (allowedOrigins.Length == 0) allowedOrigins = ["http://localhost:4200"];
if (allowedOrigins.Contains("*"))
    throw new InvalidOperationException("Cors:AllowedOrigins must list explicit origins; '*' is not allowed with credentials.");
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});

// Shared
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Domain services for Monitoring

// Subscriptions and Payments Bounded Context


// Assets ACL

builder.AddIamContextServices();
builder.AddProfilesContextServices();
builder.AddAssetsContextService();
builder.AddPlanningContextService();
builder.AddMonitoringServices();
builder.AddSubscriptionServices();
builder.AddAnalyticsContextServices();
builder.AddProcessingContextServices();
builder.Services.AddExternalProviders(builder.Configuration);
builder.Services.AddEmailDelivery(builder.Configuration);

builder.Services.AddScoped<IOrphanedFileRepository, OrphanedFileRepository>();
builder.Services.AddHostedService<OutboxProcessorBackgroundService>();
builder.Services.AddHostedService<CleanupOrphanedFilesJob>();


// Add Cortex Mediator for Event Handling

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var tokenSettings = builder.Configuration.GetSection("TokenSettings").Get<Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration.TokenSettings>()
                            ?? new Hampcoders.Electrolink.API.IAM.Infrastructure.Tokens.JWT.Configuration.TokenSettings();
        if (string.IsNullOrWhiteSpace(tokenSettings.Secret))
            throw new InvalidOperationException("TokenSettings:Secret not configured.");

        // Same parameters as the IAM middleware: issuer, audience, lifetime and signature, zero clock skew.
        options.TokenValidationParameters = tokenSettings.CreateValidationParameters();
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PremiumOnly", policy =>
        policy.Requirements.Add(new SubscriptionTierRequirement("PREMIUM", "ENTERPRISE")));
    options.AddPolicy("EnterpriseOnly", policy =>
        policy.Requirements.Add(new SubscriptionTierRequirement("ENTERPRISE")));
});

builder.Services.AddMediatR(
    typeof(SubscriptionCommandService).Assembly,
    typeof(TechnicianMetricsCommandService).Assembly,
    typeof(DeviceReadingStreamCommandService).Assembly,
    typeof(Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices.ProfileCommandService).Assembly);

builder.Services.AddScoped<IAuthorizationHandler, SubscriptionTierHandler>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Logging.AddConsole();

var app = builder.Build();

// The edge allowlists fail closed, so an empty one would otherwise surface only as 403s at request time.
foreach (var allowlistKey in new[] { "EdgeApi:EquipmentReadings:AllowedDeviceIds", "EdgeApi:RelayHistory:AllowedDeviceIds" })
{
    if (!app.Configuration.GetSection(allowlistKey).GetChildren().Any(entry => !string.IsNullOrWhiteSpace(entry.Value)))
        app.Logger.LogWarning("{AllowlistKey} has no device IDs: edge requests it guards will get 403. Use indexed entries ({AllowlistKey}:0).", allowlistKey, allowlistKey);
}

// DB schema. AppDbContext is created from its current model (EnsureCreated): it only acts on an empty database
// and never updates an existing one. It must run first: once any table exists it skips everything.
// StoreManagementDbContext then applies its versioned EF Core migrations.
// Set Database:MigrateOnStartup=false to manage the schema as a separate deployment step instead.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    scope.ServiceProvider.GetRequiredService<Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Persistence.StoreManagementDbContext>().Database.Migrate();
}

// Middleware
app.UseForwardedHeaders();
app.UseCors(FrontendCorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

//app.UseHttpsRedirection();
app.UseAuthentication();
// Before request authorization, so the 401/403 responses it produces are audited too.
app.UseAuditLogging();
app.UseRequestAuthorization();
app.UseAuthorization();
// Endpoint rate limits (public demo request form).
app.UseRateLimiter();
app.MapControllers();
// RequestAuthorizationMiddleware only honours the IAM attribute, so the docs endpoints are marked with it.
var anonymousDocs = new Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AllowAnonymousAttribute();
app.MapOpenApi().WithMetadata(anonymousDocs);
app.MapScalarApiReference(options => options
    .WithTitle("Electrolink API")
    .AddPreferredSecuritySchemes("Bearer")).WithMetadata(anonymousDocs);
// RequestAuthorizationMiddleware only honours the IAM attribute; the framework's .AllowAnonymous() still gets a 401.
app.MapHealthChecks("/health")
    .WithMetadata(new Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Attributes.AllowAnonymousAttribute());

// Uncomment the following line to enable OpenAPI documentation (Development Server)
// app.Urls.Add("http://*:8088");
//app.Urls.Add("http://*:8080");
app.Run();
