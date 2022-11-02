using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class MovementHeatBakeController : BaseEntityCameraBaker
{
    [SerializeField] private int _decalImageSideSize;

    public override void Initialize(int referencesCount, Vector2 bakeArea, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        base.Initialize(referencesCount, bakeArea, centerWorldPosition, centerWorldRotation);

        var heatDecalGenerator = new MoveHeatDecalGenerator(_decalImageSideSize, _decalImageSideSize);
        _bakeMaterial.SetTexture("_BaseMap", heatDecalGenerator.BakedTexture);

        SpawnHeatMovementBakers(referencesCount);
        SpawnHeatBakeBufferEntity(bakeArea, _bakeTexture);
    }

    private void SpawnHeatMovementBakers(int referencesCount)
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var archetype = entityManager.CreateArchetype(new ComponentType[] {
            typeof(LocalToWorld),
            typeof(Translation),
            typeof(Rotation),
            typeof(Scale),
            typeof(RenderBounds),
            typeof(RenderMesh),
            typeof(IndexReferenceComponent)
        });

        var heatBakersEntities = new NativeArray<Entity>(referencesCount, Allocator.Persistent);
        entityManager.CreateEntity(archetype, heatBakersEntities);

        var meshComponent = new RenderMesh
        {
            material = _bakeMaterial,
            mesh = _bakeMesh,
            layer = _bakeLayerId,
            castShadows = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
        };

        var bounds = new AABB
        {
            Extents = new float3(5, 1, 5)
        };
        for (var i = 0; i < heatBakersEntities.Length; i++)
        {
            entityManager.SetSharedComponentData<RenderMesh>(heatBakersEntities[i], meshComponent);
            entityManager.SetComponentData<Scale>(heatBakersEntities[i], new Scale { Value = _decalImageSideSize });
            entityManager.SetComponentData<IndexReferenceComponent>(heatBakersEntities[i], new IndexReferenceComponent { ReferenceIndex = i });
            entityManager.SetComponentData<RenderBounds>(
                heatBakersEntities[i], 
                new RenderBounds { Value = bounds });
        }

        heatBakersEntities.Dispose();
    }

    private void SpawnHeatBakeBufferEntity(float2 bakeRectSize, RenderTexture bakeTexture)
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var archetype = entityManager.CreateArchetype(new ComponentType[] {
            typeof(HeatMapSharedComponentData),
        });

        var heatMapBufferEntity = entityManager.CreateEntity(archetype);
        entityManager.SetSharedComponentData<HeatMapSharedComponentData>(
            heatMapBufferEntity,
            new HeatMapSharedComponentData
            {
                PhysicalRectSize = bakeRectSize,
                HeatTexture = bakeTexture,
            });

    }
}
