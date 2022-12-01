using Unity.Entities;

public struct GlobalParams : IComponentData
{
    public int MaxGroups;
    public float WorldScale;
}

[InternalBufferCapacity(10)]
public struct RandomData : IBufferElementData
{
    public float Value;
}