using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class MovementHeatBakeController : BaseEntityCameraBaker
{
    [SerializeField] private float _decalQuadSideSize;
    public override void Initialize(int referencesCount, Vector2 bakeArea, float texturePPU, float worldScale, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        Initialize(bakeArea, texturePPU, worldScale, centerWorldPosition, centerWorldRotation);
        SpawnHeatMovementBakers(referencesCount, worldScale);
        SpawnHeatBakeBufferEntity(bakeArea * worldScale, _bakeTexture);
    }

    private void SpawnHeatMovementBakers(int referencesCount, float worldScale)
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var archetype = entityManager.CreateArchetype(new ComponentType[] {
            typeof(LocalToWorld),
            typeof(Translation),
            typeof(Rotation),
            typeof(Scale),
            typeof(RenderBounds),
            typeof(RenderMesh),
            typeof(CopyTransformReferenceComponent)
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
            entityManager.SetComponentData<Scale>(heatBakersEntities[i], new Scale { Value = _decalQuadSideSize * worldScale });
            entityManager.SetComponentData<CopyTransformReferenceComponent>(
                heatBakersEntities[i], 
                new CopyTransformReferenceComponent { 
                    ReferenceIndex = i,
                    CopyTranslation = true,
                    CopyRotation = true
                });
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
            typeof(PhysicalSizeTexture),
        });

        var heatMapBufferEntity = entityManager.CreateEntity(archetype);
        entityManager.SetSharedComponentData<PhysicalSizeTexture>(
            heatMapBufferEntity,
            new PhysicalSizeTexture
            {
                PhysicalTextureSize = bakeRectSize,
                TextureReference = bakeTexture,
                Type = TextureTypes.MOVE_HEAT_TEXTURE
            });
    }
}
