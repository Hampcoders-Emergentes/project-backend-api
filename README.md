---
title: "Project Structure for: ASP.NET Core Monolith with DDD & Bounded Contexts" 
date: 2024-12-04 
tags:
- project-structure
- architecture
- ddd
- bounded-contexts
- aspnet-core 
stack:
  - C#
  - .NET 8/9
  - ASP.NET Core
  - Entity Framework Core
  - PostgreSQL
  - MySQL
  - SQL Server
  - MediatR
  - FluentValidation
  - Swagger/OpenAPI
  - Serilog 
principles:
- "[[Domain-Driven Design]]"
- "[[Layered Architecture]]"
- "[[Bounded Context Pattern]]"
- "[[CQRS Pattern]]"
- "[[Repository Pattern]]"

---

## Configuración local y variables de entorno

Los valores sensibles no deben guardarse en `appsettings.json`. La aplicación acepta variables de entorno con `__` para representar secciones anidadas de configuración de .NET. Por ejemplo:

```bash
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5433;Username=postgres;Password=tu_password;Database=electrolink2'
export TokenSettings__Secret='una-clave-local-de-al-menos-32-caracteres'
export Stripe__SecretKey='sk_test_...'
export Stripe__WebhookSecret='whsec_...'
export Cloudinary__ApiKey='...'
export Cloudinary__ApiSecret='...'
export Gemini__ApiKey='...'
export EdgeApi__ApiKey='...'
dotnet run --project Hampcoders.Electrolink.API.csproj
```

También puedes copiar [.env.example](./.env.example) como `.env`, completar sus valores y cargarlo antes de iniciar:

```bash
cp .env.example .env
set -a; source .env; set +a
dotnet run --project Hampcoders.Electrolink.API.csproj
```

`.env` está excluido por `.gitignore`; nunca lo subas a GitHub. En producción, configura estas mismas variables en el proveedor de hosting o en el sistema de secretos correspondiente.

## 1. Philosophy & Guiding Principles

This structure is based on **[[Domain-Driven Design]]** principles with **[[Bounded Context Pattern]]** implementation. The architecture follows a **[[Layered Architecture]]** approach enhanced with **[[CQRS Pattern]]** for command-query separation. The primary goal is to organize complex business domains into well-defined, loosely coupled contexts that can evolve independently while maintaining clear boundaries and communication protocols.

### Core Principles

- **Bounded Contexts:** Each business domain is encapsulated in its own bounded context with clear boundaries and responsibilities. Contexts communicate through Anti-Corruption Layers (ACL) to maintain independence.

- **Domain-Centric Design:** Business logic is concentrated in the Domain layer, independent of infrastructure concerns. Aggregates, Entities, and Value Objects enforce business invariants.

- **Dependency Rule:** Dependencies flow inward. Domain layer has zero external dependencies. Application layer depends on Domain. Infrastructure implements Domain interfaces.

- **CQRS Segregation:** Commands (write operations) and Queries (read operations) are separated into distinct services with different optimization strategies.

- **Shared Kernel:** Common domain building blocks (base classes, interfaces, shared utilities) live in a Shared context to promote DRY principles across bounded contexts.


---

## 2. Folder Structure Tree

```text
📁 {ProjectName}.API/
├── 📄 Program.cs                          # Application entry point & DI configuration
├── 📄 appsettings.json                    # Configuration settings
├── 📄 appsettings.Development.json        # Development-specific settings
├── 📄 GlobalUsings.cs                     # Global using directives
├── 📄 {ProjectName}.API.csproj            # Project file
│
├── 📁 {BoundedContext1}/                  # Example: Catalog Context
│   ├── 📁 Domain/                         # Pure business logic
│   │   ├── 📁 Model/
│   │   │   ├── 📁 Aggregates/            # Aggregate roots
│   │   │   │   ├── Product.cs
│   │   │   │   ├── ProductAudit.cs       # Audit properties
│   │   │   │   └── ProductContent.cs     # Content/details
│   │   │   ├── 📁 Entities/              # Domain entities
│   │   │   │   ├── Category.cs
│   │   │   │   └── CategoryAudit.cs
│   │   │   ├── 📁 ValueObjects/          # Immutable value objects
│   │   │   │   ├── Money.cs
│   │   │   │   ├── ProductCode.cs
│   │   │   │   └── IValueObject.cs
│   │   │   ├── 📁 Commands/              # Write model operations
│   │   │   │   ├── CreateProductCommand.cs
│   │   │   │   ├── UpdateProductCommand.cs
│   │   │   │   └── DeleteProductCommand.cs
│   │   │   ├── 📁 Queries/               # Read model operations
│   │   │   │   ├── GetProductByIdQuery.cs
│   │   │   │   └── GetProductsByCategoryQuery.cs
│   │   │   └── 📁 Events/                # Domain events
│   │   │       ├── ProductCreatedEvent.cs
│   │   │       └── ProductPriceChangedEvent.cs
│   │   ├── 📁 Repositories/              # Repository interfaces (contracts)
│   │   │   ├── IProductRepository.cs
│   │   │   └── ICategoryRepository.cs
│   │   └── 📁 Services/                  # Domain service interfaces
│   │       ├── IProductCommandService.cs
│   │       └── IProductQueryService.cs
│   │
│   ├── 📁 Application/                    # Application orchestration
│   │   ├── 📁 Internal/
│   │   │   ├── 📁 CommandServices/       # Command handlers implementation
│   │   │   │   └── ProductCommandService.cs
│   │   │   ├── 📁 QueryServices/         # Query handlers implementation
│   │   │   │   └── ProductQueryService.cs
│   │   │   ├── 📁 EventHandlers/         # Domain event handlers
│   │   │   │   └── ProductCreatedEventHandler.cs
│   │   │   └── 📁 OutboundServices/      # External service abstractions
│   │   │       └── ExternalInventoryService.cs
│   │   └── 📁 ACL/                       # Anti-Corruption Layer
│   │       └── CatalogContextFacade.cs   # Public interface for other contexts
│   │
│   ├── 📁 Infrastructure/                 # Technical implementation details
│   │   └── 📁 Persistence/
│   │       └── 📁 EFC/
│   │           ├── 📁 Configurations/    # EF Core entity configurations
│   │           │   ├── ProductConfiguration.cs
│   │           │   ├── CategoryConfiguration.cs
│   │           │   └── 📁 Extensions/
│   │           │       └── ModelBuilderExtensions.cs
│   │           └── 📁 Repositories/      # Repository implementations
│   │               ├── ProductRepository.cs
│   │               └── CategoryRepository.cs
│   │
│   └── 📁 Interfaces/                     # Presentation layer
│       ├── 📁 ACL/                       # ACL interface definitions
│       │   └── ICatalogContextFacade.cs
│       └── 📁 REST/
│           ├── 📁 Resources/             # DTOs for API contracts
│           │   ├── ProductResource.cs
│           │   ├── CreateProductResource.cs
│           │   ├── UpdateProductResource.cs
│           │   └── CategoryResource.cs
│           ├── 📁 Transform/             # Assemblers (mappers)
│           │   ├── CreateProductCommandFromResourceAssembler.cs
│           │   ├── ProductResourceFromEntityAssembler.cs
│           │   └── CategoryResourceFromEntityAssembler.cs
│           └── 📄 ProductsController.cs  # REST API endpoints
│
├── 📁 {BoundedContext2}/                  # Example: Orders Context
│   ├── 📁 Domain/
│   ├── 📁 Application/
│   │   └── 📁 ACL/
│   │       └── CatalogContextFacade.cs   # Consumes Catalog context
│   ├── 📁 Infrastructure/
│   └── 📁 Interfaces/
│
└── 📁 Shared/                             # Shared Kernel
    ├── 📁 Domain/
    │   ├── 📁 Model/
    │   │   ├── 📁 Aggregates/
    │   │   │   ├── BaseAggregateRoot.cs
    │   │   │   └── IAggregateRoot.cs
    │   │   ├── 📁 Entities/
    │   │   │   ├── BaseEntity.cs
    │   │   │   └── IEntity.cs
    │   │   ├── 📁 ValueObjects/
    │   │   │   ├── BaseValueObject.cs
    │   │   │   └── IValueObject.cs
    │   │   ├── 📁 Commands/
    │   │   │   └── ICommand.cs
    │   │   ├── 📁 Queries/
    │   │   │   └── IQuery.cs
    │   │   └── 📁 Events/
    │   │       └── IEvent.cs
    │   └── 📁 Repositories/
    │       ├── IBaseRepository.cs
    │       └── IUnitOfWork.cs
    │
    ├── 📁 Application/
    │   └── 📁 Internal/
    │       ├── 📁 EventHandlers/
    │       │   └── IEventHandler.cs
    │       └── 📁 OutboundServices/
    │           └── IUnitOfWork.cs
    │
    └── 📁 Infrastructure/
        ├── 📁 Interfaces/
        │   └── 📁 ASP/
        │       └── 📁 Configuration/
        │           └── 📁 Extensions/
        │               └── KebabCaseRouteNamingConvention.cs
        ├── 📁 Mediator/
        │   └── 📁 Cortex/
        │       └── 📁 Configuration/
        │           └── LoggingCommandBehavior.cs
        └── 📁 Persistence/
            └── 📁 EFC/
                ├── 📁 Configuration/
                │   └── 📁 Extensions/
                │       ├── AppDbContext.cs
                │       ├── ModelBuilderExtensions.cs
                │       └── StringExtensions.cs
                └── 📁 Repositories/
                    ├── BaseRepository.cs
                    └── UnitOfWork.cs
```

