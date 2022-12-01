using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class PositionIDBakeController : BaseEntityCameraBaker
{
    private static int _ppuCopy;
    public static int BakeTexturePPU => _ppuCopy;

    public override void Initialize(int referencesCount, Vector2 bakeArea, float worldScale, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        base.Initialize(referencesCount, bakeArea, worldScale, centerWorldPosition, centerWorldRotation);
        SpawnPositionIdBakers(referencesCount, worldScale);
        _ppuCopy = _bakeTexturePPU;
        SpawnEntityIdTextureBakeEntity(bakeArea * worldScale, _bakeTexture);
    }

    private void SpawnPositionIdBakers(int referencesCount, float worldScale)
    {
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        var archetype = entityManager.CreateArchetype(new ComponentType[] {
            typeof(LocalToWorld),
            typeof(Translation),
            typeof(Rotation),
            typeof(Scale),
            typeof(RenderBounds),
            typeof(RenderMesh),
            typeof(URPMaterialPropertyBaseColor),
            typeof(CopyTransformReferenceComponent)
        });

        var bakerEntities = new NativeArray<Entity>(referencesCount, Allocator.Persistent);
        entityManager.CreateEntity(archetype, bakerEntities);

        var bounds = new AABB
        {
            Extents = new float3(1, 1, 1)
        };

        var meshComponent = new RenderMesh
        {
            material = _bakeMaterial,
            mesh = _bakeMesh,
            layer = _bakeLayerId,
            castShadows = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
        };

        for (var i = 0; i < bakerEntities.Length; i++)
        {
            entityManager.SetSharedComponentData<RenderMesh>(bakerEntities[i], meshComponent);
            entityManager.SetComponentData<Scale>(bakerEntities[i], new Scale { Value = worldScale });
            entityManager.SetComponentData<RenderBounds>(
                bakerEntities[i],
                new RenderBounds { Value = bounds });

            var iPrimaryAxis = (int)(i / 255f);
            var iSecoundarySeAxis = i % 255f;

            entityManager.SetComponentData<URPMaterialPropertyBaseColor>(
                bakerEntities[i],
                new URPMaterialPropertyBaseColor
                {
                    Value = new float4(1f, iPrimaryAxis / 255f, iSecoundarySeAxis / 255f, 1f)
                });

            entityManager.SetComponentData<CopyTransformReferenceComponent>(
                bakerEntities[i],
                new CopyTransformReferenceComponent
                {
                    ReferenceIndex = i,
                    CopyTranslation = true,
                    CopyRotation = true
                });
        }
        bakerEntities.Dispose();
    }


    private void SpawnEntityIdTextureBakeEntity(float2 bakeRectSize, RenderTexture bakeTexture)
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
                Type = TextureTypes.ENTITY_ID_TEXTURE
            });

    }
}
