namespace TechBazar.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}

public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}

/// <summary>Base for all business entities: int key, audit fields (UTC) and soft delete.</summary>
public abstract class BaseEntity : IAuditable, ISoftDeletable
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Optimistic-concurrency counter. The DbContext increments it on every update and EF adds it to the WHERE clause,
/// so two concurrent writers (last item in stock, last coupon use, double payment callback) cannot both win.
/// Provider-agnostic (an int, not a SQL Server rowversion).
/// </summary>
public interface IVersioned
{
    int Version { get; set; }
}