---

## 3. Core Directory Breakdown

### Bounded Context Structure

Each bounded context is a self-contained module representing a specific business domain. Contexts are independent and communicate only through well-defined interfaces (ACL).

#### **Domain Layer** (`/{BoundedContext}/Domain/`)

The innermost layer containing pure business logic with zero external dependencies.

- **`Model/Aggregates/`**: Aggregate roots that enforce consistency boundaries. Each aggregate is the entry point for all operations on related entities.

    - `{Aggregate}.cs`: Main aggregate root class
    - `{Aggregate}Audit.cs`: Audit fields (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
    - `{Aggregate}Content.cs`: Rich content or complex nested data
- **`Model/Entities/`**: Domain entities that exist within aggregate boundaries but have their own identity.

- **`Model/ValueObjects/`**: Immutable objects defined by their attributes, not identity (e.g., Money, Address, Email). Implement equality by value.

- **`Model/Commands/`**: Represent write intentions (CreateX, UpdateX, DeleteX). Used in CQRS pattern for state changes.

- **`Model/Queries/`**: Represent read intentions (GetXById, GetXByY). Optimized for data retrieval without business logic.

- **`Model/Events/`**: Domain events that capture significant business occurrences (XCreated, XUpdated). Used for event-driven architecture.

- **`Repositories/`**: Interfaces defining data access contracts. The domain defines _what_ data operations are needed, not _how_.

- **`Services/`**: Interfaces for domain services that contain business logic not naturally fitting in entities/aggregates.


#### **Application Layer** (`/{BoundedContext}/Application/`)

Orchestrates domain operations, implements use cases, and coordinates cross-cutting concerns.

- **`Internal/CommandServices/`**: Implements command handlers. Validates commands, calls domain logic, persists changes through repositories.

- **`Internal/QueryServices/`**: Implements query handlers. Optimized for read operations, may bypass domain models for performance.

- **`Internal/EventHandlers/`**: React to domain events. Implement side effects, trigger workflows, or communicate with other contexts.

- **`Internal/OutboundServices/`**: Abstractions for external dependencies (third-party APIs, messaging systems).

- **`ACL/` (Anti-Corruption Layer)**: Provides a facade for other bounded contexts to interact with this context. Translates between domain models and external representations. See `[[Anti-Corruption Layer Pattern]]`.


#### **Infrastructure Layer** (`/{BoundedContext}/Infrastructure/`)

Contains all technical implementation details and framework-specific code.

- **`Persistence/EFC/Configurations/`**: Entity Framework Core configurations using Fluent API. Each entity has its own configuration class implementing `IEntityTypeConfiguration<T>`.

- **`Persistence/EFC/Repositories/`**: Concrete implementations of repository interfaces defined in Domain layer. Uses EF Core DbContext for data access.


#### **Interfaces Layer** (`/{BoundedContext}/Interfaces/`)

The outermost layer handling external communication and data transformation.

- **`REST/Resources/`**: DTOs (Data Transfer Objects) representing API contracts. Decoupled from domain models to prevent leaking domain structure.

- **`REST/Transform/`**: Assemblers (mappers) that convert between Resources and Domain models. Implements the Assembler pattern for clean object-to-object mapping.

- **`REST/{Entity}Controller.cs`**: ASP.NET Core controllers exposing RESTful endpoints. Handle HTTP concerns, validate input, delegate to services.

- **`ACL/`**: Interface definitions for Anti-Corruption Layer, consumed by other contexts.


### Shared Kernel (`/Shared/`)

Contains common building blocks reused across all bounded contexts.

- **`Domain/Model/`**: Base classes and interfaces for Aggregates, Entities, Value Objects, Commands, Queries, Events.

- **`Domain/Repositories/`**: Generic repository interfaces (`IBaseRepository<T>`, `IUnitOfWork`).

- **`Infrastructure/Persistence/EFC/`**:

    - `AppDbContext`: Main EF Core DbContext
    - `BaseRepository<T>`: Generic repository implementation
    - `UnitOfWork`: Transaction management implementation
- **`Infrastructure/Mediator/Cortex/`**: MediatR pipeline behaviors (logging, validation, transaction management).

- **`Infrastructure/Interfaces/ASP/`**: ASP.NET Core conventions (KebabCase routing, exception handling middleware).


---

## 4. Data Flow & Architecture Patterns

### Request Flow (Command - Write Operation)

```
HTTP POST Request
        ↓
ProductsController.CreateProduct()
        ↓
CreateProductCommandFromResourceAssembler
        ↓
CreateProductCommand (Domain Model)
        ↓
MediatR Pipeline
        ↓
ProductCommandService.Handle()
        ↓
Product Aggregate (Domain Logic)
        ↓
IProductRepository.AddAsync()
        ↓
ProductRepository (EF Core)
        ↓
UnitOfWork.SaveChangesAsync()
        ↓
Database Transaction
        ↓
Domain Events Dispatched
        ↓
Event Handlers (if any)
        ↓
HTTP 201 Created + ProductResource
```

### Request Flow (Query - Read Operation)

```
HTTP GET Request
        ↓
ProductsController.GetProductById()
        ↓
GetProductByIdQuery
        ↓
MediatR Pipeline
        ↓
ProductQueryService.Handle()
        ↓
IProductRepository.FindByIdAsync()
        ↓
ProductRepository (EF Core - No Tracking)
        ↓
Database Read (Optimized Query)
        ↓
ProductResourceFromEntityAssembler
        ↓
HTTP 200 OK + ProductResource
```

### Inter-Context Communication Flow

```
Orders Context (needs product info)
        ↓
ICatalogContextFacade (ACL Interface)
        ↓
CatalogContextFacade.GetProductInfo()
        ↓
Internal Catalog Query Service
        ↓
Product Repository
        ↓
Transform to ACL DTO
        ↓
Return to Orders Context
```

### Key Patterns Applied

1. **[[Domain-Driven Design]]**: Business logic encapsulated in Aggregates, Entities, and Value Objects. Ubiquitous language reflected in code.

2. **[[Repository Pattern]]**: Abstracts data access behind interfaces. Domain defines contracts, Infrastructure provides implementations.

3. **[[CQRS Pattern]]**: Separates read (Query) and write (Command) models for optimization and scalability. Commands change state, Queries read state.

4. **[[Unit of Work Pattern]]**: Manages transactions and coordinates multiple repository operations. Ensures consistency across aggregate boundaries.

5. **[[Mediator Pattern]]**: MediatR decouples request senders from handlers. Enables cross-cutting concerns through pipeline behaviors.

6. **[[Anti-Corruption Layer]]**: Protects bounded context integrity by translating external models. Prevents external changes from corrupting domain.

7. **[[Assembler Pattern]]**: Converts between domain models and external representations (Resources, DTOs) without polluting domain with presentation concerns.

8. **[[Specification Pattern]]**: (Optional) Encapsulates query logic in reusable, composable specifications for complex filtering.


---

## 5. Code Examples

### Domain Layer Example

**Aggregate Root with Business Logic**

```csharp
// Domain/Model/Aggregates/Product.cs
namespace CatalogContext.Domain.Model.Aggregates;

public class Product : BaseAggregateRoot
{
    // Value Objects
    public ProductCode Code { get; private set; }
    public Money Price { get; private set; }
    
    // Primitive Properties
    public string Name { get; private set; }
    public string Description { get; private set; }
    public bool IsActive { get; private set; }
    
    // Navigation to Entity within Aggregate
    public int CategoryId { get; private set; }
    public Category Category { get; private set; }
    
    // Domain Events Collection
    private readonly List<IEvent> _domainEvents = new();
    public IReadOnlyCollection<IEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    // Private constructor for EF Core
    private Product() { }
    
    // Factory method enforcing business rules
    public static Product Create(ProductCode code, string name, Money price, int categoryId)
    {
        // Business rule validation
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be empty", nameof(name));
            
        if (price.Amount <= 0)
            throw new ArgumentException("Product price must be positive", nameof(price));
        
        var product = new Product
        {
            Code = code,
            Name = name,
            Price = price,
            CategoryId = categoryId,
            IsActive = true
        };
        
        // Raise domain event
        product._domainEvents.Add(new ProductCreatedEvent(product.Id, product.Code));
        
        return product;
    }
    
    // Business method with invariant protection
    public void ChangePrice(Money newPrice)
    {
        if (newPrice.Amount <= 0)
            throw new ArgumentException("Price must be positive", nameof(newPrice));
        
        if (newPrice.Amount != Price.Amount)
        {
            var oldPrice = Price;
            Price = newPrice;
            _domainEvents.Add(new ProductPriceChangedEvent(Id, oldPrice, newPrice));
        }
    }
    
    public void Deactivate()
    {
        IsActive = false;
    }
    
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

**Value Object Implementation**

```csharp
// Domain/Model/ValueObjects/Money.cs
namespace CatalogContext.Domain.Model.ValueObjects;

public record Money : IValueObject
{
    public decimal Amount { get; init; }
    public string Currency { get; init; }
    
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
            
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }
    
    // Business operations
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException("Cannot add money with different currencies");
            
        return new Money(Amount + other.Amount, Currency);
    }
    
    public Money Multiply(decimal factor) => new Money(Amount * factor, Currency);
    
    // Record provides structural equality automatically
}
```

**Command & Query**

```csharp
// Domain/Model/Commands/CreateProductCommand.cs
namespace CatalogContext.Domain.Model.Commands;

