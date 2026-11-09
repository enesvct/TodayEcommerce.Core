using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.Ordering.Queries.GetOrderById;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Ordering;

public class GetOrderByIdTests
{
    private readonly ApplicationTestHost _host = new();
    private readonly Guid _ayse = Guid.NewGuid();
    private readonly Guid _mehmet = Guid.NewGuid();

    private async Task<Guid> AyseOrdersAsync()
    {
        var phone = Product.Create("Telefon", Money.Create(1_000m, "TRY"), 5);
        _host.Products.Seed(phone);
        _host.CurrentUser.UserId = _ayse;
        return await _host.Mediator.Send(new CreateOrderCommand(new[] { new CreateOrderItem(phone.Id.Value, 1) }));
    }

    [Fact]
    public async Task Owner_CanReadTheirOrder()
    {
        var orderId = await AyseOrdersAsync();

        var order = await _host.Mediator.Send(new GetOrderByIdQuery(orderId));

        Assert.NotNull(order);
        Assert.Equal(_ayse, order.CustomerId);
    }

    [Fact]
    public async Task SomeoneElsesOrder_LooksExactlyLikeAMissingOne()
    {
        var orderId = await AyseOrdersAsync();
        _host.CurrentUser.UserId = _mehmet;

        var someoneElses = await _host.Mediator.Send(new GetOrderByIdQuery(orderId));
        var missing = await _host.Mediator.Send(new GetOrderByIdQuery(Guid.NewGuid()));

        // Both null → both become 404: Mehmet cannot even learn that Ayşe's order id exists.
        Assert.Null(someoneElses);
        Assert.Null(missing);
    }
}
