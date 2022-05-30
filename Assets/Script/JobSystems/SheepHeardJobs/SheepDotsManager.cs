using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;


public class SheepDotsManager : MonoBehaviour
{
    [Header("Herd Config")]
    [SerializeField] private int _sheepCount;
    [SerializeField] private float _spawnSquareSide;

    [Space(20)]
    [SerializeField] Mesh _sheepMesh;
    [SerializeField] Material _sheepMaterial;

    private int _sheepAgentViewLayerInt;
    private NativeArray<Entity> _sheepEntities;
    private EntityManager _entityManager;

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _sheepAgentViewLayerInt = LayerMask.NameToLayer("SheepAgentViewLayer");
        SpawnHerd();
    }

    private async void SpawnHerd()
    {
        await Task.Run(() => { });

        var archetype = _entityManager.CreateArchetype(new ComponentType[] { 
            typeof(LocalToWorld),
            typeof(Translation), 
            typeof(Rotation),
            typeof(NonUniformScale),
            typeof(WorldRenderBounds),
            typeof(RenderBounds),
            typeof(ChunkWorldRenderBounds),
            typeof(PerInstanceCullingTag),
            typeof(RenderMesh),
            typeof(SheepFollowerComponent)
        });
        
        _sheepEntities = new NativeArray<Entity>(_sheepCount, Allocator.Persistent);
        _entityManager.CreateEntity(archetype, _sheepEntities);

        var meshComponent = new RenderMesh
        {
            material = _sheepMaterial,
            mesh = _sheepMesh,
            //layer = _sheepAgentViewLayerInt,
        };

        for (var i = 0; i < _sheepEntities.Length; i++)
        {
            _entityManager.SetSharedComponentData<RenderMesh>(_sheepEntities[i], meshComponent);
            _entityManager.SetComponentData<NonUniformScale>(_sheepEntities[i], new NonUniformScale { Value = Vector3.one });
            _entityManager.SetComponentData<Rotation>(_sheepEntities[i], new Rotation { Value = Quaternion.identity });
            _entityManager.SetComponentData<Translation>(
                _sheepEntities[i],
                new Translation
                {
                    Value = new Vector3(Random.value - 0.5f, 0, Random.value - 0.5f) * _spawnSquareSide
                });


            _entityManager.SetComponentData<SheepFollowerComponent>(
                _sheepEntities[i], 
                new SheepFollowerComponent { FollowIndex = (int)Random.Range(0, _sheepEntities.Length) });
        }
    }

    private void OnDestroy()
    {
        _sheepEntities.Dispose();
    }
}