public record CreateProductCommand(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    int CategoryId
) : ICommand;

// Domain/Model/Queries/GetProductByIdQuery.cs
namespace CatalogContext.Domain.Model.Queries;

public record GetProductByIdQuery(int ProductId) : IQuery<Product>;
```

**Repository Interface**

```csharp
// Domain/Repositories/IProductRepository.cs
namespace CatalogContext.Domain.Repositories;

public interface IProductRepository : IBaseRepository<Product>
{
    Task<Product?> FindByCodeAsync(ProductCode code);
    Task<IEnumerable<Product>> FindByCategoryAsync(int categoryId);
    Task<bool> ExistsWithCodeAsync(ProductCode code);
}
```

---

### Application Layer Example

**Command Service with Transaction Management**

```csharp
// Application/Internal/CommandServices/ProductCommandService.cs
namespace CatalogContext.Application.Internal.CommandServices;

public class ProductCommandService : IProductCommandService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProductCommandService> _logger;
    
    public ProductCommandService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork,
        ILogger<ProductCommandService> logger)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }
    
    public async Task<Product> Handle(CreateProductCommand command)
    {
        _logger.LogInformation("Creating product with code {Code}", command.Code);
        
        // Validate business rules
        var productCode = new ProductCode(command.Code);
        
        if (await _productRepository.ExistsWithCodeAsync(productCode))
            throw new InvalidOperationException($"Product with code {command.Code} already exists");
        
        var category = await _categoryRepository.FindByIdAsync(command.CategoryId);
        if (category == null)
            throw new ArgumentException($"Category {command.CategoryId} not found");
        
        // Create aggregate using factory method
        var price = new Money(command.Price, command.Currency);
        var product = Product.Create(productCode, command.Name, price, command.CategoryId);
        
        // Persist
        await _productRepository.AddAsync(product);
        await _unitOfWork.CompleteAsync();
        
        _logger.LogInformation("Product {ProductId} created successfully", product.Id);
        
        return product;
    }
}
```

**Query Service with Optimized Read**

```csharp
// Application/Internal/QueryServices/ProductQueryService.cs
namespace CatalogContext.Application.Internal.QueryServices;

public class ProductQueryService : IProductQueryService
{
    private readonly IProductRepository _productRepository;
    
    public ProductQueryService(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }
    
    public async Task<Product?> Handle(GetProductByIdQuery query)
    {
        // Query optimization: AsNoTracking for read-only operations
        return await _productRepository.FindByIdAsync(query.ProductId);
    }
    
    public async Task<IEnumerable<Product>> Handle(GetProductsByCategoryQuery query)
    {
        return await _productRepository.FindByCategoryAsync(query.CategoryId);
    }
}
```

**Anti-Corruption Layer Facade**

```csharp
// Application/ACL/CatalogContextFacade.cs
namespace CatalogContext.Application.ACL;

public class CatalogContextFacade : ICatalogContextFacade
{
    private readonly IProductQueryService _productQueryService;
    
