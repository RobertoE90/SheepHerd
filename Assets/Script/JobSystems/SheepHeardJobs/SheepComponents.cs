using Unity.Entities;

public struct SheepComponentDataEntity : IComponentData
{
    public int InputTargetIndex;
    public int UpdateGroupId;
    public float LastStateChangeTime;
    public int CurrentState;
    public int StateExtraInfo;
}