<img src="assets/Kepler.Core.jpg" alt="Kepler.Core Icon" width="128"/>

# **Kepler.Core**

Typed policies for EF Core: projections, filters, nested navigation, and security — all defined in one fluent, compile-safe class.

📌 **Status:** BETA — Active development  
Kepler.Core is stable for basic usage, but APIs may change as real-world feedback comes in.

---

## **What is Kepler.Core?**

Kepler.Core is a lightweight extension for Entity Framework Core that centralizes what data may be fetched, filtered, ordered, or traversed from an entity — using a single policy per entity.

It helps:
- ✅ Eliminate over-fetching (select only what you need)
- ✅ Enforce strict API contracts (control what's exposed)
- ✅ Simplify DTO-driven development (less boilerplate)
- ✅ Debug with confidence (see generated SQL + projection lambda)

---

## **Quick Start**

### **1. Install**
```bash
dotnet add package Kepler.Core
```

### **2. Define a Policy**
```csharp
using Kepler.Core.Builder;
using Kepler.Core.Enums;
using YourDomain.Entities;

[KeplerPolicyName("Public")]
public class ProductPublicPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        builder
            .AllowFields(x => x.Name, x => x.Price, x => x.Id)
            .AllowFilter(x => x.Name, FilterOperationEnum.StartsWith)
            .AllowOrderBy(x => x.Price, x => x.SellStartDate);
    }
}


[KeplerPolicyName("Nav")]
public class ProductNavtigationPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        builder
         .AllowFields(x => x.Color!, x => x.Name!, x => x.MakeFlag, x => x.SellStartDate, x => x.ProductID)


          // NEW GENERIC NESTED SUPPORT
         .AllowNestedFields(x => x.ProductCostHistories, x => x.SelectFields<ProductCostHistory>(x => x.ProductID, x => x.StartDate, x => x.StandardCost))

         .AllowNestedFields(x => x.ProductModel!, x => x.SelectFields(x => x.ProductModelID, x => x.ModifiedDate!))

         .AllowOrderBy(x => x.Name!, x => x.SellStartDate)

         .AllowFilter(x => x.MakeFlag, FilterOperationEnum.Equals)

         .AllowFilter(x => x.ProductID, FilterOperationEnum.Equals)

         .AllowFilter(x => x.Name, FilterOperationEnum.StartsWith);
    }
}


```

### **3. Register Policies**
```csharp
builder.Services.AddKepler()
    .AddKeplerPolicy<Product, ProductPublicPolicy>()
    .ValidateKeplerPolicies();
```

### **4. Apply in Queries**

**Basic usage:**
```csharp
var products = await query
    .ApplyKeplerPolicy(KeplerPolicyConfig.Create("Public", filters: dto))
    .ToListAsync();
```

**With SQL visibility:**
```csharp
var products = await query
    .ApplyKeplerPolicy(
        KeplerPolicyConfig.CreateWithSql("Public", filters: dto),
        out string? generatedSql)
    .ToListAsync();

Console.WriteLine(generatedSql);
// SELECT [Name], [Price], [Id] FROM [Products] WHERE [Name] LIKE @p0
```

**With Lambda inspection:**
```csharp
var products = await query
    .ApplyKeplerPolicy(
        KeplerPolicyConfig.CreateWithLambda("Public", filters: dto),
        out Expression? projectionLambda)
    .ToListAsync();

Console.WriteLine(projectionLambda);
// {x => new Product() {Name = x.Name, Price = x.Price, Id = x.Id}}
```

**With Full Debug Info (SQL + Lambda):**
```csharp
var products = await query
    .ApplyKeplerPolicy(
        KeplerPolicyConfig.CreateWithFullDebug("Public", filters: dto),
        out KeplerDebugInfo? debug)
    .ToListAsync();

Console.WriteLine($"SQL: {debug?.GeneratedSql}");
Console.WriteLine($"Lambda: {debug?.ProjectionLambda}");
```

---

## **Ordering & Pagination**

```csharp
// Order by SellStartDate descending
var products = await query
    .ApplyKeplerPolicy(config)
    .ApplyKeplerOrdering(KeplerOrderingConfig.CreateDescending("Public", "SellStartDate"), out string? sql)
    .ApplyKeplerPagination(page: 1, pageSize: 10)
    .ToListAsync();

// Chain multiple order by
var products = await query
    .ApplyKeplerPolicy(config)
    .ApplyKeplerOrdering(KeplerOrderingConfig.CreateDescending("Public", "CreatedAt"))
    .ThenApplyKeplerOrdering(KeplerOrderingConfig.CreateAscending("Public", "Name"))
    .ToListAsync();

var productsWithCount = query
    .ApplyKeplerPolicy(config)
    .ApplyKeplerOrdering(KeplerOrderingConfig.CreateDescending("Public", "SellStartDate"))
    .ApplyKeplerPaginationWithCount(page: 1, pageSize: 10, out int totalCount)
    .ToList();


```

---

## **Key Features**

### **Fluent Policy Builder**
```csharp
builder
    .AllowFields(x => x.Name, x => x.Email)
    .AllowFilter(x => x.Status, FilterOperationEnum.Equals)
    .AllowOrderBy(x => x.CreatedAt)
    .MaxDepth(2);  // Limit nested traversal depth
```

### **Global Field Exclusions**
Exclude sensitive fields automatically (across all policies):

```csharp
// Option 1: Via attribute
[KeplerGlobalExclude("Contains PII")]
public string Password { get; set; }

// Option 2: Via EF Core config
builder.Entity<User>()
    .GloballyExclude(x => x.ApiKey, x => x.InternalNotes);
```

### **Nested Navigation Projections**
```csharp
builder
    .AllowNestedFields(x => x.Orders, nested =>
        nested.SelectFields(x => x.Id, x => x.Total)
    );
```

---

## **Factory Methods**

Clean, self-documenting configuration:

```csharp
// Projection
KeplerPolicyConfig.Create("PolicyName")
KeplerPolicyConfig.CreateWithLambda("PolicyName")
KeplerPolicyConfig.CreateWithSql("PolicyName")
KeplerPolicyConfig.CreateWithFullDebug("PolicyName")

// Ordering
KeplerOrderingConfig.Create("PolicyName", "FieldName")
KeplerOrderingConfig.CreateAscending("PolicyName", "FieldName")
KeplerOrderingConfig.CreateDescending("PolicyName", "FieldName")
KeplerOrderingConfig.CreateWithSql("PolicyName", "FieldName")

// All support optional filters and ignoreGlobalExceptions
KeplerPolicyConfig.CreateWithSql("PolicyName", filters: dto, ignoreGlobalExceptions: false)
```

---

## **Why Kepler?**

### **Before Kepler:**
```csharp
// ❌ Manual projections (repetitive)
var products = await query
    .Select(x => new ProductDto 
    { 
        Id = x.Id, 
        Name = x.Name, 
        Price = x.Price 
    })
    .ToListAsync();

// ❌ Over-fetching (loads everything)
var products = await query.ToListAsync();

// ❌ No visibility into what's happening
```

### **With Kepler:**
```csharp
// ✅ One-liner, type-safe, controlled
var products = await query
    .ApplyKeplerPolicy(KeplerPolicyConfig.Create("Public"))
    .ToListAsync();

// ✅ See exactly what's being fetched
var products = await query
    .ApplyKeplerPolicy(
        KeplerPolicyConfig.CreateWithSql("Public"),
        out string? sql)
    .ToListAsync();
```

---

## **License**

MIT — see LICENSE.

---

## **Contributing**

If Kepler solves your problem:

⭐ **Star the repo**  
🐞 **Report issues**  
💬 **Suggest improvements**

Built with ❤️ by Mohammad Ali Ebrahimzadeh