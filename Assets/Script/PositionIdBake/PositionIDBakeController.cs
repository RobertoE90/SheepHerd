using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public class PositionIDBakeController : BaseEntityCameraBaker
{
    private Vector2Int _textureSize;

    public override void Initialize(int referencesCount, Vector2 bakeArea, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        base.Initialize(referencesCount, bakeArea, centerWorldPosition, centerWorldRotation);
        
        _textureSize = new Vector2Int((int)(bakeArea.x * _bakeTexturePPU), (int)(bakeArea.y * _bakeTexturePPU));
        
        SpawnPositionIdBakers(referencesCount);
        //SpawnHeatBakeBufferEntity(bakeArea, bakeTexture);
    }

    private void SpawnPositionIdBakers(int referencesCount)
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
            typeof(IndexReferenceComponent)
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
            entityManager.SetComponentData<Scale>(bakerEntities[i], new Scale { Value = 1 });
            entityManager.SetComponentData<RenderBounds>(
                bakerEntities[i],
                new RenderBounds { Value = bounds });

            entityManager.SetComponentData<URPMaterialPropertyBaseColor>(
                bakerEntities[i],
                new URPMaterialPropertyBaseColor
                {
                    Value = new float4(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, 1f)
                });

            entityManager.SetComponentData<IndexReferenceComponent>(
                bakerEntities[i],
                new IndexReferenceComponent
                {
                    ReferenceIndex = i
                });

        }

        bakerEntities.Dispose();
    }

    /*
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
    */

}