    public CatalogContextFacade(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }
    
    // Exposed to other contexts - returns ACL-specific DTOs
    public async Task<ProductInfoDto?> GetProductInfoAsync(int productId)
    {
        var query = new GetProductByIdQuery(productId);
        var product = await _productQueryService.Handle(query);
        
        if (product == null) return null;
        
        // Transform to ACL DTO (different from REST Resources)
        return new ProductInfoDto
        {
            Id = product.Id,
            Code = product.Code.Value,
            Name = product.Name,
            Price = product.Price.Amount,
            Currency = product.Price.Currency,
            IsAvailable = product.IsActive
        };
    }
}
```

---

### Infrastructure Layer Example

**EF Core Entity Configuration**

```csharp
// Infrastructure/Persistence/EFC/Configurations/ProductConfiguration.cs
namespace CatalogContext.Infrastructure.Persistence.EFC.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();
        
        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        
        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(1000);
        
        // Value Object mapping - Owned Entity
        builder.OwnsOne(p => p.Code, code =>
        {
            code.Property(c => c.Value)
                .HasColumnName("code")
                .HasMaxLength(50)
                .IsRequired();
                
            code.HasIndex(c => c.Value).IsUnique();
        });
        
        // Value Object mapping - Owned Entity
        builder.OwnsOne(p => p.Price, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(18, 2)
                .IsRequired();
                
            price.Property(m => m.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(3)
                .IsRequired();
        });
        
        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);
        
        // Audit fields (from BaseAggregateRoot)
        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
            
        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at");
        
        // Relationships
        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .HasConstraintName("fk_products_categories")
            .OnDelete(DeleteBehavior.Restrict);
        
        // Ignore domain events (not persisted)
        builder.Ignore(p => p.DomainEvents);
    }
}
```

**Repository Implementation**

```csharp
// Infrastructure/Persistence/EFC/Repositories/ProductRepository.cs
namespace CatalogContext.Infrastructure.Persistence.EFC.Repositories;

public class ProductRepository : BaseRepository<Product>, IProductRepository
{
    public ProductRepository(AppDbContext context) : base(context) { }
    
