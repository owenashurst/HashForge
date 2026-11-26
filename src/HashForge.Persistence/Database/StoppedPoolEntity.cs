namespace HashForge.Persistence.Database;

/// <summary>A pool an administrator stopped. Its presence is the whole record: the pool is not started while it exists.</summary>
public sealed class StoppedPoolEntity
{
    public required string Name { get; set; }
}
