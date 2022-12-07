//#define DEBUG_RAYS

using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System;
using UnityEngine.Rendering;
using Unity.Burst;
using Unity.Rendering;

public class SheepHeardJobSystem : SystemBase
{
    private const float SHEEP_MOVEMENT_SPEED = 1f;
    private const float SHEEP_TURN_SPEED = 0.085f;
    private EntityQuery _sheepsQuery;

    private RenderTexture _bakedHeatTexture;
    private int2 _bakedHeatTextureSize;
    private float2 _mapsPhysicalSize;

    private int _codeIterator;
    private Entity _globalParamsEntity;
    private GlobalParams _globalParams;
    private Entity _inputAttractBufferEntity;
    
    private NativeArray<byte> _bakedHeatTextureData;

    //input 
    private bool _initialized = false;
    private RenderTexture _inputsTexture;
    private NativeArray<byte> _inputsTextureData;
    private int2 _inputTexturesSize;

    protected override void OnStartRunning()
    {
        _sheepsQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(Translation),
            typeof(SheepComponentDataEntity)
        });

        //initialize input entities
        var query = GetEntityQuery(new ComponentType[] { typeof(InputPoint), typeof(InputAttractTagComponent) });
        var entities = query.ToEntityArray(Allocator.Temp);
        _inputAttractBufferEntity = entities[0];
        entities.Dispose();
        

        _globalParamsEntity = GetSingletonEntity<GlobalParams>();
        _globalParams = EntityManager.GetComponentData<GlobalParams>(_globalParamsEntity);
        _codeIterator = 0;

    }

    private void InitializeDataTextures() {
        var query = GetEntityQuery(new ComponentType[] { typeof(PhysicalSizeTexture) });
        var textureEntities = query.ToEntityArray(Allocator.Temp);

        var inputPhysicalRectSize = new float2(float.MaxValue, float.MaxValue);

        foreach (var entity in textureEntities)
        {
            var physicalSizeTextureComponent = EntityManager.GetSharedComponentData<PhysicalSizeTexture>(entity);
            if(physicalSizeTextureComponent.Type == TextureTypes.MOVE_HEAT_TEXTURE && _bakedHeatTexture == null)
            {

                _bakedHeatTexture = physicalSizeTextureComponent.TextureReference;
                _mapsPhysicalSize = physicalSizeTextureComponent.PhysicalTextureSize;
                _bakedHeatTextureSize = new int2(_bakedHeatTexture.width, _bakedHeatTexture.height);
                _bakedHeatTextureData = new NativeArray<byte>(_bakedHeatTexture.width * _bakedHeatTexture.height * 4, Allocator.Persistent);
            }
            if(physicalSizeTextureComponent.Type == TextureTypes.INPUT_BAKE_TEXTURE && _inputsTexture == null)
            {
                _inputsTexture = physicalSizeTextureComponent.TextureReference;
                inputPhysicalRectSize = physicalSizeTextureComponent.PhysicalTextureSize;
                _inputTexturesSize = new int2(_inputsTexture.width, _inputsTexture.height);
                _inputsTextureData = new NativeArray<byte>(_inputsTexture.width * _inputsTexture.height * 4, Allocator.Persistent);
            }
        }
        
        textureEntities.Dispose();

        if (_inputsTexture == null || _bakedHeatTexture == null)
            return;

        if(!inputPhysicalRectSize.Equals(_mapsPhysicalSize))
        {
            Debug.LogWarning("Bake maps physical size should be the same");
            return;
        }

        RequestHeatTextureToArrayBake();
        _initialized = true;
    }

    private void RequestHeatTextureToArrayBake()
    {
        AsyncGPUReadback.Request(
            _bakedHeatTexture,
            0,
            (req) =>
            {
                try
                {
                    if (!_bakedHeatTexture)
                        return;

                    //an extra copy here due to unity bug
                    _bakedHeatTextureData.Dispose();
                    _bakedHeatTextureData = new NativeArray<byte>(req.GetData<byte>(), Allocator.Persistent);

                    //just repeat the callback on the next frame
                    RequestInputTextureDataCopy();
                }
                catch (ObjectDisposedException e)
                {
                    Debug.LogWarning($"The native array was disposed {e}");
                }

            });
    }


    private void RequestInputTextureDataCopy()
    {

        AsyncGPUReadback.Request(
            _inputsTexture,
            0,
            (req) =>
            {
                try
                {
                    if (!_inputsTexture)
                        return;

                    //an extra copy here due to unity bug
                    _inputsTextureData.Dispose();
                    _inputsTextureData = new NativeArray<byte>(req.GetData<byte>(), Allocator.Persistent);

                    //just repeat the callback on the next frame
                    RequestHeatTextureToArrayBake();
                }
                catch (ObjectDisposedException e)
                {
                    Debug.LogWarning($"The native array was disposed {e}");
                }

            });
    }


    protected override void OnDestroy()
    {
        _bakedHeatTextureData.Dispose();
    }

    protected override void OnUpdate()
    {
        if (!_initialized)
            InitializeDataTextures();

        var inputsBuffer = GetBuffer<InputPoint>(_inputAttractBufferEntity);
        var inputAttractArray = inputsBuffer.ToNativeArray(Allocator.TempJob);

        var randomValuesBuffer = GetBuffer<RandomData>(_globalParamsEntity);
        var randomArrays = randomValuesBuffer.ToNativeArray(Allocator.TempJob);
        
        UpdateMovementJob job = new UpdateMovementJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<Rotation>(),
            GetComponentTypeHandle<SheepComponentDataEntity>(),
            GetComponentTypeHandle<URPMaterialPropertyBaseColor>(),
            inputAttractArray,
            randomArrays,
            _bakedHeatTextureData,
            _bakedHeatTextureSize,
            _inputsTextureData,
            _inputTexturesSize,
            _mapsPhysicalSize,
            _globalParams.WorldScale,
            _codeIterator,
            Time.DeltaTime,
            (float)Time.ElapsedTime);

        _codeIterator++;
        var handle = job.ScheduleParallel(_sheepsQuery);

        var inputJob = new UpdateSheepInputIdJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<SheepComponentDataEntity>(),
            _inputsTextureData,
            _inputTexturesSize,
            _mapsPhysicalSize);

        Dependency = inputJob.ScheduleParallel(_sheepsQuery, 1, handle);
        Dependency.Complete();

        if (_codeIterator >= _globalParams.MaxGroups)
            _codeIterator = 0;

        inputAttractArray.Dispose();
        randomArrays.Dispose();
    }

    private struct UpdateMovementJob : IJobEntityBatch
    {
        private ComponentTypeHandle<Translation> _translationType;
        private ComponentTypeHandle<Rotation> _rotationType;
        private ComponentTypeHandle<SheepComponentDataEntity> _sheepType;
        private ComponentTypeHandle<URPMaterialPropertyBaseColor> _color;
        [ReadOnly] private NativeArray<InputPoint> _inputAttractArray;
        [ReadOnly] private NativeArray<RandomData> _randomDataArray;
        [ReadOnly] private NativeArray<byte> _heatMap;
        private int2 _heatMapDimensions;

        [ReadOnly] private NativeArray<byte> _inputRepulseMap;
        private int2 _inputRepulseMapDimensions;

        private float2 _physicalMapsSize;
        private float _worldScale;

        private int _codeIterator;

        private float _rotationStepSpread;

        private float _scaledDeltaTime;
        private float _executionTime;

        public UpdateMovementJob(
            ComponentTypeHandle<Translation> t,
            ComponentTypeHandle<Rotation> r,
            ComponentTypeHandle<SheepComponentDataEntity> sheepComponent,
            ComponentTypeHandle<URPMaterialPropertyBaseColor> color,
            NativeArray<InputPoint> inputAttractArray,
            NativeArray<RandomData> randomDataArray,
            NativeArray<byte> heatMap,
            int2 heatMapDimensions,
            NativeArray<byte> inputRepulseMap,
            int2 inputRepulseMapDimensions,
            float2 physicalMapsSize,
            float worldScale,
            int codeIterator,
            float deltaTime,
            float time)
        {
            _translationType = t;
            _rotationType = r;
            _sheepType = sheepComponent;
            _color = color;

            _inputAttractArray = inputAttractArray;
            _randomDataArray = randomDataArray;

            _heatMap = heatMap;
            _heatMapDimensions = heatMapDimensions;

            _inputRepulseMap = inputRepulseMap;
            _inputRepulseMapDimensions = inputRepulseMapDimensions;

            _physicalMapsSize = physicalMapsSize;
            _worldScale = worldScale;
            _codeIterator = codeIterator;

            _rotationStepSpread = math.PI * 0.1f;
            _scaledDeltaTime = deltaTime;
            _executionTime = time;
        }

        [BurstCompile]
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<Translation> translations = batchInChunk.GetNativeArray(this._translationType);
            NativeArray<Rotation> rotations = batchInChunk.GetNativeArray(this._rotationType);
            NativeArray<SheepComponentDataEntity> sheeps = batchInChunk.GetNativeArray(this._sheepType);
            NativeArray<URPMaterialPropertyBaseColor> colors = batchInChunk.GetNativeArray(this._color);


            for (int i = 0; i < batchInChunk.Count; ++i)
            {
                var translation = translations[i];
                var rotation = rotations[i];
                var sheep = sheeps[i];
                var color = colors[i];

                switch (sheep.CurrentState) {
                    case 0: //move to target
                        MoveToTarget(ref translation, rotation, ref sheep);
                        color.Value = new float4(0, 1, 0, 0);
                        break;
                    case 1: //move to trace
                        FollowOthersTrace(ref translation, rotation, ref sheep);
                        color.Value = new float4(0, 0.5f, 0.5f, 0);
                        break;
                    case 2: //move to less heat zone
                        color.Value = new float4(0, 0, 1, 0);
                        MoveToLessHeat(ref translation, rotation, ref sheep);
                        break;
                    case 3: //idle
                        color.Value = new float4(1, 0, 1, 0);
                        Idle(ref sheep, translation.Value);
                        break;
                    case 4: //run away
                        RepulseBehavior(ref translation, ref rotation, ref sheep);
                        color.Value = new float4(1, 0, 0, 0);
                        break;
                }

                CheckForRunAwayState(ref translation, ref sheep);
                
                //rotate to target
                var rotationTick = ComputeRotationTick(sheep.TargetRotation, rotation.Value);
                rotation.Value = math.mul(rotation.Value, quaternion.EulerXYZ(0, rotationTick, 0));
                rotations[i] = rotation;

                translations[i] = translation;
                sheeps[i] = sheep;
                colors[i] = color;
            }
        }


        private void Idle(ref SheepComponentDataEntity sheep, float3 pos)
        {
            if(sheep.StateExtraInfo == 0)
            {
                sheep.StateExtraInfo = (int)(GetRandomNormalizedValue(sheep.UpdateGroupId) * 10 + 7);
            }

            if(_executionTime - sheep.LastStateChangeTime > sheep.StateExtraInfo)
                ChangeToRandomState(ref sheep); //change to random state
        }

        private void MoveToTarget(ref Translation translation, Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var globalForward = new float3(0, 0, 1);
            var canMoveForward = CanMoveForward(translation, rotation, globalForward, out var localForward);
            
            if (canMoveForward)
            {
                translation.Value += localForward;
                sheep.StateExtraInfo = 0;
            }
            else
            {
                sheep.StateExtraInfo++;
                if (sheep.StateExtraInfo > 200) //no move max time
                {
                    if (GetRandomNormalizedValue(sheep.UpdateGroupId) > 0.6f)
                        ChangeState(2, ref sheep);
                    else
                        sheep.StateExtraInfo = 0;
                }
            }

            if (_codeIterator == sheep.UpdateGroupId)
            {
                
                var normalizedTargetDirection = math.normalizesafe(_inputAttractArray[sheep.InputAttrackIndex].LocalInputPosition - translation.Value.xz);
                var lookAtRotation = HorizontalLookAtRotation(normalizedTargetDirection);
                
                var positiveNormalizedSearchRot = math.mul(lookAtRotation, quaternion.EulerXYZ(0, _rotationStepSpread, 0));
                GetMapDeltaValue(
                    translation.Value,
                    math.mul(positiveNormalizedSearchRot, globalForward),
                    SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 60,
                    _heatMap,
                    _heatMapDimensions,
                    BakeChannelCode.RED, 
                    out var heatValuePositive);

                var negativeNormalizedSearchRot = math.mul(lookAtRotation, quaternion.EulerXYZ(0, _rotationStepSpread * -1, 0));
                GetMapDeltaValue(
                    translation.Value,
                    math.mul(negativeNormalizedSearchRot, globalForward),
                    SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 60,
                    _heatMap,
                    _heatMapDimensions,
                    BakeChannelCode.RED,
                    out var heatValueNegative);


                var targetRotation = lookAtRotation;

                if (heatValuePositive != heatValueNegative)
                {
                    targetRotation = (heatValuePositive > heatValueNegative) ?
                        negativeNormalizedSearchRot :
                        positiveNormalizedSearchRot;
                }

                sheep.TargetRotation = targetRotation;
                if (_executionTime - sheep.LastStateChangeTime > 150) {
                    var randomValue = GetRandomNormalizedValue(sheep.UpdateGroupId);
                    ChangeState(randomValue > 0.6f ? 1 : 0, ref sheep);
                }
            }
        }

        private void FollowOthersTrace(ref Translation translation, Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var globalForward = new float3(0, 0, 1);
            var canMoveForward = CanMoveForward(translation, rotation, globalForward, out var localForward);

            GetMapDeltaValue(
                translation.Value,
                math.mul(rotation.Value, globalForward),
                SHEEP_MOVEMENT_SPEED * 2,
                _heatMap,
                _heatMapDimensions,
                BakeChannelCode.BLUE,
                out var forwardValue);

            canMoveForward = canMoveForward && forwardValue > 10;

            if (canMoveForward)
            {
                translation.Value += localForward;
                sheep.StateExtraInfo = 0;
            }
            else
            {
                sheep.StateExtraInfo++;
                if (sheep.StateExtraInfo > 20) //no move max time
                {
                    if (GetRandomNormalizedValue(sheep.UpdateGroupId + 1) > 0.75f)
                        ChangeToRandomState(ref sheep); //less heat search state
                    else
                        sheep.StateExtraInfo = 0;
                }
            }

            if (_codeIterator == sheep.UpdateGroupId)
            {
                var dirSearchTick = 2;
                var maxValue = 0;
                quaternion targetRotation = quaternion.identity;
                for(var i = dirSearchTick * -1; i <= dirSearchTick; i++)
                {
                    var searchQuat = math.mul(rotation.Value, quaternion.EulerXYZ(0, _rotationStepSpread * i, 0));
                    GetMapDeltaValue(
                        translation.Value,
                        math.mul(searchQuat, globalForward),
                        SHEEP_MOVEMENT_SPEED * 5,
                        _heatMap,
                        _heatMapDimensions,
                        BakeChannelCode.BLUE,
                        out var traceSearchValue);
                    if(maxValue < traceSearchValue)
                    {
                        maxValue = traceSearchValue;
                        targetRotation = searchQuat;
                    }
                }

                if (maxValue < 45)
                {
                    ChangeToRandomState(ref sheep); //go to target or to less heat
                    return;
                }

                sheep.TargetRotation = targetRotation;
            }
        }

        private void MoveToLessHeat(ref Translation translation, Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var globalForward = new float3(0, 0, 1);
            
            var canMoveForward = CanMoveForward(translation, rotation, globalForward, out var localForward, 7.5f, 120);
            if (canMoveForward)
            {
                translation.Value += localForward;
                sheep.StateExtraInfo = 0;
            }


            if (_codeIterator == sheep.UpdateGroupId && !canMoveForward)
            {
                var negativeSideValue = 0;
                var positiveSideValue = 0;

                var minSideValue = 0;

                for (var j = 1; j <= 3; j++)
                {
                    for (var i = 1; i < 4; i++)
                    {
                        var searchRotation = math.mul(rotation.Value, quaternion.EulerXYZ(0, _rotationStepSpread * i * 2.5f, 0));
                        GetMapDeltaValue(
                            translation.Value,
                            math.mul(searchRotation, globalForward),
                            SHEEP_MOVEMENT_SPEED * j * 1.5f,
                            _heatMap,
                            _heatMapDimensions,
                            BakeChannelCode.RED,
                            out var heatValuePositive);
                        positiveSideValue += heatValuePositive;

                        searchRotation = math.mul(rotation.Value, quaternion.EulerXYZ(0, _rotationStepSpread * i * -2.5f, 0));
                        GetMapDeltaValue(
                            translation.Value,
                            math.mul(searchRotation, globalForward),
                            SHEEP_MOVEMENT_SPEED * j * 1.5f,
                            _heatMap,
                            _heatMapDimensions,
                            BakeChannelCode.RED,
                            out var heatValueNegative);
                        negativeSideValue += heatValueNegative;
                    }
                }

                if(negativeSideValue > positiveSideValue)
                {
                    minSideValue = positiveSideValue;
                    sheep.StateExtraInfo = 1;
                }
                else
                {
                    minSideValue = negativeSideValue;
                    sheep.StateExtraInfo = -1;
                }
                
                if (negativeSideValue == positiveSideValue)
                    sheep.StateExtraInfo = GetRandomNormalizedValue(sheep.UpdateGroupId) > 0.5f ? -1 : 1;
               
            }

            sheep.TargetRotation = math.mul(rotation.Value, quaternion.EulerXYZ(0, math.PI * sheep.StateExtraInfo * 0.15f, 0));

            if (_executionTime - sheep.LastStateChangeTime > (10 * GetRandomNormalizedValue(sheep.UpdateGroupId))) //state exit time
            {
                ChangeState(GetRandomNormalizedValue(sheep.UpdateGroupId + 1) > 0.6f ? 1 : 2, ref sheep); //change to follow trace or to same state
            }
        }

        private void RepulseBehavior(ref Translation translation, ref Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var globalForward = new float3(0, 0, 1);

            if (CanMoveForward(translation, rotation, globalForward, out var resultDeltaForward, ThresholdValue:240))
                translation.Value += resultDeltaForward * 2f;
            else
            {
                //search for clear path
                var minValue = float.MaxValue;
                var targetRotation = sheep.TargetRotation;
                for (var i = -1; i <= 1; i++)
                {
                    var searchRot = math.mul(sheep.TargetRotation, quaternion.Euler(0, _rotationStepSpread * 2, 0));
                    GetMapDeltaValue(
                        translation.Value,
                        math.mul(searchRot, globalForward),
                        SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 60,
                        _heatMap,
                        _heatMapDimensions,
                        BakeChannelCode.RED,
                        out var heatValue);

                    if (heatValue < minValue)
                    {
                        minValue = heatValue;
                        targetRotation = sheep.TargetRotation;
                    }
                }

                sheep.TargetRotation = targetRotation;
                rotation.Value = math.slerp(rotation.Value, sheep.TargetRotation, 0.15f);
            }
            
            if (_codeIterator == sheep.UpdateGroupId)
            {
                var checkSectorCount = 8f;
                var maxRepulseValue = sheep.StateExtraInfo;
                var tick = math.PI * 2 / checkSectorCount;
                var maxRepulseRot = quaternion.identity;
                var found = false;
                for (var i = 0; i < checkSectorCount; i++)
                {
                    var deltaRotation = math.mul(rotation.Value, quaternion.Euler(0, i * tick, 0));
                    GetMapDeltaValue(
                        translation.Value,
                        math.mul(deltaRotation, globalForward),
                        SHEEP_MOVEMENT_SPEED * 2f,
                        _inputRepulseMap,
                        _inputRepulseMapDimensions,
                        BakeChannelCode.RED,
                        out var value);

                    if (maxRepulseValue < value)
                    {
                        found = true;
                        maxRepulseValue = value;
                        maxRepulseRot = deltaRotation;
                    }
                }

                if (found)
                {
                    sheep.StateExtraInfo = maxRepulseValue;
                    sheep.TargetRotation = math.mul(maxRepulseRot, quaternion.Euler(0, math.PI, 0));
                }


                rotation.Value = math.slerp(rotation.Value, sheep.TargetRotation, 0.05f * _scaledDeltaTime);
                ChangeToRandomState(ref sheep); //change to follow trace or to same state
                
            }
        }


        private void CheckForRunAwayState(ref Translation translation, ref SheepComponentDataEntity sheep)
        {
            GetMapValue(translation.Value, _inputRepulseMap, _inputRepulseMapDimensions, BakeChannelCode.RED, out var value);
            if (value > 20)
                ChangeState(4, ref sheep);
        }

        private void ChangeToRandomState(ref SheepComponentDataEntity sheepDataComponent)
        {
            var randomState = (int)(GetRandomNormalizedValue(sheepDataComponent.UpdateGroupId) * 100) % 4;
            ChangeState(randomState, ref sheepDataComponent);
        }

        private void ChangeState(int newState, ref SheepComponentDataEntity sheepDataComponent)
        {
            if (newState < 0 || newState > 4)
                return;

            sheepDataComponent.CurrentState = newState;
            sheepDataComponent.LastStateChangeTime = _executionTime;
            sheepDataComponent.StateExtraInfo = 0;
        }

        private float ComputeRotationTick(quaternion targetRotation, quaternion originRotation)
        {
            var angle = GetSignedAngleWidthRotations(targetRotation, originRotation, out var sign);
            var stepAngle = SHEEP_TURN_SPEED * _scaledDeltaTime;

            if (angle > stepAngle)
                angle = stepAngle;
            return angle * sign;
        }

        private float GetSignedAngleWidthRotations(quaternion targetRotation, quaternion originRotation, out float sign)
        {
            var forward = new float3(0, 0, 1);
            var targetForward = math.mul(targetRotation, forward);
            var forwardDot = math.dot(
                math.mul(originRotation, forward),
                targetForward);

            sign = 0f;
            if (forwardDot > 0.99f)
                return 0f;

            var rightDot = math.dot(
                math.mul(originRotation, new float3(1, 0, 0)),
                targetForward);

            var nextRotationDirection = forwardDot * rightDot;
            if (forwardDot < 0)
                nextRotationDirection *= -1f;

            sign = math.sign(nextRotationDirection);
            var angle = (1f - forwardDot) * math.PI * 0.5f;
            if(forwardDot < 0)
                angle = (forwardDot * -1f * math.PI * 0.5f) + math.PI * 0.5f;
            
            return angle;
        }

        private bool GetMapDeltaValue(float3 originPosition, float3 deltaDirection, float deltaDistance, NativeArray<byte> map, int2 mapDimensions, BakeChannelCode channel, out byte result, bool debug = false)
        {
            var searchPosition = originPosition + (deltaDirection * deltaDistance * _worldScale);
            if (debug)
                Debug.DrawLine(originPosition, searchPosition, Color.green);
            return GetMapValue(searchPosition, map, mapDimensions, channel, out result);
        }

        private bool GetMapValue(float3 position, NativeArray<byte> map, int2 mapDimensions, BakeChannelCode channel, out byte result)
        {

            var mapIndex = LocalPositionToMapIndex(position, _physicalMapsSize, mapDimensions);
            if (mapIndex != -1)
            {
                result = map[mapIndex + (int)channel];
                return true;
            }
            result = byte.MaxValue;
            return false;
        }

        private int LocalPositionToMapIndex(float3 position, float2 physicalRectSize, int2 heatMapSize)
        {
            var searchPosition = (position.xz + physicalRectSize * 0.5f);

            var physicalToImageScale = heatMapSize.x / physicalRectSize.x;
            searchPosition *= physicalToImageScale;
            int2 texturePos = new int2((int)searchPosition.x, (int)searchPosition.y);
            
            if (texturePos.x < 0 ||
                texturePos.y < 0 ||
                texturePos.x >= heatMapSize.x ||
                texturePos.y >= heatMapSize.y)
                return -1;

            var index = (texturePos.y * heatMapSize.x + texturePos.x) * 4;
            if (index >= _heatMap.Length)
                return -1;

            return index;
        }

        private bool CanMoveForward(Translation translation, Rotation rotation, float3 globalForward, out float3 localForward, float forwardSearchScale = 5, int ThresholdValue = 200)
        {
            var normalizedLocalForward = math.mul(rotation.Value, globalForward);
            localForward = normalizedLocalForward * SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * _worldScale;
            var localForwardHeatMapIndex = LocalPositionToMapIndex(translation.Value + localForward * SHEEP_MOVEMENT_SPEED * forwardSearchScale, _physicalMapsSize, _heatMapDimensions);
            //Debug.Log(_heatMap[localForwardHeatMapIndex]);
            return (localForwardHeatMapIndex != -1 && _heatMap[localForwardHeatMapIndex] < ThresholdValue);
        }

        private quaternion HorizontalLookAtRotation(float2 normalizedDirection)
        {
            var dot = Vector2.Dot(Vector2.up, normalizedDirection);
            dot = (dot * -1f + 1f) * 0.5f;
            var radDotValue = dot * math.PI;
            if (normalizedDirection.x < 0f)
                radDotValue = (dot * -1 + 2) * math.PI;

            radDotValue *= 0.5f; //this is not equivalent to the rotation in angles

            var cr = math.cos(radDotValue);
            var sr = math.sin(radDotValue);

            return new quaternion(0, sr, 0, cr);
        }

        private float GetRandomNormalizedValue(int seed)
        {
            return _randomDataArray[(seed % _randomDataArray.Length)].Value;
        }

        private enum BakeChannelCode
        {
            RED = 0,
            GREEN = 1,
            BLUE = 2
        }
    }
}
