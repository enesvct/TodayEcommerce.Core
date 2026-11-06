using TodayEcommerce.Core.Application.Abstractions;

namespace TodayEcommerce.Core.Infrastructure.Services;

internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
