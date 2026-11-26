namespace HashForge.Mining.Pools;

/// <summary>Remembers which pools an administrator stopped, so they stay stopped when the service restarts.</summary>
public interface IStoppedPoolStore
{
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(string name, CancellationToken cancellationToken);

    Task RemoveAsync(string name, CancellationToken cancellationToken);
}
