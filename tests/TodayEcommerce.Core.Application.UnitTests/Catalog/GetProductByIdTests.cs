using TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Catalog;

public class GetProductByIdTests
{
    private readonly ApplicationTestHost _host = new();

    [Fact]
    public async Task ExistingProduct_ReturnsFlatDto()
    {
        var product = Product.Create("Phone", Money.Create(1_000m, "TRY"), 0);
        _host.Products.Seed(product);

        var dto = await _host.Mediator.Send(new GetProductByIdQuery(product.Id.Value));

        Assert.NotNull(dto);
        Assert.Equal(product.Id.Value, dto.Id);
        Assert.Equal(1_000m, dto.Price);
        Assert.Equal("TRY", dto.Currency);
        Assert.False(dto.InStock);
    }

    [Fact]
    public async Task UnknownProduct_ReturnsNull()
    {
        var dto = await _host.Mediator.Send(new GetProductByIdQuery(Guid.NewGuid()));

        Assert.Null(dto);
    }

    [Fact]
    public async Task EmptyId_IsRejectedByValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _host.Mediator.Send(new GetProductByIdQuery(Guid.Empty)));

        Assert.Equal(0, _host.Products.QueryCount);
    }
}
