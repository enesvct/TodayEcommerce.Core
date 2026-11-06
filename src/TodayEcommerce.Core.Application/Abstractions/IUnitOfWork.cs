namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// Commits every change tracked during a use-case in a single transaction:
/// either all of it is saved, or none of it. Implemented by the EF Core DbContext in Infrastructure.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
