using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Infrastructure.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PersistenceTests
{
    private readonly PostgresFixture _db;

    public PersistenceTests(PostgresFixture db)
    {
        _db = db;
    }

    private async Task<ProductId> SeedProductAsync(int stock)
    {
        var product = Product.Create("Telefon X", Money.Create(10_000m, "TRY"), stock);
        await using var db = _db.CreateDbContext();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    [Fact]
    public async Task AyseAndMehmet_BuyTheLastUnitAtTheSameTime_SecondSaveIsRejected()
    {
        var productId = await SeedProductAsync(stock: 1);

        // Two independent requests read the same row — both see stock = 1.
        await using var ayseRequest = _db.CreateDbContext();
        await using var mehmetRequest = _db.CreateDbContext();
        var ayseCopy = await ayseRequest.Products.SingleAsync(p => p.Id == productId);
        var mehmetCopy = await mehmetRequest.Products.SingleAsync(p => p.Id == productId);

        // Both pass the domain rule on their own (stale) copy.
        ayseCopy.RemoveStock(Quantity.Create(1));
        mehmetCopy.RemoveStock(Quantity.Create(1));

        // Ayşe saves first and wins; Mehmet's UPDATE ... WHERE xmin = <old> matches 0 rows.
        await ayseRequest.SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(() => mehmetRequest.SaveChangesAsync());

        // Only one unit was sold.
        await using var check = _db.CreateDbContext();
        var stored = await check.Products.SingleAsync(p => p.Id == productId);
        Assert.Equal(0, stored.StockQuantity);
    }

    [Fact]
    public async Task Retry_AfterConflict_SeesFreshDataAndDomainRuleRejectsTheSale()
    {
        var productId = await SeedProductAsync(stock: 1);

        await using (var ayse = _db.CreateDbContext())
        {
            (await ayse.Products.SingleAsync(p => p.Id == productId)).RemoveStock(Quantity.Create(1));
            await ayse.SaveChangesAsync();
        }

        // Mehmet's retry reads the current row (stock 0) — now the domain rule itself says no.
        await using var mehmetRetry = _db.CreateDbContext();
        var fresh = await mehmetRetry.Products.SingleAsync(p => p.Id == productId);

        Assert.False(fresh.HasStockFor(Quantity.Create(1)));
    }

    [Fact]
    public async Task Order_RoundTrip_PreservesAggregateWithItemsAndValueObjects()
    {
        var phone = Product.Create("Telefon X", Money.Create(10_000m, "TRY"), 5);
        var order = Order.Create(new CustomerId(Guid.NewGuid()), "TRY", DateTime.UtcNow);
        order.AddItem(phone.Id, phone.Name, phone.Price, Quantity.Create(2));
        order.Place(DateTime.UtcNow);

        await using (var write = _db.CreateDbContext())
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        await using var read = _db.CreateDbContext();
        var loaded = await read.Orders.SingleAsync(o => o.Id == order.Id);

        Assert.Equal(OrderStatus.Placed, loaded.Status);
        var line = Assert.Single(loaded.Items);
        Assert.Equal(Money.Create(10_000m, "TRY"), line.UnitPrice);
        Assert.Equal(Quantity.Create(2), line.Quantity);
        Assert.Equal(Money.Create(20_000m, "TRY"), loaded.Total);
    }

    [Fact]
    public async Task Database_CheckConstraint_RejectsNegativeStock_EvenWhenDomainIsBypassed()
    {
        var productId = await SeedProductAsync(stock: 1);
        await using var db = _db.CreateDbContext();

        // Raw SQL skips every C# rule — the database is the last line of defence.
        var act = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE products SET stock_quantity = -5 WHERE id = {productId.Value}");

        await Assert.ThrowsAnyAsync<Exception>(act);
    }
}
