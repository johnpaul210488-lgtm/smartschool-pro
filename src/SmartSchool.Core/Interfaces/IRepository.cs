using System.Linq.Expressions;

namespace SmartSchool.Core.Interfaces;

/// <summary>
/// Minimal generic repository. Query() exposes IQueryable so callers keep full
/// LINQ/EF power (Include, Select, paging) without duplicating wrapper methods.
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    IQueryable<TEntity> Query(bool asNoTracking = true);

    Task<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);

    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);

    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default);

    Task AddAsync(TEntity entity, CancellationToken ct = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}
