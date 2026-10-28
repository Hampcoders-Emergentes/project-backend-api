using System.Text;
using System.Text.Json.Serialization;
using Hampcoders.Electrolink.API.Assets.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.IAM.Infrastructure.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Shared.Application.Internal.EventPublisher;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Configuration;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Persistence.EFC.Repositories;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Interfaces.ASP.Configuration;
using Hampcoders.Electrolink.API.Shared.Domain.Repositories;
using Hampcoders.Electrolink.API.Shared.Domain.Services;
using Hampcoders.Electrolink.API.Shared.Infrastructure.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using MediatR; 
using Hampcoders.Electrolink.API.IAM.Infrastructure.Pipeline.Middleware.Extensions;
using Hampcoders.Electrolink.API.Monitoring.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Planning.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Profiles.Infrastructure.Persistence.JSON;
using Hampcoders.Electrolink.API.Analytics.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Processing.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Analytics.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Processing.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Hampcoders.Electrolink.API.Subscriptions.Application.Internal.CommandServices;
using Hampcoders.Electrolink.API.Subscriptions.Infrastructure.Interfaces.ASP.Configuration.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Hampcoders.Electrolink.API.Shared.Infrastructure;
using Hampcoders.Electrolink.API.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Hampcoders.Electrolink.API.StoreManagement.Infrastructure.Configuration;

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
    options.UseNpgsql(connectionString, o =>
    {
        o.UseNetTopologySuite();
    });
    
    if (builder.Environment.IsDevelopment())
        options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .LogTo(Console.WriteLine, LogLevel.Information)
            .EnableSensitiveDataLogging()
            .EnableDetailedErrors();
    else
        options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .LogTo(Console.WriteLine, LogLevel.Error);
});

builder.AddStoreManagementContextService(connectionString);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Hampcoders.ElectrolinkPlatform.API",
        Version     = "v1",
        Description = "Hampcoders Electrolink Platform API",
        Contact     = new OpenApiContact { Name = "Hampcoders", Email = "contact@Hampcoders.com" },
        License     = new OpenApiLicense { Name = "Apache 2.0", Url = new Uri("https://www.apache.org/licenses/LICENSE-2.0") }
    });

    options.EnableAnnotations();

    // JWT Bearer
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In            = ParameterLocation.Header,
        Description   = "Ingrese el token JWT",
        Name          = "Authorization",
        Type          = SecuritySchemeType.Http,
        Scheme        = "bearer",
        BearerFormat  = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    if (builder.Environment.IsDevelopment())
    {
        options.AddServer(new OpenApiServer
        {
            Url         = "http://localhost:5055",
            Description = "Development Server"
        });
    }
});


// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllPolicy", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
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
        // Carga la clave secreta desde la configuración (asegúrate de que TokenSettings.Secret esté configurado)
        var secret = builder.Configuration["TokenSettings:Secret"] ?? throw new InvalidOperationException("TokenSettings:Secret not configured.");
        var key = Encoding.ASCII.GetBytes(secret);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, // Valida la firma del token
            IssuerSigningKey = new SymmetricSecurityKey(key), // Usa tu clave secreta
            ValidateIssuer = false, // Puedes establecer esto en true si tienes un emisor de tokens específico
            ValidateAudience = false, // Puedes establecer esto en true si tienes una audiencia de tokens específica
            ValidateLifetime = true, // Valida la fecha de expiración del token
            ClockSkew = TimeSpan.Zero // No permite desviación del reloj para la expiración
        };
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
    typeof(ConsumptionDashboardCommandService).Assembly,
    typeof(DeviceReadingStreamCommandService).Assembly,
    typeof(Hampcoders.Electrolink.API.Profiles.Application.Internal.CommandServices.ProfileCommandService).Assembly);

builder.Services.AddScoped<IAuthorizationHandler, SubscriptionTierHandler>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// DB Init
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();
}

// Middleware
app.UseForwardedHeaders();
app.UseCors("AllowAllPolicy");
app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAllPolicy");
//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRequestAuthorization();
app.UseAuthorization();
app.MapControllers();

// Uncomment the following line to enable OpenAPI documentation (Development Server)
// app.Urls.Add("http://*:8088");
//app.Urls.Add("http://*:8080");
builder.Logging.AddConsole();
app.Run();
