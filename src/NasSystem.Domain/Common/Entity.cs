// =============================================================================
// NasSystem.Domain - Entity.cs
// 领域实体基类
// =============================================================================

namespace NasSystem.Domain.Common;

/// <summary>
/// 领域实体基类
/// </summary>
/// <typeparam name="TId">主键类型</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>
    /// 实体唯一标识
    /// </summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>
    /// 创建时间 (UTC)
    /// </summary>
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后修改时间 (UTC)
    /// </summary>
    public DateTime? UpdatedAt { get; protected set; }

    /// <summary>
    /// 领域事件集合
    /// </summary>
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// 只读领域事件集合
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// 添加领域事件
    /// </summary>
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// 移除领域事件
    /// </summary>
    public void RemoveDomainEvent(IDomainEvent domainEvent) => _domainEvents.Remove(domainEvent);

    /// <summary>
    /// 清空领域事件
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// 标记为已修改
    /// </summary>
    protected void MarkAsModified() => UpdatedAt = DateTime.UtcNow;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}

/// <summary>
/// 使用 Guid 作为主键的实体基类
/// </summary>
public abstract class Entity : Entity<Guid>
{
    protected Entity() => Id = Guid.NewGuid();
}