    public async Task<Product?> FindByCodeAsync(ProductCode code)
    {
        return await Context.Set<Product>()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Code == code);
    }
    
    public async Task<IEnumerable<Product>> FindByCategoryAsync(int categoryId)
    {
        return await Context.Set<Product>()
            .Where(p => p.CategoryId == categoryId && p.IsActive)
            .Include(p => p.Category)
            .AsNoTracking()
            .ToListAsync();
    }
    
    public async Task<bool> ExistsWithCodeAsync(ProductCode code)
    {
        return await Context.Set<Product>()
            .AnyAsync(p => p.Code == code);
    }
}
```

---

### Interfaces Layer Example

**REST Controller**

```csharp
// Interfaces/REST/ProductsController.cs
namespace CatalogContext.Interfaces.REST;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IProductCommandService _commandService;
    private readonly IProductQueryService _queryService;
    
    public ProductsController(
        IProductCommandService commandService,
        IProductQueryService queryService)
    {
        _commandService = commandService;
        _queryService = queryService;
    }
    
    /// <summary>
    /// Creates a new product
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProductResource), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResource>> CreateProduct(
        [FromBody] CreateProductResource resource)
    {
        try
        {
            // Transform Resource to Command
            var command = CreateProductCommandFromResourceAssembler.ToCommand(resource);
            
            // Execute command
            var product = await _commandService.Handle(command);
            
            // Transform Entity to Resource
            var productResource = ProductResourceFromEntityAssembler.ToResource(product);
            
            return CreatedAtAction(
                nameof(GetProductById),
                new { id = product.Id },
                productResource);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    
    /// <summary>
    /// Gets a product by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResource>> GetProductById(int id)
    {
        var query = new GetProductByIdQuery(id);
        var product = await _queryService.Handle(query);
        
        if (product == null)
            return NotFound(new { message = $"Product with ID {id} not found" });
        
        var resource = ProductResourceFromEntityAssembler.ToResource(product);
        return Ok(resource);
    }
    
    /// <summary>
    /// Gets products by category
    /// </summary>
    [HttpGet("by-category/{categoryId:int}")]
    [ProducesResponseType(typeof(IEnumerable<ProductResource>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductResource>>> GetProductsByCategory(
        int categoryId)
    {
        var query = new GetProductsByCategoryQuery(categoryId);
        var products = await _queryService.Handle(query);
        
        var resources = products.Select(ProductResourceFromEntityAssembler.ToResource);
        return Ok(resources);
    }
}
```

**Resource (DTO)**

```csharp
// Interfaces/REST/Resources/ProductResource.cs
namespace CatalogContext.Interfaces.REST.Resources;

public record ProductResource(
    int Id,
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    bool IsActive,
    int CategoryId,
    string CategoryName,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

// Interfaces/REST/Resources/CreateProductResource.cs
public record CreateProductResource(
    string Code,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    int CategoryId
);
```

**Assembler (Mapper)**

```csharp
// Interfaces/REST/Transform/CreateProductCommandFromResourceAssembler.cs
namespace CatalogContext.Interfaces.REST.Transform;

public static class CreateProductCommandFromResourceAssembler
{
    public static CreateProductCommand ToCommand(CreateProductResource resource)
    {
        return new CreateProductCommand(
            resource.Code,
            resource.Name,
            resource.Description,
            resource.Price,
            resource.Currency,
            resource.CategoryId
        );
    }
}

// Interfaces/REST/Transform/ProductResourceFromEntityAssembler.cs
public static class ProductResourceFromEntityAssembler
{
    public static ProductResource ToResource(Product entity)
    {
        return new ProductResource(
            entity.Id,
            entity.Code.Value,
            entity.Name,
            entity.Description,
            entity.Price.Amount,
            entity.Price.Currency,
            entity.IsActive,
            entity.CategoryId,
            entity.Category?.Name ?? string.Empty,
            entity.CreatedAt,
            entity.UpdatedAt
        );
    }
}
```

---

### Shared Kernel Example

**Base Repository Interface**

```csharp
// Shared/Domain/Repositories/IBaseRepository.cs
namespace Shared.Domain.Repositories;

public interface IBaseRepository<TEntity> where TEntity : class
{
    Task<TEntity?> FindByIdAsync(int id);
    Task<IEnumerable<TEntity>> ListAsync();
    Task AddAsync(TEntity entity);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    Task<bool> ExistsAsync(int id);
}
```

**Base Repository Implementation**

```csharp
// Shared/Infrastructure/Persistence/EFC/Repositories/BaseRepository.cs
namespace Shared.Infrastructure.Persistence.EFC.Repositories;

public abstract class BaseRepository<TEntity> : IBaseRepository<TEntity> 
    where TEntity : class
{
    protected readonly AppDbContext Context;

    protected BaseRepository(AppDbContext context)
    {
        Context = context;
    }

    public virtual async Task<TEntity?> FindByIdAsync(int id)
    {
        return await Context.Set<TEntity>().FindAsync(id);
    }

    public virtual async Task<IEnumerable<TEntity>> ListAsync()
    {
        return await Context.Set<TEntity>().ToListAsync();
    }

    public virtual async Task AddAsync(TEntity entity)
    {
        await Context.Set<TEntity>().AddAsync(entity);
    }

    public virtual void Update(TEntity entity)
    {
        Context.Set<TEntity>().Update(entity);
    }

    public virtual void Remove(TEntity entity)
    {
        Context.Set<TEntity>().Remove(entity);
    }

    public virtual async Task<bool> ExistsAsync(int id)
    {
        return await Context.Set<TEntity>().FindAsync(id) != null;
    }
}
```

**Unit of Work Implementation**

```csharp
// Shared/Infrastructure/Persistence/EFC/Repositories/UnitOfWork.cs
namespace Shared.Infrastructure.Persistence.EFC.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> CompleteAsync()
    {
        // Dispatch domain events before saving
        await DispatchDomainEventsAsync();
        
        // Save all changes in a single transaction
        return await _context.SaveChangesAsync();
    }

    private async Task DispatchDomainEventsAsync()
    {
        var aggregates = _context.ChangeTracker
            .Entries<BaseAggregateRoot>()
            .Where(x => x.Entity.DomainEvents.Any())
            .Select(x => x.Entity)
            .ToList();

        var domainEvents = aggregates
            .SelectMany(x => x.DomainEvents)
            .ToList();

        aggregates.ForEach(a => a.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            // Publish event using MediatR or custom event dispatcher
            // await _mediator.Publish(domainEvent);
        }
    }
}
```

**AppDbContext**

```csharp
// Shared/Infrastructure/Persistence/EFC/Configuration/Extensions/AppDbContext.cs
namespace Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // DbSets for each bounded context
    // Catalog Context
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    
    // Orders Context
    // public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        
        // Apply naming conventions (snake_case)
        modelBuilder.UseSnakeCaseNamingConvention();
        
        // Apply pluralization for table names
        modelBuilder.UsePluralizingTableNameConvention();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Set audit fields automatically
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is BaseAggregateRoot && 
                       (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entry in entries)
        {
            var entity = (BaseAggregateRoot)entry.Entity;
            
            if (entry.State == EntityState.Added)
            {
                entity.CreatedAt = DateTime.UtcNow;
            }
            
            if (entry.State == EntityState.Modified)
            {
                entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
```

---

## 6. Key Trade-offs

### Pros

- ✅ **Strong Business Logic Encapsulation:** Domain models enforce business rules and invariants. Business complexity is centralized and testable.

- ✅ **Scalability Through Bounded Contexts:** Each context can evolve independently. Teams can work on different contexts without coordination overhead.

- ✅ **Maintainability:** Clear separation of concerns across layers. Changes to infrastructure don't affect domain logic. Easy to locate and modify code.

- ✅ **Testability:** Domain layer has zero dependencies and can be unit tested in isolation. Repository pattern enables easy mocking for tests.

- ✅ **Technology Flexibility:** Infrastructure can be swapped (e.g., from EF Core to Dapper) without affecting domain. Database vendor agnostic.

- ✅ **CQRS Optimization:** Read and write models optimized separately. Queries can bypass domain models for performance.

- ✅ **Context Integrity:** Anti-Corruption Layer prevents external changes from polluting domain. Each context maintains its own ubiquitous language.

- ✅ **Event-Driven Capabilities:** Domain events enable reactive behavior, audit trails, and eventual consistency patterns.


### Cons

- ❌ **High Initial Complexity:** Steep learning curve for developers unfamiliar with DDD. More upfront design and architecture decisions required.

- ❌ **Significant Boilerplate:** Multiple layers, interfaces, and transformations increase code volume. Simple CRUD operations require many files.

- ❌ **Over-engineering Risk:** Can be excessive for simple domains or small applications. Not all business problems require this level of sophistication.

- ❌ **Development Speed Trade-off:** Initial feature development is slower due to architectural ceremony. ROI comes from long-term maintainability.

- ❌ **Team Skill Requirements:** Requires developers proficient in DDD, OOP principles, and architectural patterns. Junior developers may struggle.

- ❌ **Performance Overhead:** Multiple layer traversals and object transformations add latency. Value Objects and Aggregates create more object allocations.

- ❌ **Bounded Context Boundaries:** Difficult to identify correct boundaries initially. Poor boundaries lead to tight coupling and integration headaches.

- ❌ **Communication Complexity:** ACL and inter-context communication add integration points. Eventual consistency between contexts can complicate business flows.


---

## 7. When to Use This Structure

### ✅ Use this structure when:

- **Complex Business Domains:** The application has rich business logic, complex workflows, and non-trivial business rules that require encapsulation.

- **Multiple Business Domains:** Your application spans several distinct business capabilities (e.g., Catalog, Orders, Inventory, Shipping) that benefit from separate bounded contexts.

- **Long-Term Projects:** Building enterprise applications expected to evolve and be maintained for years. The upfront investment pays off over time.

- **Large Development Teams:** Multiple teams working on different parts of the system. Bounded contexts enable parallel development with clear boundaries.

- **High Change Frequency:** Business requirements evolve frequently. The architecture enables changes to be localized within bounded contexts.

- **Event-Driven Requirements:** System needs audit trails, event sourcing, or reactive behaviors triggered by domain events.

- **Testability is Critical:** Comprehensive unit testing of business logic is a requirement. Domain isolation makes testing straightforward.

- **Microservices Migration Path:** Planning eventual decomposition into microservices. Bounded contexts provide natural service boundaries.


### ❌ Consider simpler alternatives when:

- **Simple CRUD Applications:** The application is primarily data entry/retrieval with minimal business logic. A simpler layered architecture or even MVC may suffice.

- **Tight Deadlines:** Building an MVP or prototype where time-to-market is critical. The architectural overhead slows initial development.

- **Small Team or Solo Developer:** Limited development resources may struggle with the complexity. Simpler structures reduce cognitive load.

- **Well-Defined, Stable Domain:** Business logic is simple and unlikely to change. The flexibility of DDD provides limited value.

- **Read-Heavy Workloads:** Application is primarily querying and displaying data. CQRS adds unnecessary complexity if you're not leveraging write-side benefits.

- **Greenfield Uncertainty:** Domain understanding is still evolving. Consider starting simpler and refactoring toward DDD as the domain crystallizes.


---

## 8. Testing Strategy

### Unit Tests

**Domain Layer Testing:**

- **Focus:** Test Aggregates, Entities, Value Objects, and Domain Services in complete isolation.
- **No Dependencies:** Zero external dependencies. Tests run in memory without database or frameworks.
- **What to Test:**
    - Aggregate factory methods enforce business rules
    - Business methods maintain invariants
    - Domain events are raised correctly
    - Value Object equality and immutability
- **Example:**

```csharp
public class ProductTests
{
    [Fact]
    public void Create_ValidProduct_ShouldRaiseProductCreatedEvent()
    {
        // Arrange
        var code = new ProductCode("PRD-001");
        var price = new Money(99.99m, "USD");
        
        // Act
        var product = Product.Create(code, "Test Product", price, categoryId: 1);
        
        // Assert
        product.Should().NotBeNull();
        product.DomainEvents.Should().ContainSingle(e => e is ProductCreatedEvent);
    }
    
    [Fact]
    public void ChangePrice_NegativePrice_ShouldThrowException()
    {
        // Arrange
        var product = CreateValidProduct();
        var invalidPrice = new Money(-10m, "USD");
        
        // Act & Assert
        var act = () => product.ChangePrice(invalidPrice);
        act.Should().Throw<ArgumentException>();
    }
}
```

**Application Layer Testing:**

- **Focus:** Test Command/Query Services with mocked repositories.
- **Mock Dependencies:** Use mocking frameworks (Moq, NSubstitute) for repository and infrastructure abstractions.
- **What to Test:**
    - Service orchestration logic
    - Validation rules
    - Repository interactions
    - Exception handling
- **Example:**

```csharp
public class ProductCommandServiceTests
{
    private readonly Mock<IProductRepository> _repositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly ProductCommandService _service;
    
    public ProductCommandServiceTests()
    {
        _repositoryMock = new Mock<IProductRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _service = new ProductCommandService(
            _repositoryMock.Object,
            _unitOfWorkMock.Object,
            Mock.Of<ILogger<ProductCommandService>>());
    }
    
    [Fact]
    public async Task Handle_CreateProduct_ShouldCallRepository()
    {
        // Arrange
        var command = new CreateProductCommand("PRD-001", "Test", "Desc", 99.99m, "USD", 1);
        _repositoryMock
            .Setup(r => r.ExistsWithCodeAsync(It.IsAny<ProductCode>()))
            .ReturnsAsync(false);
        
        // Act
        await _service.Handle(command);
        
        // Assert
        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Product>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CompleteAsync(), Times.Once);
    }
}
```

### Integration Tests

**Infrastructure Layer Testing:**

- **Focus:** Test repository implementations against real database (using test containers or in-memory database).
- **What to Test:**
    - EF Core configurations work correctly
    - Queries return expected results
    - Transactions and Unit of Work behavior
    - Database migrations apply successfully

**Example:**

```csharp
public class ProductRepositoryIntegrationTests : IClassFixture<DatabaseFixture>
{
    private readonly AppDbContext _context;
    private readonly ProductRepository _repository;
    
    public ProductRepositoryIntegrationTests(DatabaseFixture fixture)
    {
        _context = fixture.CreateContext();
        _repository = new ProductRepository(_context);
    }
    
    [Fact]
    public async Task FindByCodeAsync_ExistingProduct_ShouldReturnProduct()
    {
        // Arrange
        var code = new ProductCode("PRD-TEST");
        var product = Product.Create(code, "Test", new Money(10m, "USD"), 1);
        await _repository.AddAsync(product);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _repository.FindByCodeAsync(code);
        
        // Assert
        result.Should().NotBeNull();
        result.Code.Should().Be(code);
    }
}
```

### End-to-End Tests

**API Layer Testing:**

- **Focus:** Test complete request/response flows through controllers.
- **Tools:** WebApplicationFactory, REST Client, Postman/Newman
- **What to Test:**
    - HTTP endpoints return correct status codes
    - Request validation works
    - Authentication/authorization
    - Complete business workflows

**Example:**

```csharp
public class ProductsControllerE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public ProductsControllerE2ETests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task CreateProduct_ValidRequest_ShouldReturn201()
    {
        // Arrange
        var request = new CreateProductResource(
            "PRD-E2E", "E2E Product", "Test", 49.99m, "USD", 1);
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<ProductResource>();
        product.Should().NotBeNull();
        product.Code.Should().Be("PRD-E2E");
    }
}
```

---

## 9. Related Concepts

- [[Domain-Driven Design]]
- [[Bounded Context Pattern]]
- [[Layered Architecture]]
- [[CQRS Pattern]]
- [[Repository Pattern]]
- [[Unit of Work Pattern]]
- [[Anti-Corruption Layer]]
- [[Aggregate Pattern]]
- [[Value Object Pattern]]
- [[Domain Events]]
- [[Mediator Pattern]]
- [[Assembler Pattern]]
- [[Dependency Inversion Principle]]
- [[SOLID Principles]]
- [[Ubiquitous Language]]
- [[Event Sourcing]]

---

## 10. Additional Resources

### Official Documentation

- **Microsoft .NET Documentation:** https://learn.microsoft.com/en-us/dotnet/
- **Entity Framework Core:** https://learn.microsoft.com/en-us/ef/core/
- **ASP.NET Core Web API:** https://learn.microsoft.com/en-us/aspnet/core/web-api/
- **Domain-Driven Design Reference:** https://www.domainlanguage.com/ddd/reference/

### Recommended Libraries/Tools

- **MediatR:** In-process messaging for implementing CQRS and mediator patterns. Decouples command/query senders from handlers.
- **FluentValidation:** Strongly-typed validation rules for commands and resources. More expressive than Data Annotations.
- **AutoMapper:** Object-to-object mapping (alternative to manual Assemblers). Reduces boilerplate for simple transformations.
- **Swashbuckle (Swagger):** Automatic API documentation generation from controller attributes. Essential for API discoverability.
- **Serilog:** Structured logging framework with rich sinks (file, database, cloud). Superior to default .NET logging.
- **Bogus:** Fake data generation for testing. Creates realistic test entities quickly.
- **xUnit / NUnit:** Unit testing frameworks. xUnit is more modern and preferred for .NET Core.
- **FluentAssertions:** Assertion library making tests more readable and expressive.
- **Moq / NSubstitute:** Mocking frameworks for creating test doubles.
- **Testcontainers:** Provides lightweight Docker containers for integration tests with real databases.
- **Polly:** Resilience and transient-fault-handling library (retry, circuit breaker, timeout policies).

### Community Examples

- **eShopOnContainers:** Microsoft reference application - https://github.com/dotnet-architecture/eShopOnContainers
- **Clean Architecture Solution Template:** Jason Taylor's template - https://github.com/jasontaylordev/CleanArchitecture
- **Modular Monolith:** Kamil Grzybek's example - https://github.com/kgrzybek/modular-monolith-with-ddd
- **CQRS Journey:** Microsoft patterns & practices - https://github.com/microsoftarchive/cqrs-journey

### Books

- **Domain-Driven Design** by Eric Evans (Blue Book)
- **Implementing Domain-Driven Design** by Vaughn Vernon (Red Book)
- **Clean Architecture** by Robert C. Martin
- **Patterns of Enterprise Application Architecture** by Martin Fowler

---

## 11. Migration Notes

### Migrating from Layered Monolith without DDD

If migrating from a traditional 3-layer architecture (Presentation, Business Logic, Data Access):

**Phase 1: Introduce Bounded Contexts**

1. Identify distinct business domains in your existing codebase
2. Create separate folders for each bounded context
3. Move related controllers, services, and repositories into context folders
4. No code changes yet - just reorganization

**Phase 2: Refactor Domain Layer**

1. Extract business logic from services into domain entities
2. Introduce Aggregates to enforce consistency boundaries
3. Replace primitive types with Value Objects where appropriate
4. Define repository interfaces in Domain layer (move from Data layer)

**Phase 3: Implement CQRS**

1. Split existing services into CommandServices and QueryServices
2. Introduce Command and Query objects
3. Optimize read queries to bypass domain models when appropriate

**Phase 4: Add Anti-Corruption Layer**

1. Identify cross-context dependencies
2. Create ACL facades for each context
3. Refactor direct dependencies to use ACL interfaces

**Phase 5: Introduce Domain Events**

1. Identify side effects that should be event-driven
2. Implement event handlers
3. Configure MediatR pipeline for event dispatch

### Considerations and Gotchas

- **Database Schema Changes:** Refactoring toward Aggregates may require schema changes. Use EF Core migrations carefully.
- **Performance Impact:** Initial implementation may be slower due to additional layers. Profile and optimize hot paths.
- **Team Training:** Allocate time for team to learn DDD concepts. Pair programming helps knowledge transfer.
- **Incremental Adoption:** Don't attempt to refactor entire codebase at once. Pick one bounded context as pilot.
- **Value Object Mapping:** EF Core Owned Entities have quirks. Test Value Object configurations thoroughly.
- **Transaction Boundaries:** Be explicit about transaction scopes. Unit of Work should align with Aggregate boundaries.

### Incremental Adoption Strategy

**Week 1-2:** Team training on DDD fundamentals. Identify bounded context candidates.

**Week 3-4:** Implement Shared Kernel with base classes and interfaces. Set up project structure.

**Week 5-8:** Refactor one bounded context end-to-end as proof of concept. Document learnings.

**Week 9+:** Gradually refactor remaining contexts. Run old and new implementations in parallel where possible.

Remember: **You don't have to implement everything at once**. Start with core DDD building blocks (Aggregates, Value Objects, Repositories) and add complexity (CQRS, Events, ACL) as needed.

---

## 12. Domain Events vs Integration Events

### Understanding the Distinction

A common source of confusion in DDD implementations is when to use **Domain Events** versus **Integration Events**. Both represent "something that happened," but they serve different purposes and operate at different scopes.

### Domain Events

**Definition**: Events that represent significant business occurrences within a bounded context or within the same application process (monolith).

**Characteristics**:

- ✅ **In-Process Communication**: Published and consumed within the same application instance
- ✅ **Transaction Boundary**: Can be part of the same Unit of Work transaction
- ✅ **Synchronous or Async**: Typically synchronous (immediate execution), but can be deferred
- ✅ **Strong Consistency**: Changes happen atomically - all succeed or all fail
- ✅ **Domain Model Part**: Defined in the Domain layer as part of ubiquitous language
- ✅ **Internal Contract**: Schema can evolve freely as only internal code consumes them

**When to Use Domain Events in Monoliths**:

- Side effects within the same bounded context (e.g., Product created → Update inventory count)
- Communication between bounded contexts in the same process (e.g., Order placed → Catalog reserves stock)
- Implementing eventual consistency within transaction boundaries
- Audit logging and domain event sourcing
- Triggering workflows that must complete in the same transaction

**Example Domain Event Implementation**:

```csharp
// Domain/Model/Events/ProductCreatedEvent.cs
namespace CatalogContext.Domain.Model.Events;

public record ProductCreatedEvent : IEvent
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; }
    public decimal Price { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    public ProductCreatedEvent(int productId, string productCode, decimal price)
    {
        ProductId = productId;
        ProductCode = productCode;
        Price = price;
    }
}
```

**Raising Domain Events from Aggregates**:

```csharp
// Domain/Model/Aggregates/Product.cs
public class Product : BaseAggregateRoot
{
    private readonly List<IEvent> _domainEvents = new();
    public IReadOnlyCollection<IEvent> DomainEvents => _domainEvents.AsReadOnly();
    
    public static Product Create(ProductCode code, string name, Money price, int categoryId)
    {
        var product = new Product
        {
            Code = code,
            Name = name,
            Price = price,
            CategoryId = categoryId,
            IsActive = true
        };
        
        // Raise domain event - stored in aggregate
        product.RaiseDomainEvent(new ProductCreatedEvent(
            product.Id, 
            product.Code.Value, 
            product.Price.Amount));
        
        return product;
    }
    
    public void RaiseDomainEvent(IEvent @event)
    {
        _domainEvents.Add(@event);
    }
    
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}
```

**Dispatching Domain Events in Unit of Work**:

```csharp
// Shared/Infrastructure/Persistence/EFC/Repositories/UnitOfWork.cs
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private readonly IMediator _mediator;

    public UnitOfWork(AppDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<int> CompleteAsync()
    {
        // Step 1: Dispatch domain events BEFORE SaveChanges
        // This ensures event handlers run in the same transaction
        await DispatchDomainEventsAsync();
        
        // Step 2: Commit all changes atomically
        var result = await _context.SaveChangesAsync();
        
        return result;
    }

    private async Task DispatchDomainEventsAsync()
    {
        // Find all aggregates with pending events
        var aggregatesWithEvents = _context.ChangeTracker
            .Entries<BaseAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        // Collect all events
        var domainEvents = aggregatesWithEvents
            .SelectMany(a => a.DomainEvents)
            .ToList();

        // Clear events from aggregates
        aggregatesWithEvents.ForEach(a => a.ClearDomainEvents());

        // Publish each event through mediator
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent);
        }
    }
}
```

**Domain Event Handler Example**:

```csharp
// Application/Internal/EventHandlers/ProductCreatedEventHandler.cs
namespace CatalogContext.Application.Internal.EventHandlers;

public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ILogger<ProductCreatedEventHandler> _logger;

    public ProductCreatedEventHandler(
        IInventoryRepository inventoryRepository,
        ILogger<ProductCreatedEventHandler> logger)
    {
        _inventoryRepository = inventoryRepository;
        _logger = logger;
    }

    public async Task Handle(ProductCreatedEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Handling ProductCreatedEvent for Product {ProductId}", 
            @event.ProductId);

        // Create inventory record in same transaction
        var inventoryItem = new InventoryItem(
            productId: @event.ProductId,
            initialStock: 0,
            reorderLevel: 10);

        await _inventoryRepository.AddAsync(inventoryItem);
        
        // No need to call UnitOfWork.CompleteAsync() here
        // The original transaction will commit everything
        
        _logger.LogInformation(
            "Inventory item created for Product {ProductId}", 
            @event.ProductId);
    }
}
```

---

### Integration Events

**Definition**: Events that represent significant business occurrences meant for consumption by external systems, separate bounded contexts (microservices), or asynchronous workflows outside transaction boundaries.

**Characteristics**:

- ✅ **Out-of-Process Communication**: Published to message brokers (RabbitMQ, Azure Service Bus, Kafka)
- ✅ **Transaction Independence**: Cannot share database transactions with the publisher
- ✅ **Asynchronous**: Always fire-and-forget with eventual consistency
- ✅ **Durable**: Persisted in message broker for reliability
- ✅ **Public Contract**: Schema must be versioned and backward compatible
- ✅ **Cross-System**: Consumed by external microservices, webhooks, or third-party systems

**When to Use Integration Events**:

- Communication with external systems (payment gateways, CRM, analytics platforms)
- Cross-bounded-context communication in microservices architectures
- Publishing events to external consumers (webhooks, mobile apps)
- Event-driven workflows with eventual consistency requirements
- Preparing monolith for future decomposition into microservices

**Example Integration Event Definition**:

```csharp
// Infrastructure/Messaging/Events/ProductPublishedIntegrationEvent.cs
namespace CatalogContext.Infrastructure.Messaging.Events;

