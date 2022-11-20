using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public class InputEntityManager : MonoBehaviour
{

    [SerializeField] private Transform[] _attractTargets;
    public int InputAttractCount => _attractTargets.Length;
    
    [Space(10)]
    [SerializeField] private Transform[] _repulseTargets;
    public int InputRepulseCount => _repulseTargets.Length;

    [Space(20)]
    [SerializeField] private ComputeShader _bakeComputeShader;
    [SerializeField] private Renderer _debugSurfaceRenderer;

    private Entity _inputEntity;
    private EntityManager _entityManager;
    private Matrix4x4 _originTransform;

    private static InputEntityManager _instance;

    private float _bakeSideSize;
    private RenderTexture _inputBakeTexture;

    private const int BAKE_INDEX_SCALE = 20;


    public static InputEntityManager Instance
    {
        get
        {
            if(_instance == null)
            {
                _instance = GameObject.FindObjectOfType<InputEntityManager>();
                if (_instance == null)
                    Debug.LogError("No input entity manager on scene");
            }
            return _instance;
        }
    }

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _inputEntity = _entityManager.CreateEntity();
        _originTransform = Matrix4x4.identity;

        var dynamicBuffer = _entityManager.AddBuffer<InputPoint>(_inputEntity);
    }

    public void Initialize(float bakeSideSize)
    {
        _bakeSideSize = bakeSideSize;
        _inputBakeTexture = new RenderTexture(
            (int)bakeSideSize * PositionIDBakeController.BakeTexturePPU, 
            (int)bakeSideSize * PositionIDBakeController.BakeTexturePPU, 
            0, 
            RenderTextureFormat.ARGB32);

        _inputBakeTexture.enableRandomWrite = true;
        _inputBakeTexture.filterMode = FilterMode.Point;
        _inputBakeTexture.Create();

        var mat = _debugSurfaceRenderer.material;
        mat.SetTexture("_BaseMap", _inputBakeTexture);

        _debugSurfaceRenderer.transform.localScale = new Vector3(bakeSideSize, 1, bakeSideSize);
        SpawnInputBakeEntity(Vector2.one * bakeSideSize, _inputBakeTexture);
    }


    private void SpawnInputBakeEntity(float2 bakeRectSize, RenderTexture bakeTexture)
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
                Type = TextureTypes.INPUT_BAKE_TEXTURE
            });

    }

    private void Update()
    {
        if (_attractTargets == null || _attractTargets.Length == 0 || _inputBakeTexture == null)
            return;

        var dynamicBuffer = _entityManager.GetBuffer<InputPoint>(_inputEntity);
        if (dynamicBuffer.Length != _attractTargets.Length)
        {
            for (var i = dynamicBuffer.Length; i < _attractTargets.Length; i++)
            {
                dynamicBuffer.Add(new InputPoint
                {
                    LocalInputPosition = new float2(0, 0),
                });
            }
        }

        var inputBuffer = dynamicBuffer.Reinterpret<float2>();

        if(inputBuffer.Length != _attractTargets.Length)
        {
            Debug.LogError("Buffer different from target size");
            return;
        }

        var attractData = new InputAttractData[inputBuffer.Length];
        for (var i = 0; i < inputBuffer.Length; i++)
        {
            var positionValue = WorldToInputSpace(_attractTargets[i].position);
            inputBuffer[i] = positionValue;

            attractData[i] = new InputAttractData
            {
                UvPosition = WorldToTextureUv(_attractTargets[i].position),
                ColorChannelId = InputIndexToColorChannelCode(i)
            };
        }
        
        var inputBakeKernel = _bakeComputeShader.FindKernel("InputAttractBake");
        _bakeComputeShader.SetTexture(inputBakeKernel, "InputTexture", _inputBakeTexture);

        //data buffers
        var inputAttractBufferData = new ComputeBuffer(attractData.Length, sizeof(float) * 3);
        inputAttractBufferData.SetData(attractData);
        _bakeComputeShader.SetBuffer(inputBakeKernel, "InputAttractDataBuffer", inputAttractBufferData);

        _bakeComputeShader.SetInt("TextureWidth", _inputBakeTexture.width);
        _bakeComputeShader.SetInt("TextureHeight", _inputBakeTexture.height);

        _bakeComputeShader.SetInt("InputAttractBufferCount", attractData.Length);

        var repulseData = new InputRepulseData[_repulseTargets.Length];
        for (var i = 0; i < _repulseTargets.Length; i++)
        {
            repulseData[i] = new InputRepulseData
            {
                UvPosition = WorldToTextureUv(_repulseTargets[i].position),
                ColorChannelId = InputIndexToColorChannelCode(i),
                Width = 0.10f,
                Strengh = 1.0f
            };
        }
        var inputRepulsionBufferData = new ComputeBuffer(repulseData.Length, sizeof(float) * 5);
        inputRepulsionBufferData.SetData(repulseData);
        _bakeComputeShader.SetBuffer(inputBakeKernel, "InputRepulseDataBuffer", inputRepulsionBufferData);
        _bakeComputeShader.SetInt("InputRepulseBufferCount", repulseData.Length);


        _bakeComputeShader.Dispatch(
            inputBakeKernel,
            _inputBakeTexture.width / 4,
            _inputBakeTexture.height / 4,
            1);

        inputAttractBufferData.Release();
        inputRepulsionBufferData.Release();
    }

    public void SetInputReferenceMatrix(Matrix4x4 inputReference)
    {
        _originTransform = inputReference;
    }

    private float2 WorldToInputSpace(Vector3 worldPosition)
    {
        var localPos =_originTransform.MultiplyPoint3x4(worldPosition);
        return new float2(localPos.x, localPos.z);
    }

    private void OnDrawGizmos()
    {
        if (_attractTargets == null)
            return;

        Gizmos.color = Color.green;
        foreach(var target in _attractTargets)
            Gizmos.DrawWireSphere(target.position, 10f);

        Gizmos.color = Color.red;
        foreach (var target in _repulseTargets)
            Gizmos.DrawWireSphere(target.position, 20f);
    }

    public float2 WorldToTextureUv(Vector3 worldPos)
    {
        var positionValue = WorldToInputSpace(worldPos);
        positionValue = positionValue + new float2(1, 1) * _bakeSideSize * 0.5f;
        positionValue /= _bakeSideSize;
        return positionValue;
    }

    public static float InputIndexToColorChannelCode(int index)
    {
        float value = (index + 1) * BAKE_INDEX_SCALE;
        if (value >= byte.MaxValue)
            Debug.LogError($"Exceeding byte size in index {index + 1} baked to {value} with scale {BAKE_INDEX_SCALE}");

        value = value / (float)byte.MaxValue;
        return value;
    }

    public static int ColorCodeToIndex(int colorChannel)
    {
        var result = (int)(colorChannel / (float)BAKE_INDEX_SCALE) - 1;
        if (result < 0)
            result = 0;
        return result;
    }
}

[InternalBufferCapacity(4)]
public struct InputPoint: IBufferElementData
{
    public float2 LocalInputPosition;
}

public struct InputAttractData
{
    public float ColorChannelId;
    public float2 UvPosition;
}

public struct InputRepulseData
{
    public float ColorChannelId;
    public float2 UvPosition;
    public float Width;
    public float Strengh;
}