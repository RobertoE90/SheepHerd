using Unity.Entities;

public struct SheepFollowerComponent : IComponentData
{
    public int FollowIndex;
    public float DistanceToFollowed;
}