/// <summary>
/// Integration event published when a product becomes available externally.
/// IMPORTANT: This is a public contract - breaking changes affect external consumers.
/// </summary>
public class ProductPublishedIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = nameof(ProductPublishedIntegrationEvent);
    public string EventVersion { get; init; } = "v1"; // Versioning for schema evolution
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    
    // Business data
    public int ProductId { get; init; }
    public string ProductCode { get; init; }
    public string ProductName { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; }
    public string CategoryName { get; init; }
    public bool IsAvailable { get; init; }
}
```

**Publishing Integration Events from Domain Event Handlers**:

```csharp
// Application/Internal/EventHandlers/ProductCreatedEventHandler.cs
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IEventBus _eventBus; // Message broker abstraction
    private readonly IProductRepository _productRepository;
    private readonly ILogger<ProductCreatedEventHandler> _logger;

    public async Task Handle(ProductCreatedEvent domainEvent, CancellationToken ct)
    {
        // 1. Handle internal side effects (same transaction)
        var inventoryItem = new InventoryItem(domainEvent.ProductId, initialStock: 0);
        await _inventoryRepository.AddAsync(inventoryItem);
        
        _logger.LogInformation("Inventory created for Product {ProductId}", domainEvent.ProductId);
        
        // 2. Note: Integration event will be published AFTER transaction commits
        // This is handled by the Outbox Pattern (see below)
    }
}
```

**Outbox Pattern Implementation** (Recommended for Reliability):

```csharp
// Shared/Infrastructure/Messaging/Outbox/OutboxMessage.cs
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; }
    public string Payload { get; set; } // JSON serialized event
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
}

