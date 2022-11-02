//#define DEBUG_RAYS

using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using System;
using UnityEngine.Rendering;
using Unity.Burst;

public class SheepHeardJobSystem : SystemBase
{
    private const float SHEEP_MOVEMENT_SPEED = 1f;
    private const float SHEEP_TURN_SPEED = 1f;
    private EntityQuery _sheepsQuery;

    private RenderTexture _heatBakeTexture;
    private int2 _bakeTextureSize;
    private float2 _movementHeatRectSize;

    private int _codeIterator;
    private Entity _globalParamsEntity;
    private GlobalParams _globalParams;
    private Entity _inputBufferEntity;
    
    private NativeArray<byte> _bakedTextureData;

    protected override void OnStartRunning()
    {
        _sheepsQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(Translation),
            typeof(SheepComponentDataEntity)
        });

        _inputBufferEntity = GetSingletonEntity<InputPoint>();
        _globalParamsEntity = GetSingletonEntity<GlobalParams>();
        _globalParams = EntityManager.GetComponentData<GlobalParams>(_globalParamsEntity);
        _codeIterator = 0;

        var heatMapEntity = GetSingletonEntity<HeatMapSharedComponentData>();
        var heatMapData = EntityManager.GetSharedComponentData<HeatMapSharedComponentData>(heatMapEntity);
        _heatBakeTexture = heatMapData.HeatTexture;
        _movementHeatRectSize = heatMapData.PhysicalRectSize;
        _bakeTextureSize = new int2(_heatBakeTexture.width, _heatBakeTexture.height);
        _bakedTextureData = new NativeArray<byte>(_heatBakeTexture.width * _heatBakeTexture.height, Allocator.Persistent);
        
        RequestTextureToArrayBake();
    }

    private void RequestTextureToArrayBake()
    {
        
        AsyncGPUReadback.Request(
            _heatBakeTexture,
            0,
            (req) =>
            {
                try
                {
                    if (!_heatBakeTexture)
                        return;

                    //an extra copy here due to unity bug
                    _bakedTextureData.Dispose();
                    _bakedTextureData = new NativeArray<byte>(req.GetData<byte>(), Allocator.Persistent);
                    
                    //just repeat the callback on the next frame
                    RequestTextureToArrayBake();
                }
                catch (ObjectDisposedException e)
                {
                    Debug.LogWarning($"The native array was disposed {e}");
                }

            });
    }

    protected override void OnDestroy()
    {
        _bakedTextureData.Dispose();
    }

    protected override void OnUpdate()
    {
        var inputsBuffer = GetBuffer<InputPoint>(_inputBufferEntity);
        var inputsArray = inputsBuffer.ToNativeArray(Allocator.TempJob);

        var randomValuesBuffer = GetBuffer<RandomData>(_globalParamsEntity);
        var randomArrays = randomValuesBuffer.ToNativeArray(Allocator.TempJob);

        UpdateMovementJob job = new UpdateMovementJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<Rotation>(),
            GetComponentTypeHandle<SheepComponentDataEntity>(),
            inputsArray,
            randomArrays,
            _bakedTextureData,
            _bakeTextureSize,
            _movementHeatRectSize,
            _codeIterator,
            Time.DeltaTime,
            (float)Time.ElapsedTime);

        _codeIterator++;
        Dependency = job.ScheduleParallel(_sheepsQuery);
        Dependency.Complete();

        if (_codeIterator >= _globalParams.MaxGroups)
            _codeIterator = 0;

        inputsArray.Dispose();
        randomArrays.Dispose();
    }

    private struct UpdateMovementJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Translation> _translationType;
        public ComponentTypeHandle<Rotation> _rotationType;
        public ComponentTypeHandle<SheepComponentDataEntity> _sheepType;
        [ReadOnly] private NativeArray<InputPoint> _inputsArray;
        [ReadOnly] private NativeArray<RandomData> _randomDataArray;
        [ReadOnly] private NativeArray<byte> _heatMap;
        private int2 _heatMapSize;
        
        private float2 _physicalRectSize;

        private int _codeIterator;

        private int _rotationSearchSteps;
        private float _rotationStepSpread;

        private float _scaledDeltaTime;
        private float _executionTime;

        public UpdateMovementJob(
            ComponentTypeHandle<Translation> t,
            ComponentTypeHandle<Rotation> r,
            ComponentTypeHandle<SheepComponentDataEntity> sheepComponent,
            NativeArray<InputPoint> inputsArray,
            NativeArray<RandomData> randomDataArray,
            NativeArray<byte> heatMap,
            int2 heatMapSize,
            float2 physicalRectSize,
            int codeIterator,
            float deltaTime,
            float time)
        {
            _translationType = t;
            _rotationType = r;
            _sheepType = sheepComponent;

            _inputsArray = inputsArray;
            _randomDataArray = randomDataArray;

            _heatMap = heatMap;
            _heatMapSize = heatMapSize;
            _physicalRectSize = physicalRectSize;

            _codeIterator = codeIterator;

            _rotationSearchSteps = 4;
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


            for (int i = 0; i < batchInChunk.Count; ++i)
            {
                var translation = translations[i];
                var rotation = rotations[i];
                var sheep = sheeps[i];

                switch (sheep.CurrentState) {
                    case 0: //move to target
                        MoveToTarget(ref translation, ref rotation, ref sheep);
                        break;
                    case 1: //move away from danger zone
                        break;
                    case 2: //move to less heat zone
                        MoveToLessHeat(ref translation, ref rotation, ref sheep);
                        break;
                    case 3: //idle
                        Idle(ref sheep, translation.Value);
                        break;
                }

                MoveAwayFromPoint(ref translation, ref rotation, ref sheep);

                translations[i] = translation;
                rotations[i] = rotation;
                sheeps[i] = sheep;
            }
        }

        private void Idle(ref SheepComponentDataEntity sheep, float3 pos)
        {
            if(sheep.StateExtraInfo == 0)
            {
                sheep.StateExtraInfo = (int)(GetRandomNormalizedValue(sheep.UpdateGroupId) * 10 + 7);
            }

            if(_executionTime - sheep.LastStateChangeTime > sheep.StateExtraInfo)
                ChangeState(2, ref sheep); //less heat search state
        }

        private void MoveToTarget(ref Translation translation, ref Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var globalForward = new float3(0, 0, 1);
            var normalizedLocalForward = math.mul(rotation.Value, globalForward);
            var localForward = normalizedLocalForward * SHEEP_MOVEMENT_SPEED * _scaledDeltaTime;
            var localForwardHeatMapIndex = LocalPositionToHeatMapIndex(translation.Value + localForward * SHEEP_MOVEMENT_SPEED * 4, _physicalRectSize, _heatMapSize);

            if (localForwardHeatMapIndex != -1 && _heatMap[localForwardHeatMapIndex] < 170)
            {
                translation.Value += localForward;
                sheep.StateExtraInfo = 0;
            }
            else
            {
                sheep.StateExtraInfo++;
                if (sheep.StateExtraInfo > 40) //no move max time
                {
                    if(GetRandomNormalizedValue(sheep.UpdateGroupId) > 0.6f)
                        ChangeState(2, ref sheep); //less heat search state
                    else
                        sheep.StateExtraInfo = 0;
                }
            }

            if (_codeIterator == sheep.UpdateGroupId)
            {
                var normalizedTargetDirection = math.normalizesafe(_inputsArray[sheep.InputTargetIndex].LocalInputPosition - translation.Value.xz);
                var lookAtRotation = HorizontalLookAtRotation(normalizedTargetDirection);
                
                var positiveNormalizedSearchRot = math.mul(lookAtRotation, quaternion.EulerXYZ(0, _rotationStepSpread, 0));
                GetDeltaHeatAtRotation(
                    translation.Value,
                    math.mul(positiveNormalizedSearchRot, globalForward),
                    SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 3,
                    out var heatValuePositive);

                var negativeNormalizedSearchRot = math.mul(lookAtRotation, quaternion.EulerXYZ(0, _rotationStepSpread * -1, 0));
                GetDeltaHeatAtRotation(
                    translation.Value,
                    math.mul(negativeNormalizedSearchRot, globalForward),
                    SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 3,
                    out var heatValueNegative);


                var targetRotation = lookAtRotation;

                if (heatValuePositive != heatValueNegative)
                {
                    targetRotation = (heatValuePositive > heatValueNegative) ?
                        negativeNormalizedSearchRot :
                        positiveNormalizedSearchRot;
                }
                
                var angle = GetSignedAngleWidthRotations(targetRotation, rotation.Value, out var sign);
                var stepAngle = SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 5f;
                if (angle > stepAngle)
                    angle = stepAngle;
                rotation.Value = math.mul(rotation.Value, quaternion.EulerXYZ(0, sign * angle, 0));

                if (_executionTime - sheep.LastStateChangeTime > 40 && GetRandomNormalizedValue(sheep.UpdateGroupId) > 0.85f)
                    ChangeState(3, ref sheep); //go to idle
            }
        }

        private void MoveToLessHeat(ref Translation translation, ref Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var globalForward = new float3(0, 0, 1);
            var normalizedLocalForward = math.mul(rotation.Value, globalForward);
            var localForward = normalizedLocalForward * SHEEP_MOVEMENT_SPEED * _scaledDeltaTime;
            var localForwardHeatMapIndex = LocalPositionToHeatMapIndex(translation.Value + localForward * SHEEP_MOVEMENT_SPEED * 4, _physicalRectSize, _heatMapSize);

            var canMoveForward = localForwardHeatMapIndex != -1 && _heatMap[localForwardHeatMapIndex] < 100;
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

                for(var i = 1; i < 4; i++)
                {
                    var searchRotation = math.mul(rotation.Value, quaternion.EulerXYZ(0, _rotationStepSpread * i * 4, 0));
                    GetDeltaHeatAtRotation(
                        translation.Value,
                        math.mul(searchRotation, globalForward),
                        SHEEP_MOVEMENT_SPEED * 3,
                        out var heatValuePositive);
                    positiveSideValue += heatValuePositive;

                    searchRotation = math.mul(rotation.Value, quaternion.EulerXYZ(0, _rotationStepSpread * i * -4, 0));
                    GetDeltaHeatAtRotation(
                        translation.Value,
                        math.mul(searchRotation, globalForward),
                        SHEEP_MOVEMENT_SPEED * 3,
                        out var heatValueNegative);
                    negativeSideValue += heatValueNegative;
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
                
                if (negativeSideValue == positiveSideValue || minSideValue > 200)
                    sheep.StateExtraInfo = 0;

                if (minSideValue < 150)
                {
                    var randomValue = GetRandomNormalizedValue(sheep.UpdateGroupId);
                    if (randomValue < 0.3f)
                        ChangeState(0, ref sheep); //change to gototarget
                    else if (randomValue < 0.5)
                        ChangeState(3, ref sheep); //go to idle
                }
            }

            rotation.Value = math.mul(rotation.Value, quaternion.EulerXYZ(0, SHEEP_MOVEMENT_SPEED * 2 * _scaledDeltaTime * sheep.StateExtraInfo, 0));

            if(_executionTime - sheep.LastStateChangeTime > (5 + 15 * GetRandomNormalizedValue(sheep.UpdateGroupId))) //state exit time
            {
                ChangeState(0, ref sheep); //change to gototarget
            }
        }

        private void MoveAwayFromPoint(ref Translation translation, ref Rotation rotation, ref SheepComponentDataEntity sheep)
        {
            var escapeDistance = math.distance(translation.Value.xz, _inputsArray[3].LocalInputPosition);
            if (escapeDistance > 10)
                return;

            var normalizedEscapeDirection = math.normalizesafe(translation.Value.xz - _inputsArray[3].LocalInputPosition);
            var escapeRotation = HorizontalLookAtRotation(normalizedEscapeDirection);

            var escapeForward = new float3(normalizedEscapeDirection.x, 0, normalizedEscapeDirection.y) * SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 10f;
            
            var nextPositionMapIndex = LocalPositionToHeatMapIndex(translation.Value + escapeForward * SHEEP_MOVEMENT_SPEED * 4, _physicalRectSize, _heatMapSize);

            var canMoveForward = nextPositionMapIndex != -1 && _heatMap[nextPositionMapIndex] < 200;
            if (canMoveForward)
            {
                translation.Value += escapeForward;
                rotation.Value = escapeRotation;
                return;
            }
            
            /*
            if (_codeIterator == sheep.UpdateGroupId)
            {
                var positiveNormalizedSearchRot = math.mul(escapeRotation, quaternion.EulerXYZ(0, _rotationStepSpread * 3, 0));
                GetDeltaHeatAtRotation(
                    translation.Value,
                    math.mul(positiveNormalizedSearchRot, globalForward),
                    SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 3,
                    out var heatValuePositive);

                var negativeNormalizedSearchRot = math.mul(escapeRotation, quaternion.EulerXYZ(0, _rotationStepSpread * -3, 0));
                GetDeltaHeatAtRotation(
                    translation.Value,
                    math.mul(negativeNormalizedSearchRot, globalForward),
                    SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 3,
                    out var heatValueNegative);


                var targetRotation = escapeRotation;

                if (heatValuePositive != heatValueNegative)
                {
                    targetRotation = (heatValuePositive > heatValueNegative) ?
                        negativeNormalizedSearchRot :
                        positiveNormalizedSearchRot;
                }

                var angle = GetSignedAngleWidthRotations(targetRotation, rotation.Value, out var sign);
                var stepAngle = SHEEP_MOVEMENT_SPEED * _scaledDeltaTime * 5f;
                if (angle > stepAngle)
                    angle = stepAngle;
                rotation.Value = math.mul(rotation.Value, quaternion.EulerXYZ(0, sign * angle, 0));
            }
            */
        }


        private void ChangeState(int newState, ref SheepComponentDataEntity sheepDataComponent)
        {
            if (newState < 0 || newState > 4)
                return;

            sheepDataComponent.CurrentState = newState;
            sheepDataComponent.LastStateChangeTime = _executionTime;
            sheepDataComponent.StateExtraInfo = 0;
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

        private bool GetDeltaHeatAtRotation(float3 entityPosition, float3 searchDirection, float distance,  out byte value)
        {
            value = byte.MaxValue;
            var searchPosition = entityPosition + (searchDirection * distance);
#if DEBUG_RAYS
            Debug.DrawLine(entityPosition, searchPosition, Color.yellow);
#endif
            var mapIndex = LocalPositionToHeatMapIndex(searchPosition, _physicalRectSize, _heatMapSize);
            if (mapIndex != -1)
            {
                value = _heatMap[mapIndex];
                return true;
            }
            return false;
        }

        private int LocalPositionToHeatMapIndex(float3 position, float2 physicalRectSize, int2 heatMapSize)
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
    }
}
