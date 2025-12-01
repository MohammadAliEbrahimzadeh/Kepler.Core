# **Kepler.Core**
Typed policies for EF Core: projections, filters, nested navigation, and security — all defined in one fluent, compile-safe class.

📌 Status: BETA — Active development
Kepler.Core is stable for basic usage, but APIs may change as real-world feedback comes in.

Kepler.Core is a lightweight extension for Entity Framework Core that centralizes what data may be fetched, filtered, ordered, or traversed from an entity — using a single policy per entity.
It helps eliminate over-fetching, enforce strict API contracts, and simplify DTO-driven development.

---

## **Quick Start**

```csharp
// ---------------------------------------------
// 1. Install
// dotnet add package Kepler.EFCore
// ---------------------------------------------


// ---------------------------------------------
// 2. Define a Policy
// ---------------------------------------------
using Kepler.Core.Builder;
using Kepler.Core.Enums;
using YourDomain.Entities;

[KeplerPolicyName("Public")]
public class ProductPublicPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        builder
            .AllowFields(x => x.Color!, x => x.Name!, x => x.MakeFlag, x => x.SellStartDate, x => x.ProductID)
            .AllowOrderBy(x => x.SellStartDate!)
            .AllowFilter(x => x.MakeFlag, FilterOperationEnum.Equals)
            .AllowFilter(x => x.ProductID, FilterOperationEnum.Equals)
            .AllowFilter(x => x.Name, FilterOperationEnum.StartsWith);
    }
}


// ---------------------------------------------
// 3. Register Policies
// ---------------------------------------------
builder.Services.AddKepler()
    .AddKeplerPoliciesFromAssembly(typeof(ProductPublicPolicy).Assembly)
    .ValidateKeplerPolicies();


// ---------------------------------------------
// 4. Apply in a Query
// ---------------------------------------------
public async Task<CustomResponse> GetProductsAsync(ProductFilterDto dto, CancellationToken cancellationToken)
{
    var productQuery = _unitOfWork.GetAsQueryable<Product>()
        .ApplyKeplerOrdering("Filter", x => x.SellStartDate, OrderOperationEnum.Ascending)
        .ApplyKeplerPolicy("Filter", dto);

    var count = await productQuery.CountAsync();

    var products = await productQuery
        .ApplyKeplerPagination()
        .ProjectToType<ProductTestDto>()
        .ToListAsync(cancellationToken);

    return new CustomResponse
    {
        Data = products,
        IsSuccess = true,
        Message = "Returned",
        StatusCode = HttpStatusCode.OK,
        TotalCount = count
    };
}

License

MIT — see LICENSE.

Contribute

If Kepler kills your CRUD pain:
⭐ Star the repo • 🐞 Submit issues • 💬 PRs welcome

Built with ❤️ by Mohammad Ali Ebrahimzadeh
