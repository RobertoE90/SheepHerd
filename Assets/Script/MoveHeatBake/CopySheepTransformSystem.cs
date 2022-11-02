using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class CopySheepTransformSystem : SystemBase
{
    private EntityQuery _jobQuery;
    private EntityQuery _sheepsQuery;

    protected override void OnStartRunning()
    {
        _jobQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(Translation),
            typeof(Rotation),
            typeof(IndexReferenceComponent)
        });

        _sheepsQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(LocalToWorld),
            typeof(SheepComponentDataEntity)
        });
    }

    protected override void OnUpdate()
    {
        var referencesTransforms = _sheepsQuery.ToComponentDataArray<LocalToWorld>(Allocator.TempJob);

        var job = new CopyTransformJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<Rotation>(),
            GetComponentTypeHandle<IndexReferenceComponent>(),
            referencesTransforms);

        Dependency = job.ScheduleParallel(_jobQuery);
        Dependency.Complete();

        referencesTransforms.Dispose();
    }


    private struct CopyTransformJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Translation> _translationType;
        public ComponentTypeHandle<Rotation> _rotationType;
        public ComponentTypeHandle<IndexReferenceComponent> _indexReferenceType;
        [ReadOnly] private NativeArray<LocalToWorld> _transformArray;
        

        public CopyTransformJob(
            ComponentTypeHandle<Translation> t,
            ComponentTypeHandle<Rotation> r,
            ComponentTypeHandle<IndexReferenceComponent> indexRefType,
            NativeArray<LocalToWorld> transformArray)
        {
            _translationType = t;
            _rotationType = r;
            _indexReferenceType = indexRefType;
            _transformArray = transformArray;
        }

        [BurstCompile]
        public void Execute(ArchetypeChunk batchInChunk, int indexOfFirstEntityInQuery)
        {
            NativeArray<Translation> translations = batchInChunk.GetNativeArray(this._translationType);
            NativeArray<Rotation> rotations = batchInChunk.GetNativeArray(this._rotationType);
            NativeArray< IndexReferenceComponent > indexRefs = batchInChunk.GetNativeArray(this._indexReferenceType);

            for (int i = 0; i < batchInChunk.Count; ++i)
            {
                var t = translations[i];
                var r = rotations[i];

                var referenceMatrix = _transformArray[indexRefs[i].ReferenceIndex];
                t.Value = math.mul(referenceMatrix.Value, new float4(0, 0, 0, 1)).xyz;
                r.Value = referenceMatrix.Rotation;

                translations[i] = t;
                rotations[i] = r;
            }
        }
    }
}
