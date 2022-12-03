using Unity.Entities;
using Unity.Mathematics;

public struct SheepComponentDataEntity : IComponentData
{
    public int InputAttrackIndex;
    public quaternion TargetRotation;
    public float InputRepulseStrength;
    public int UpdateGroupId;
    public float LastStateChangeTime;
    public int CurrentState;
    public int StateExtraInfo;
}