// Shared/Infrastructure/Messaging/Outbox/IOutboxRepository.cs
public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message);
    Task<IEnumerable<OutboxMessage>> GetUnprocessedAsync(int batchSize = 50);
    Task MarkAsProcessedAsync(Guid messageId);
    Task MarkAsFailedAsync(Guid messageId, string errorMessage);
}
```

**Saving to Outbox in Same Transaction**:

```csharp
// Application/Internal/EventHandlers/ProductCreatedEventHandler.cs
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    private readonly IOutboxRepository _outbox;
    private readonly IProductRepository _productRepository;

    public async Task Handle(ProductCreatedEvent domainEvent, CancellationToken ct)
    {
        // 1. Fetch additional data for integration event
        var product = await _productRepository.FindByIdAsync(domainEvent.ProductId);
        if (product == null) return;

        // 2. Map to integration event
        var integrationEvent = new ProductPublishedIntegrationEvent
        {
            ProductId = product.Id,
            ProductCode = product.Code.Value,
            ProductName = product.Name,
            Price = product.Price.Amount,
            Currency = product.Price.Currency,
            CategoryName = product.Category?.Name ?? "Uncategorized",
            IsAvailable = product.IsActive
        };

        // 3. Save to outbox table (same transaction as domain changes)
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = integrationEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent),
            OccurredAt = DateTime.UtcNow
        };

        await _outbox.AddAsync(outboxMessage);
        
        // 4. Background worker will publish from outbox to message broker
        // This ensures at-least-once delivery even if message broker is unavailable
    }
}
```

**Background Worker to Process Outbox**:

```csharp
// Shared/Infrastructure/Messaging/Outbox/OutboxProcessor.cs
public class OutboxProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceProvider serviceProvider, ILogger<OutboxProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

                // Get unprocessed messages
                var messages = await outboxRepo.GetUnprocessedAsync(batchSize: 50);

                foreach (var message in messages)
                {
                    try
                    {
                        // Publish to message broker (RabbitMQ, Azure Service Bus, etc.)
                        await eventBus.PublishAsync(message.EventType, message.Payload);
                        
                        // Mark as processed
                        await outboxRepo.MarkAsProcessedAsync(message.Id);
                        
                        _logger.LogInformation("Published outbox message {MessageId}", message.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to publish message {MessageId}", message.Id);
                        await outboxRepo.MarkAsFailedAsync(message.Id, ex.Message);
                    }
                }

                // Poll every 5 seconds
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in outbox processor");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
```

---

### Decision Matrix: Domain Events vs Integration Events

|Scenario|Event Type|Reason|
|---|---|---|
|Product created → Create inventory record (same DB)|**Domain Event**|Same transaction, strong consistency|
|Order placed → Update product stock count|**Domain Event**|Same bounded context, immediate consistency|
|User registered → Send welcome email|**Domain Event** (async handler) or **Integration Event**|If email failure shouldn't block registration, use Integration Event|
|Product price changed → Notify price monitoring service|**Integration Event**|External system, eventual consistency acceptable|
|Payment completed → Update order status|**Domain Event** if same DB, **Integration Event** if separate microservice|Depends on deployment architecture|
|Invoice generated → Send to accounting system|**Integration Event**|Cross-system integration, external consumer|
|Product published → Sync to search index (Elasticsearch)|**Integration Event**|External infrastructure, eventual consistency|
|Order canceled → Refund payment via payment gateway|**Integration Event**|External API call, idempotency required|

---

### Recommended Approach for Monoliths

#### **Phase 1: Start with Domain Events Only**

For most monolithic applications, Domain Events are sufficient and simpler:

```csharp
// Simple in-process event handling
public class Product : BaseAggregateRoot
{
    public static Product Create(...)
    {
        var product = new Product { ... };
        product.RaiseDomainEvent(new ProductCreatedEvent(...));
        return product;
    }
}

// Handler executes in same transaction
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    public async Task Handle(ProductCreatedEvent @event, CancellationToken ct)
    {
        // Update inventory, create audit log, etc.
        // All in same transaction - strong consistency
    }
}
```

**Benefits**:

- ✅ Simpler infrastructure (no message broker needed)
- ✅ Strong consistency guarantees
- ✅ Easier debugging (synchronous flow)
- ✅ Lower operational complexity

#### **Phase 2: Add Integration Events When Needed**

Introduce Integration Events when you encounter:

- External system integration requirements
- Need for asynchronous, fire-and-forget workflows
- Preparing for microservices decomposition
- Event-driven architectures with multiple consumers

```csharp
// Evolution: Domain Event → Integration Event
public class ProductCreatedEventHandler : INotificationHandler<ProductCreatedEvent>
{
    private readonly IOutboxRepository _outbox;
    
