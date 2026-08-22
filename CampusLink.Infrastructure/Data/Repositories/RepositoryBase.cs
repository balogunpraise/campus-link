using System.Linq.Expressions;
using CampusLink.Core.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data.Repositories;

// Shared EF Core implementation. Every read takes a trackChanges flag:
// pass false for anything you only display (no change-tracker overhead), true
// when you intend to mutate the entity and call SaveAsync.
public abstract class RepositoryBase<T>(AppDbContext context) : IRepositoryBase<T> where T : class
{
    protected AppDbContext Context { get; } = context;

    protected DbSet<T> Entities => Context.Set<T>();

    public IQueryable<T> FindAll(bool trackChanges) =>
        trackChanges ? Entities : Entities.AsNoTracking();

    public IQueryable<T> FindByCondition(Expression<Func<T, bool>> expression, bool trackChanges) =>
        FindAll(trackChanges).Where(expression);

    public virtual Task<T> FindByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken = default) =>
        // Entities all key on Id via BaseEntity; EF resolves the property by
        // name so this works for the Identity-derived types too.
        FindAll(trackChanges)
            .FirstOrDefaultAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);

    public Task<bool> ExistsAsync(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default) =>
        Entities.AsNoTracking().AnyAsync(expression, cancellationToken);

    public async Task CreateAsync(T entity, CancellationToken cancellationToken = default) =>
        await Entities.AddAsync(entity, cancellationToken);

    public void Update(T entity) => Entities.Update(entity);

    public void Delete(T entity) => Entities.Remove(entity);
}
