using System.Linq.Expressions;
using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

// Generic data-access contract shared by every repository.
//
// Reads return IQueryable so callers can compose (filter, page, project)
// without the repository needing an overload per screen; writes only stage
// changes — nothing hits the database until IRepositoryManager.SaveAsync,
// so a single request can span several repositories in one transaction.
public interface IRepositoryBase<T> where T : class
{
    IQueryable<T> FindAll(bool trackChanges);

    IQueryable<T> FindByCondition(Expression<Func<T, bool>> expression, bool trackChanges);

    Task<T> FindByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default);

    Task CreateAsync(T entity, CancellationToken cancellationToken = default);

    void Update(T entity);

    void Delete(T entity);
}
