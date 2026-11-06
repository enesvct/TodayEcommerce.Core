using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Ordering;

public class CreateOrderTests
{
    private readonly ApplicationTestHost _host = new();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private Product SeedProduct(string name = "Phone", decimal price = 1_000m, int stock = 10)
    {
        var product = Product.Create(name, Money.Create(price, "TRY"), stock);
        _host.Products.Seed(product);
        return product;
    }

    [Fact]
    public async Task EmptyItems_IsRejectedByValidation_BeforeTouchingTheDatabase()
    {
        var command = new CreateOrderCommand(CustomerId, Array.Empty<CreateOrderItem>());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _host.Mediator.Send(command));

        Assert.Contains(nameof(CreateOrderCommand.Items), ex.Errors.Keys);
        Assert.Equal(0, _host.Products.QueryCount); // handler never ran → no wasted query
        Assert.Equal(0, _host.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task MultipleProblems_AreAllReportedAtOnce()
    {
        var command = new CreateOrderCommand(
            Guid.Empty,
            new[] { new CreateOrderItem(Guid.Empty, 0) });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _host.Mediator.Send(command));

        Assert.Contains("CustomerId", ex.Errors.Keys);
        Assert.Contains("Items[0].ProductId", ex.Errors.Keys);
        Assert.Contains("Items[0].Quantity", ex.Errors.Keys);
    }

    [Fact]
    public async Task ValidRequest_PlacesOrder_WithCatalogPriceSnapshot_AndSavesOnce()
    {
        var phone = SeedProduct("Phone", 1_000m);
        var @case = SeedProduct("Case", 50m);
        var command = new CreateOrderCommand(CustomerId, new[]
        {
            new CreateOrderItem(phone.Id.Value, 2),
            new CreateOrderItem(@case.Id.Value, 1),
        });

        var orderId = await _host.Mediator.Send(command);

        var order = Assert.Single(_host.Orders.Added);
        Assert.Equal(orderId, order.Id.Value);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(Money.Create(2_050m, "TRY"), order.Total);
        Assert.Equal(_host.Clock.UtcNow, order.PlacedAtUtc);
        Assert.Equal(1, _host.Products.QueryCount); // all products loaded in ONE query (no N+1)
        Assert.Equal(1, _host.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task UnknownProduct_ThrowsNotFound_AndSavesNothing()
    {
        var command = new CreateOrderCommand(CustomerId, new[] { new CreateOrderItem(Guid.NewGuid(), 1) });

        await Assert.ThrowsAsync<NotFoundException>(() => _host.Mediator.Send(command));

        Assert.Empty(_host.Orders.Added);
        Assert.Equal(0, _host.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task InsufficientStock_ThrowsDomainException_AndSavesNothing()
    {
        var phone = SeedProduct(stock: 1);
        var command = new CreateOrderCommand(CustomerId, new[] { new CreateOrderItem(phone.Id.Value, 2) });

        await Assert.ThrowsAsync<DomainException>(() => _host.Mediator.Send(command));

        Assert.Empty(_host.Orders.Added);
        Assert.Equal(0, _host.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task PlacingOrder_DoesNotDecreaseStock_Yet()
    {
        // Documents the current design: stock is decreased asynchronously in Step 5 (Outbox).
        var phone = SeedProduct(stock: 10);

        await _host.Mediator.Send(new CreateOrderCommand(CustomerId, new[] { new CreateOrderItem(phone.Id.Value, 3) }));

        Assert.Equal(10, phone.StockQuantity);
    }
}
