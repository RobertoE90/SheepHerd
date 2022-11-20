using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public class InputSourceTagJob : SystemBase
{
    private bool _initialized = false;

    private RenderTexture _inputsIdTexture;
    private NativeArray<byte> _inputsIdTextureData;

    private uint2 _inputTexturesSize;
    private float2 _texturePhysicalRectSize;

    private EntityQuery _sheepsQuery;

    private void Initialize()
    {
        _sheepsQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(Translation),
            typeof(SheepComponentDataEntity)
        });

        //initialize texture components
        var query = GetEntityQuery(new ComponentType[] { typeof(PhysicalSizeTexture) });
        var textureEntities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in textureEntities)
        {
            var physicalSizeTextureComponent = EntityManager.GetSharedComponentData<PhysicalSizeTexture>(entity);
            if (physicalSizeTextureComponent.Type == TextureTypes.INPUT_BAKE_TEXTURE)
            {
                _inputsIdTexture = physicalSizeTextureComponent.TextureReference;
                _texturePhysicalRectSize = physicalSizeTextureComponent.PhysicalTextureSize;
                break;
            }
        }

        if (_inputsIdTexture == null)
            return;

        _inputTexturesSize = new uint2((uint)_inputsIdTexture.width, (uint)_inputsIdTexture.height);
        _inputsIdTextureData = new NativeArray<byte>(
            _inputsIdTexture.width * _inputsIdTexture.height * 4,
            Allocator.Persistent);

        RequestInputIdTextureDataCopy();

        _initialized = true;
    }

    private void RequestInputIdTextureDataCopy()
    {

        AsyncGPUReadback.Request(
            _inputsIdTexture,
            0,
            (req) =>
            {
                try
                {
                    if (!_inputsIdTexture)
                        return;

                    //an extra copy here due to unity bug
                    _inputsIdTextureData.Dispose();
                    _inputsIdTextureData = new NativeArray<byte>(req.GetData<byte>(), Allocator.Persistent);

                    //just repeat the callback on the next frame
                    RequestInputIdTextureDataCopy();
                }
                catch (ObjectDisposedException e)
                {
                    Debug.LogWarning($"The native array was disposed {e}");
                }

            });
    }


    protected override void OnUpdate()
    {
        if (!_initialized)
        {
            Initialize();
            return;
        }

        var job = new UpdateSheepInputIdJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<SheepComponentDataEntity>(),
            _inputsIdTextureData,
            _inputTexturesSize,
            _texturePhysicalRectSize);

        Dependency = job.ScheduleParallel(_sheepsQuery);
        Dependency.Complete();

    }

    protected override void OnDestroy()
    {
        _inputsIdTextureData.Dispose();
    }

    private struct UpdateSheepInputIdJob : IJobEntityBatch
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
                /*
                var idColor = new int4()
                {
                    x = _inputIdMap[idMapIndex],
                    y = _inputIdMap[idMapIndex + 1],
                    z = _inputIdMap[idMapIndex + 2],
                    w = _inputIdMap[idMapIndex + 3]
                };
                */
                var inputId = InputEntityManager.ColorCodeToIndex(_inputIdMap[idMapIndex + 2]); //blue channel for attract group id
                if (sheep.InputTargetIndex != inputId)
                {
                    sheep.InputTargetIndex = inputId;
                    sheeps[i] = sheep;
                }
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
}
