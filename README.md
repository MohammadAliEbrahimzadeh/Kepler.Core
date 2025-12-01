NuGet GitHub license .NET
Typed policies for EF Core: Projections, filters, nested navigation, and security in one fluent class. Slim queries, no leaks, faster execution. Built to kill CRUD hell.
Kepler.Core is a lightweight extension for Entity Framework Core that lets you define data access policies in one place: what fields to return, what filters/orders are allowed, and how to handle nests/depth. No more manual Includes, if-chains for filters, or DTO explosions. One policy class per entity — compile-safe, refactor-friendly, and impossible to misuse.
Born from frustration with base CRUD (600+ lines of reflection hell), Kepler gives you GraphQL power for REST without the schema ceremony. Tired of over-fetching, filter leaks, or "why is CreatedAt in every response?"? Kepler fixes it.

Quick Start
Installation
dotnet add package Kepler.EFCore

1. Define a Policy
using Kepler.Core.Builder;
using Kepler.Core.Enums;
using YourDomain.Entities;

[KeplerPolicyName("Public")]
public class ProductPublicPolicy : IKeplerPolicy<Product>
{
    public void Configure(IKeplerPolicyBuilder<Product> builder)
    {
        builder
            .AllowFields(p => p.Name, p => p.Color)  // Typed, safe — no strings
            .AllowFilter(p => p.Name, FilterOperation.Contains)  // Only allowed ops
            .AllowOrderBy(p => p.Name, OrderDirection.Ascending | OrderDirection.Descending)
            .AllowNestedFields(p => p.Category,  // Slim nests
                nested => nested.AllowFields(c => c.Name).MaxDepth(1));
    }
}

2. Register Policies
builder.Services.AddKepler()
    .AddKeplerPoliciesFromAssembly(typeof(ProductPublicPolicy).Assembly)
    .ValidateKeplerPolicies();


3. Use in Query (One Line)
  public async Task<CustomResponse> GetProductsAsync(ProductFilterDto dto, CancellationToken cancellationToken)
  {
      var product = _unitOfWork.GetAsQueryable<Product>()
          .ApplyKeplerOrdering("Filter", x => x.SellStartDate, OrderOperationEnum.Ascending)
          .ApplyKeplerPolicy("Filter", dto);

      var count = await product.CountAsync();

      var products = await product.ApplyKeplerPagination()
          .ProjectToType<ProductTestDto>().ToListAsync(cancellationToken);

      return new CustomResponse()
      {
          Data = products,
          IsSuccess = true,
          Message = "Returned",
          StatusCode = HttpStatusCode.OK,
          TotalCount = count
      };

  }

  v1.0.0 is beta — OSS under MIT.Happy for all feedback.

Installation & Usage
See Quick Start above.
License
MIT — see LICENSE.

⭐ Star if it kills your CRUD pain. Feedback? Issues. Built with ❤️ by Mohammad Ali Ebrahimzadeh.
