using System.Reflection;
using NetArchTest.Rules;

namespace TodayEcommerce.Core.ArchitectureTests;

/// <summary>
/// Guards the boundary between the Catalog and Ordering modules (the vertical cut, where
/// <see cref="LayerDependencyTests"/> guards the horizontal one). A module may use another module's
/// Contracts — its published integration events — but never its domain, use cases or handlers.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(Domain.AssemblyReference).Assembly,
        typeof(Application.AssemblyReference).Assembly,
    ];

    private static readonly string[] OrderingInternals =
    [
        "TodayEcommerce.Core.Domain.Ordering",
        "TodayEcommerce.Core.Application.Ordering.Commands",
        "TodayEcommerce.Core.Application.Ordering.Queries",
        "TodayEcommerce.Core.Application.Ordering.EventHandlers",
    ];

    // Ways to change Catalog state. ProductId stays allowed: referring to another aggregate by id is fine.
    private static readonly string[] CatalogWriteAccess =
    [
        "TodayEcommerce.Core.Domain.Catalog.IProductRepository",
        "TodayEcommerce.Core.Domain.Catalog.IStockReservationRepository",
        "TodayEcommerce.Core.Domain.Catalog.StockReservation",
        "TodayEcommerce.Core.Application.Catalog.Commands",
        "TodayEcommerce.Core.Application.Catalog.EventHandlers",
    ];

    [Fact]
    public void Catalog_Should_Not_Know_Orderings_Internals_Only_Its_Contracts()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespace("TodayEcommerce.Core.Domain.Catalog")
            .Or().ResideInNamespace("TodayEcommerce.Core.Application.Catalog")
            .ShouldNot().HaveDependencyOnAny(OrderingInternals)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Ordering_Should_Not_Change_Catalog_State_It_Asks_Through_Contracts()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespace("TodayEcommerce.Core.Domain.Ordering")
            .Or().ResideInNamespace("TodayEcommerce.Core.Application.Ordering")
            // Known, documented exception: placing an order READS the catalog (price and name snapshot,
            // early friendly stock check). It never changes a product. Next step for a full split:
            // a read contract published by the Catalog instead of its repository.
            .And().DoNotHaveName("CreateOrderCommandHandler")
            .ShouldNot().HaveDependencyOnAny(CatalogWriteAccess)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Contracts_Are_A_Published_Language_Primitives_Only_No_Domain_Types()
    {
        var result = Types.InAssemblies(Assemblies)
            .That().ResideInNamespaceEndingWith(".Contracts")
            .ShouldNot().HaveDependencyOn("TodayEcommerce.Core.Domain")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
