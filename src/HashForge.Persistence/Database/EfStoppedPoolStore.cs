using HashForge.Mining.Pools;
using Microsoft.EntityFrameworkCore;

namespace HashForge.Persistence.Database;

public sealed class EfStoppedPoolStore(IDbContextFactory<HashForgeDbContext> contextFactory) : IStoppedPoolStore
{
    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.StoppedPools.AsNoTracking().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task AddAsync(string name, CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            // Stopping a pool twice must not fail on the second attempt.
            if (await context.StoppedPools.AnyAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            context.StoppedPools.Add(new StoppedPoolEntity { Name = name });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(string name, CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            await context.StoppedPools.Where(p => p.Name == name).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
