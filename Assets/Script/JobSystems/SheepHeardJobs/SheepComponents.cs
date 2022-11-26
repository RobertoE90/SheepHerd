using Unity.Entities;

public struct SheepComponentDataEntity : IComponentData
{
    public int InputAttrackIndex;
    public int InputRepulseIndex;
    public float InputRepulseStrength;
    public int UpdateGroupId;
    public float LastStateChangeTime;
    public int CurrentState;
    public int StateExtraInfo;
}