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

[KeplerPolicyName("ItemTest")]
public class ItemPolicy : IKeplerPolicy<Category>
{
    public void Configure(IKeplerPolicyBuilder<Category> builder)
    {
        builder
            .AllowFields(x => x.Name!, x => x.IsDeleted, x => x.Id)
            .AllowFilter(x => x.Name!, FilterOperationEnum.StartsWith)
            .AllowOrderBy(x => x.Id);
    }
}


// ---------------------------------------------
// 3. Register Policies
// ---------------------------------------------
builder.Services.AddKepler()
    .AddKeplerPolicy<Item, ItemPolicy>()
    .ValidateKeplerPolicies();


// ---------------------------------------------
// 4. Apply in a Query
// ---------------------------------------------
   public async Task<CustomResponse> KeplerTestAsync(CancellationToken cancellationToken)
    {
        var itemQueryable = _unitOfWork.GetAsQueryable<Category>(cancellationToken);

        var data = await itemQueryable.ApplyKeplerPolicy
            (new KeplerPolicyConfig { PolicyName = "ItemTest", ReturnLambdaExpression = true }, out Expression? debugLambda).ToListAsync();

        return new CustomResponse(data: data, isSuccess: true, message: ResponseMessages.DataRetrievedSuccess, statusCode: HttpStatusCode.OK);
    }

License

MIT — see LICENSE.

Contribute

If Kepler kills your CRUD pain:
⭐ Star the repo • 🐞 Submit issues • 💬 PRs welcome

Built with ❤️ by Mohammad Ali Ebrahimzadeh