    public async Task Handle(ProductCreatedEvent domainEvent, CancellationToken ct)
    {
        // 1. Internal side effects (same transaction)
        await HandleInternalSideEffects(domainEvent);
        
        // 2. Save integration event to outbox (same transaction)
        var integrationEvent = MapToIntegrationEvent(domainEvent);
        await _outbox.AddAsync(CreateOutboxMessage(integrationEvent));
        
        // 3. Background worker publishes to message broker later
    }
}
```

---

### Key Takeaways

1. **Domain Events = Internal Communication**: Use for side effects within your application, even across bounded contexts in a monolith.

2. **Integration Events = External Communication**: Use for publishing to external systems, microservices, or when eventual consistency is acceptable.

3. **Start Simple**: Begin with Domain Events. Add Integration Events only when you have concrete external integration needs.

4. **Outbox Pattern is Essential**: If you use Integration Events, implement the Outbox Pattern to guarantee delivery and maintain consistency.

5. **Transaction Boundaries Matter**: Domain Events can participate in transactions; Integration Events cannot.

6. **Schema Stability**: Domain Event schemas can evolve freely. Integration Event schemas are public contracts requiring versioning.


### Related Concepts

- [[Domain Events Pattern]]
- [[Integration Events Pattern]]
- [[Outbox Pattern]]
- [[Transactional Outbox]]
- [[Event-Driven Architecture]]
- [[Eventual Consistency]]
- [[At-Least-Once Delivery]]