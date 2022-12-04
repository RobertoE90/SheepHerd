using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public struct UpdateSheepInputIdJob : IJobEntityBatch
{
    public ComponentTypeHandle<Translation> _translationType;
    public ComponentTypeHandle<SheepComponentDataEntity> _sheepType;
    [ReadOnly] private NativeArray<byte> _inputIdMap;

    private uint2 _inputIdMapSize;
    private float2 _physicalRectSize;

    public UpdateSheepInputIdJob(
        ComponentTypeHandle<Translation> t,
        ComponentTypeHandle<SheepComponentDataEntity> sheepComponent,
        NativeArray<byte> idMap,
        uint2 idMapSize,
        float2 physicalRectSize)
    {
        _translationType = t;
        _sheepType = sheepComponent;

        _inputIdMap = idMap;
        _inputIdMapSize = idMapSize;
        _physicalRectSize = physicalRectSize;
    }

    [BurstCompile]
    public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
    {
        NativeArray<Translation> translations = batchInChunk.GetNativeArray(this._translationType);
        NativeArray<SheepComponentDataEntity> sheeps = batchInChunk.GetNativeArray(this._sheepType);


        for (int i = 0; i < batchInChunk.Count; ++i)
        {
            var sheep = sheeps[i];
            var idMapIndex = EntityPositionToIdMapIndex(translations[i].Value);

            bool sheepInfoChanged = false;
            var inputAttrackId = InputEntityManager.ColorCodeToIndex(_inputIdMap[idMapIndex + 2]); //blue channel for attract group id
            if (sheep.InputAttrackIndex != inputAttrackId)
            {
                sheep.InputAttrackIndex = inputAttrackId;
                sheepInfoChanged = true;
            }

            var inputRepulseStrength = _inputIdMap[idMapIndex] / 255f; //red channel for repulse strenght
            if (inputRepulseStrength > 0.05) //apply repulse if is over threshold
            {
                if (math.abs(inputRepulseStrength - sheep.InputRepulseStrength) > 0.1f)
                {
                    sheep.InputRepulseStrength = inputRepulseStrength;
                    sheepInfoChanged = true;
                }
            }

            if (sheepInfoChanged)
                sheeps[i] = sheep;
        }
    }

    private int EntityPositionToIdMapIndex(float3 position)
    {
        var positionValue = position.xz + _physicalRectSize * 0.5f;
        positionValue.x /= _physicalRectSize.x;
        positionValue.y /= _physicalRectSize.y;

        var texturePos = new uint2(
            (uint)(positionValue.x * _inputIdMapSize.x),
            (uint)(positionValue.y * _inputIdMapSize.y));

        var result = (int)(texturePos.x + texturePos.y * _inputIdMapSize.x) * 4;
        result = math.clamp(result, 0, _inputIdMap.Length - 1);
        return result;
    }
}
