using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class CopySheepTransformSystem : SystemBase
{
    private EntityQuery _jobQuery;
    private EntityQuery _sheepsQuery;
    private int _currentChunkExecutionCode;
    private int _chunkExecutionLoopCount;

    protected override void OnStartRunning()
    {
        _jobQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(Translation),
            typeof(Rotation),
            typeof(CopyTransformReferenceComponent)
        });

        _sheepsQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(LocalToWorld),
            typeof(SheepComponentDataEntity)
        });

        _currentChunkExecutionCode = 0;
        _chunkExecutionLoopCount = 20;
    }

    protected override void OnUpdate()
    {
        var referencesTransforms = _sheepsQuery.ToComponentDataArray<LocalToWorld>(Allocator.TempJob);
        var job = new CopyTransformJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<Rotation>(),
            GetComponentTypeHandle<CopyTransformReferenceComponent>(),
            referencesTransforms,
            _currentChunkExecutionCode,
            _chunkExecutionLoopCount
            );

        Dependency = job.ScheduleParallel(_jobQuery);
        Dependency.Complete();
        _currentChunkExecutionCode++;
        if (_currentChunkExecutionCode == _chunkExecutionLoopCount)
            _currentChunkExecutionCode = 0;
        referencesTransforms.Dispose();
    }


    private struct CopyTransformJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Translation> _translationType;
        public ComponentTypeHandle<Rotation> _rotationType;
        public ComponentTypeHandle<CopyTransformReferenceComponent> _indexReferenceType;
        [ReadOnly] private NativeArray<LocalToWorld> _transformArray;
        private int _chunkExcecutionCode;
        private int _executionLoopLength;

        public CopyTransformJob(
            ComponentTypeHandle<Translation> t,
            ComponentTypeHandle<Rotation> r,
            ComponentTypeHandle<CopyTransformReferenceComponent> indexRefType,
            NativeArray<LocalToWorld> transformArray,
            int chunkExecutionCode,
            int executionLoopLength)
        {
            _translationType = t;
            _rotationType = r;
            _indexReferenceType = indexRefType;
            _transformArray = transformArray;
            _chunkExcecutionCode = chunkExecutionCode;
            _executionLoopLength = executionLoopLength;
        }

        [BurstCompile]
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<Translation> translations = batchInChunk.GetNativeArray(this._translationType);
            NativeArray<Rotation> rotations = batchInChunk.GetNativeArray(this._rotationType);
            NativeArray<CopyTransformReferenceComponent> indexRefs = batchInChunk.GetNativeArray(this._indexReferenceType);


            for (int i = 0; i < batchInChunk.Count; ++i)
            {
                if (i % _executionLoopLength != _chunkExcecutionCode)
                    continue;

                var referenceMatrix = _transformArray[indexRefs[i].ReferenceIndex];
                
                if (indexRefs[i].CopyTranslation)
                {
                    var t = translations[i];
                    t.Value = math.mul(referenceMatrix.Value, new float4(0, 0, 0, 1)).xyz;
                    translations[i] = t;
                }

                if (indexRefs[i].CopyRotation)
                {
                    var r = rotations[i];
                    r.Value = referenceMatrix.Rotation;
                    rotations[i] = r;
                }
            }

        }
    }
}
