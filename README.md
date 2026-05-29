<img src="assets/Kepler.Core.jpg" alt="Kepler.Core Icon" width="128"/>

# **Kepler.Core**

Typed policies for EF Core: projections, filters, nested navigation, and security — all defined in one fluent, compile-safe class.

📌 **Status:** BETA — Active development  
Kepler.Core is stable for basic usage, but APIs may change as real-world feedback comes in.

[![NuGet](https://img.shields.io/nuget/v/Kepler.Core)](https://www.nuget.org/packages/Kepler.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Kepler.Core)](https://www.nuget.org/packages/Kepler.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## **What is Kepler.Core?**

Kepler.Core is a lightweight extension for Entity Framework Core that centralizes what data may be fetched, filtered, ordered, or traversed from an entity — using a single policy per entity.

It helps:
- ✅ Eliminate over-fetching (select only what you need)
- ✅ Enforce strict API contracts (control what fields are exposed per role)
- ✅ Simplify DTO-driven development (less boilerplate, no manual projections)
- ✅ Secure sensitive fields globally with `[KeplerGlobalExclude]`
- ✅ Traverse deep nested navigation graphs with full control
- ✅ Debug with confidence (see generated SQL + projection lambda)

---

## **Why Kepler?**

### Before Kepler:
```csharp
// ❌ Manual projections — repetitive and fragile
var products = await query
    .Select(x => new ProductDto
    {
        Id = x.Id,
        Name = x.Name,
        Price = x.Price
    })
    .ToListAsync();

// ❌ Over-fetching — loads everything including sensitive fields
var products = await query.ToListAsync();

// ❌ No visibility, no access control, no consistency
```

### With Kepler:
```csharp
// ✅ One-liner, type-safe, controlled
var products = await query
    .ApplyKeplerPolicy(KeplerPolicyConfig.Create("Public"))
    .ToListAsync();

// ✅ See exactly what SQL is being generated
var products = await query
    .ApplyKeplerPolicy(
        KeplerPolicyConfig.CreateWithSql("Public"),
        out string? sql)
    .ToListAsync();
```

---

## **Quick Start**

### **1. Install**
```bash
dotnet add package Kepler.Core
```

### **2. Define a Policy**

A policy is a single class per entity (or per use case) that declares exactly what is allowed — fields, filters, ordering, and nested navigation.

**Simple policy:**
```csharp
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
```

**Multi-role policy:**
```csharp
[KeplerPolicyName("Product")]
public class ProductPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        // Public role — limited fields
        builder.For("Public")
            .AllowFields(x => x.Name, x => x.Price, x => x.ProductID)
            .AllowFilter(x => x.Name, FilterOperationEnum.StartsWith)
            .AllowOrderBy(x => x.Name, x => x.Price);

        // Admin role — more fields
        builder.For("Admin")
            .AllowAllExcept(x => x.rowguid)
            .AllowFilter(x => x.MakeFlag, FilterOperationEnum.Equals)
            .AllowFilter(x => x.ProductID, FilterOperationEnum.Equals)
            .AllowOrderBy(x => x.Name, x => x.SellStartDate);
    }
}
```

**Policy with nested navigation:**
```csharp
[KeplerPolicyName("Nav")]
public class ProductNavigationPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        builder
            .AllowFields(x => x.Color!, x => x.Name!, x => x.MakeFlag, x => x.SellStartDate, x => x.ProductID)

            // Nested collection with selective fields
            .AllowNestedFields(x => x.ProductCostHistories,
                nested => nested.SelectFields<ProductCostHistory>(
                    x => x.ProductID, x => x.StartDate, x => x.StandardCost))

            // Filtered nested collection — only items matching the condition
            .AllowFilteredNestedFields(x => x.ProductListPriceHistories.Where(x => x.ListPrice < 74),
                nested => nested.SelectFields<ProductListPriceHistory>(
                    x => x.ProductID, x => x.StartDate, x => x.ListPrice, x => x.ModifiedDate))

            .AllowFilteredNestedFields(x => x.ProductInventories.Where(x => x.Quantity <= 324),
                nested => nested.SelectFields<ProductInventory>(
                    x => x.ProductID, x => x.LocationID, x => x.Shelf!, x => x.Bin, x => x.Quantity))

            .AllowFilteredNestedFields(x => x.ProductReviews.Where(x => x.Rating == 5),
                nested => nested.SelectFields<ProductReview>(x => x.Rating, x => x.ProductReviewID))

            // Single navigation property
            .AllowNestedFields(x => x.ProductModel!,
                nested => nested.SelectFields<ProductModel>(x => x.ProductModelID, x => x.ModifiedDate!))

            .AllowOrderBy(x => x.Name!, x => x.SellStartDate)
            .AllowFilter(x => x.MakeFlag, FilterOperationEnum.Equals)
            .AllowFilter(x => x.ProductID, FilterOperationEnum.Equals)
            .AllowFilter(x => x.Name, FilterOperationEnum.StartsWith);
    }
}
```

**Deep nested traversal (multi-level `ThenInclude`):**
```csharp
[KeplerPolicyName("Deep")]
public class ProductDeepPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        builder.For("Default")
            .AllowFields(x => x.Name, x => x.ProductNumber)

            // Level 1: Product -> ProductSubcategory
            .AllowNestedFields(x => x.ProductSubcategory, sub =>
            {
                sub.SelectFields(x => x.ProductSubcategoryID, x => x.Name);

                // Level 2: ProductSubcategory -> ProductCategory
                sub.ThenInclude(x => x.ProductCategory, cat =>
                {
                    cat.SelectFields(x => x.ProductCategoryID, x => x.Name);

                    // Level 3: ProductCategory -> ProductSubcategories (back-reference)
                    cat.ThenInclude(x => x.ProductSubcategories, sub2 =>
                    {
                        sub2.SelectFields(x => x.Name);
                    });
                });
            });
    }
}
```

This generates the correct multi-level SQL:
```sql
SELECT [p].[Name], [p].[ProductNumber],
       [p0].[ProductSubcategoryID], [p0].[Name],
       [p1].[ProductCategoryID], [p1].[Name],
       [p2].[Name], [p2].[ProductSubcategoryID]
FROM [Production].[Product] AS [p]
LEFT JOIN [Production].[ProductSubcategory] AS [p0] ON [p].[ProductSubcategoryID] = [p0].[ProductSubcategoryID]
LEFT JOIN [Production].[ProductCategory] AS [p1] ON [p0].[ProductCategoryID] = [p1].[ProductCategoryID]
LEFT JOIN [Production].[ProductSubcategory] AS [p2] ON [p1].[ProductCategoryID] = [p2].[ProductCategoryID]
```

---

### **3. Register Policies**

**Register individually:**
```csharp
builder.Services.AddKepler()
    .AddKeplerPolicy<Product, ProductPublicPolicy>()
    .AddKeplerPolicy<Product, ProductNavigationPolicy>()
    .ValidateKeplerPolicies(); // throws at startup if any policy is missing
```

**Or auto-register from an assembly:**
```csharp
builder.Services.AddKepler()
    .AddKeplerPoliciesFromAssembly(typeof(ProductPublicPolicy).Assembly)
    .ValidateKeplerPolicies();
```

---

### **4. Apply in Queries**

**Basic:**
```csharp
var products = await query
    .ApplyKeplerPolicy(KeplerPolicyConfig.Create("Public", filters: dto))
    .ToListAsync();
```

**With role:**
```csharp
var products = await query
    .ApplyKeplerPolicy(KeplerPolicyConfig.Create("Product", filters: dto, role: "Admin"))
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
// SELECT [p].[Name], [p].[Price], [p].[Id] FROM [Products] WHERE [p].[Name] LIKE @p0
```

**With Lambda inspection:**
```csharp
var products = await query
    .ApplyKeplerPolicy(
        KeplerPolicyConfig.CreateWithLambda("Public", filters: dto),
        out Expression? projectionLambda)
    .ToListAsync();

Console.WriteLine(projectionLambda);
// x => new Product() {Name = x.Name, Price = x.Price, Id = x.Id}
```

**With full debug info (SQL + Lambda):**
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
// Single order by descending
var products = await query
    .ApplyKeplerPolicy(config)
    .ApplyKeplerOrdering(KeplerOrderingConfig.CreateDescending("Public", "SellStartDate"))
    .ApplyKeplerPagination(page: 1, pageSize: 10)
    .ToListAsync();

// Chain multiple order by
var products = await query
    .ApplyKeplerPolicy(config)
    .ApplyKeplerOrdering(KeplerOrderingConfig.CreateDescending("Public", "CreatedAt"))
    .ThenApplyKeplerOrdering(KeplerOrderingConfig.CreateAscending("Public", "Name"))
    .ToListAsync();

// Pagination with total count
var products = query
    .ApplyKeplerPolicy(config)
    .ApplyKeplerOrdering(KeplerOrderingConfig.CreateDescending("Public", "SellStartDate"))
    .ApplyKeplerPaginationWithCount(page: 1, pageSize: 10, out int totalCount)
    .ToList();
```

> Ordering is validated against `AllowOrderBy` — attempting to order by a field not in the policy throws a clear security error at runtime.

---

## **Global Field Exclusions**

Exclude sensitive fields automatically across **all** policies for an entity. These fields will never appear in any projection regardless of what a policy allows.

```csharp
// Option 1: Attribute on the property
public class User
{
    public int Id { get; set; }
    public string Name { get; set; }

    [KeplerGlobalExclude("Contains PII")]
    public string PasswordHash { get; set; }

    [KeplerGlobalExclude("Internal use only")]
    public string ApiKey { get; set; }
}

// Option 2: EF Core model configuration
modelBuilder.Entity<User>()
    .GloballyExclude(x => x.PasswordHash, x => x.ApiKey);
```

Global exclusions are scanned recursively through the entire navigation graph, so nested entities are protected too.

---

## **Inspecting Policy Configuration**

You can inspect the full resolved configuration of any policy at runtime — useful for debugging or building audit tooling.

```csharp
// Get full debug info
var info = KeplerPolicyHelper.GetPolicyConfiguration(typeof(Product), "Nav", role: "Default");

Console.WriteLine(info.AllowedFields);       // scalar fields
Console.WriteLine(info.NestedFieldPolicies); // navigation projections
Console.WriteLine(info.AllowedFilters);      // allowed filter operations
Console.WriteLine(info.AllowedOrderByFields);
Console.WriteLine(info.GlobalExclusions);

// Or print a formatted summary to console
KeplerPolicyHelper.PrintPolicyConfiguration(typeof(Product), "Nav", role: "Default");
```

---

## **Factory Methods Reference**

```csharp
// Policy config
KeplerPolicyConfig.Create("PolicyName")
KeplerPolicyConfig.Create("PolicyName", filters: dto, role: "Admin")
KeplerPolicyConfig.CreateWithLambda("PolicyName")
KeplerPolicyConfig.CreateWithSql("PolicyName")
KeplerPolicyConfig.CreateWithFullDebug("PolicyName")

// Ordering config
KeplerOrderingConfig.Create("PolicyName", "FieldName")
KeplerOrderingConfig.CreateAscending("PolicyName", "FieldName")
KeplerOrderingConfig.CreateDescending("PolicyName", "FieldName")
KeplerOrderingConfig.CreateWithSql("PolicyName", "FieldName")
```

All factory methods accept optional `filters`, `role`, and `ignoreGlobalExceptions` parameters.

---

## **Changelog**

### v1.1.x
- ✅ **Fixed:** Deep nested traversal via `ThenInclude` now correctly generates multi-level SQL JOINs
- ✅ **Fixed:** `BuildNestedBinding` now handles all collection types (`ICollection<>`, `IList<>`, `List<>`, `IEnumerable<>`)
- ✅ Children in `NestedFieldPolicy` are now recursively projected instead of being nulled out

### v1.0.x
- Initial release with policy builder, filters, ordering, pagination, global exclusions, and debug tooling

---

## **License**

MIT — see [LICENSE](LICENSE).

---

## **Contributing**

If Kepler solves your problem:

⭐ **Star the repo**  
🐞 **Report issues**  
💬 **Suggest improvements**

Built with ❤️ by Mohammad Ali Ebrahimzadeh