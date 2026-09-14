namespace Meta.Dow.DataPermission;

/// <summary>
/// 实体创建人，用于 Self 数据范围。
/// </summary>
public interface IHasCreatorId
{
    Guid? CreatorId { get; }
}
