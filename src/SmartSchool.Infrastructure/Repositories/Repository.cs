using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Core.Interfaces;
using SmartSchool.Infrastructure.Data;

namespace SmartSchool.Infrastructure.Repositories;

public class Repository<TEntity> : IRepository<TEntity> where TEntity : class
{
    private readonly SchoolDbContext _context;
    private readonly DbSet<TEntity> _set;

    public Repository(SchoolDbContext context)
    {
        _context = context;
        _set = context.Set<TEntity>();
    }

    public IQueryable<TEntity> Query(bool asNoTracking = true)
        => asNoTracking ? _set.AsNoTracking() : _set;

    public async Task<TEntity?> FindAsync(object[] keys, CancellationToken ct = default)
        => await _set.FindAsync(keys, ct);

    public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => _set.FirstOrDefaultAsync(predicate, ct);

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => _set.AnyAsync(predicate, ct);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => _set.CountAsync(predicate, ct);

    public async Task AddAsync(TEntity entity, CancellationToken ct = default)
        => await _set.AddAsync(entity, ct);

    public void Update(TEntity entity) => _set.Update(entity);

    public void Remove(TEntity entity) => _set.Remove(entity);
}
