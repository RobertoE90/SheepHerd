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
    private Vector3[] _repulseLastFramePositions;

    [Space(20)]
    [SerializeField] private ComputeShader _bakeComputeShader;
    [SerializeField] private Renderer _debugSurfaceRenderer;

    private Entity _inputAttractEntity;
    private Entity _inputRepulseEntity;
    private EntityManager _entityManager;
    private Matrix4x4 _originTransform;

    private static InputEntityManager _instance;

    private float _worldScale;
    private float2 _bakeSize;
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
        _inputAttractEntity = _entityManager.CreateEntity();
        _entityManager.AddComponent(_inputAttractEntity, typeof(InputAttractTagComponent));
        _entityManager.AddBuffer<InputPoint>(_inputAttractEntity);

        _inputRepulseEntity = _entityManager.CreateEntity();
        _entityManager.AddComponent(_inputRepulseEntity, typeof(InputRepulseTagComponent));
        _entityManager.AddBuffer<InputPoint>(_inputRepulseEntity);

        _originTransform = Matrix4x4.identity;

    }

    public void Initialize(Vector2 bakeSize, float worldScale)
    {
        _bakeSize = bakeSize * worldScale;
        _inputBakeTexture = new RenderTexture(
            (int)bakeSize.x * PositionIDBakeController.BakeTexturePPU, 
            (int)bakeSize.y * PositionIDBakeController.BakeTexturePPU, 
            0, 
            RenderTextureFormat.ARGB32);

        _inputBakeTexture.enableRandomWrite = true;
        _inputBakeTexture.filterMode = FilterMode.Point;
        _inputBakeTexture.Create();

        var mat = _debugSurfaceRenderer.material;
        mat.SetTexture("_BaseMap", _inputBakeTexture);

        _debugSurfaceRenderer.transform.localScale = new Vector3(bakeSize.x * worldScale, 1, bakeSize.y * worldScale);
        SpawnInputBakeEntity(_bakeSize, _inputBakeTexture);

        _worldScale = worldScale;
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

    private ComputeBuffer ProcessInputAttractData()
    {
        var dynamicBuffer = _entityManager.GetBuffer<InputPoint>(_inputAttractEntity);
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

        var inputBufferAsArray = dynamicBuffer.Reinterpret<float2>();

        if (inputBufferAsArray.Length != _attractTargets.Length)
        {
            Debug.LogError("Buffer different from target size");
            return new ComputeBuffer(0, 0);
        }

        var attractData = new InputAttractData[inputBufferAsArray.Length];
        for (var i = 0; i < inputBufferAsArray.Length; i++)
        {
            var positionValue = WorldToInputSpace(_attractTargets[i].position);
            inputBufferAsArray[i] = positionValue;

            attractData[i] = new InputAttractData
            {
                UvPosition = WorldToTextureUv(_attractTargets[i].position),
                ColorChannelId = InputIndexToColorChannelCode(i)
            };
        }

        var inputAttractBufferData = new ComputeBuffer(attractData.Length, sizeof(float) * 3);
        inputAttractBufferData.SetData(attractData);
        return inputAttractBufferData;
    }

    private bool ProcessInputRepulseData(out ComputeBuffer cb)
    {
        if(_repulseTargets.Length == 0)
        {
           cb = new ComputeBuffer(1, 4);
           return false;
        }
        var dynamicBuffer = _entityManager.GetBuffer<InputPoint>(_inputRepulseEntity);
        if (dynamicBuffer.Length != _repulseTargets.Length)
        {
            for (var i = dynamicBuffer.Length; i < _repulseTargets.Length; i++)
            {
                dynamicBuffer.Add(new InputPoint
                {
                    LocalInputPosition = new float2(0, 0),
                });
            }
        }

        var inputBufferAsArray = dynamicBuffer.Reinterpret<float2>();

        if (inputBufferAsArray.Length != _repulseTargets.Length)
        {
            Debug.LogError("Buffer different from target size");
            cb = new ComputeBuffer(1, 4);
            return false;
        }

        
        var repulseData = new InputRepulseData[_repulseTargets.Length];
        for (var i = 0; i < _repulseTargets.Length; i++)
        {
            inputBufferAsArray[i] = WorldToInputSpace(_repulseTargets[i].position);
            float repulseSpeed = ComputeRepulseInputSpeed(i);
            repulseSpeed /= _bakeSize.x;
            repulseSpeed = Mathf.Clamp(0f, repulseSpeed * 2, 0.2f);

            repulseData[i] = new InputRepulseData
            {
                UvPosition = WorldToTextureUv(_repulseTargets[i].position),
                ColorChannelId = InputIndexToColorChannelCode(i),
                Width = repulseSpeed,
                Strengh = 1.0f
            };
        }

        cb = new ComputeBuffer(repulseData.Length, sizeof(float) * 5);
        cb.SetData(repulseData);
        
        return true;
    }

    private void Update()
    {
        if (_attractTargets == null || _attractTargets.Length == 0 || _inputBakeTexture == null)
            return;

        var inputBakeKernel = _bakeComputeShader.FindKernel("InputAttractBake");
        _bakeComputeShader.SetTexture(inputBakeKernel, "InputTexture", _inputBakeTexture);

        var inputAttractBufferData = ProcessInputAttractData();
        _bakeComputeShader.SetBuffer(inputBakeKernel, "InputAttractDataBuffer", inputAttractBufferData);
        _bakeComputeShader.SetInt("InputAttractBufferCount", inputAttractBufferData.count);

        var succeed = ProcessInputRepulseData(out var inputRepulsionBufferData);
        if (succeed)
        {
            _bakeComputeShader.SetBuffer(inputBakeKernel, "InputRepulseDataBuffer", inputRepulsionBufferData);
            _bakeComputeShader.SetInt("InputRepulseBufferCount", inputRepulsionBufferData.count);
        }

        _bakeComputeShader.SetInt("TextureWidth", _inputBakeTexture.width);
        _bakeComputeShader.SetInt("TextureHeight", _inputBakeTexture.height);


        _bakeComputeShader.Dispatch(
            inputBakeKernel,
            _inputBakeTexture.width / 4,
            _inputBakeTexture.height / 4,
            1);

        inputAttractBufferData.Release();
        inputRepulsionBufferData.Release();

        if (_repulseLastFramePositions == null)
            _repulseLastFramePositions = new Vector3[_repulseTargets.Length];

        for(var i = 0; i < _repulseTargets.Length; i++)
        {
            _repulseLastFramePositions[i] = _repulseTargets[i].position;
        }
    }

    private float ComputeRepulseInputSpeed(int index)
    {
        if(_repulseLastFramePositions == null)
            return 0f;

        var delta = Vector3.Distance(_repulseTargets[index].position, _repulseLastFramePositions[index]);
        return delta / Time.deltaTime;
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

        Gizmos.color = Color.blue;
        foreach(var target in _attractTargets)
            Gizmos.DrawWireSphere(target.position, 10f * _worldScale);

        Gizmos.color = Color.red;
        foreach (var target in _repulseTargets)
            Gizmos.DrawWireSphere(target.position, 20f * _worldScale);
    }

    private float2 WorldToTextureUv(Vector3 worldPos)
    {
        var positionValue = WorldToInputSpace(worldPos);
        positionValue = positionValue + _bakeSize * 0.5f;
        positionValue = new float2(positionValue.x / _bakeSize.x, positionValue.y / _bakeSize.y);
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

[InternalBufferCapacity(10)]
public struct InputPoint: IBufferElementData
{
    public float2 LocalInputPosition;
}

public struct InputAttractTagComponent: IComponentData
{

}


public struct InputRepulseTagComponent : IComponentData
{

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