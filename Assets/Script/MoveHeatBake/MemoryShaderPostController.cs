using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class MemoryShaderPostController : MonoBehaviour
{
    [SerializeField] private float _decayValue;
    [SerializeField][Range(0.9f, 1.0f)] private float _traceDecayValue;
    [Space(20)]
    [SerializeField] private ComputeShader _memoryComputeShader;
    [SerializeField] private BaseEntityCameraBaker _baker;
    [Space(20)]
    
    private Camera _camera;
    private int _decayPropertyId;
    private int _tracedecayPropertyId;

    private RenderTexture _cacheRenderTexture;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _decayPropertyId = Shader.PropertyToID("_Decay");
        _tracedecayPropertyId = Shader.PropertyToID("_TraceDecay");
        _baker.BakerInitializedAction += OnBakerInitialized;
        if (_baker.IsInitialized)
            OnBakerInitialized();
    }

    private void OnBakerInitialized()
    {
        _cacheRenderTexture = new RenderTexture(_baker.BakeTexture.descriptor);
        _cacheRenderTexture.Create();
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += OnCameraEndRendering;
    }

    private void OnCameraEndRendering(ScriptableRenderContext context, Camera renderCamera)
    {
        if (_camera == null || renderCamera != _camera)
            return;

        if (_cacheRenderTexture == null || _baker == null || !_baker.IsInitialized )
            return;

        var kernelHandle = _memoryComputeShader.FindKernel("TextureDecay");
        _memoryComputeShader.SetFloat(_decayPropertyId, _decayValue);
        _memoryComputeShader.SetFloat(_tracedecayPropertyId, _traceDecayValue);
        _memoryComputeShader.SetTexture(kernelHandle, "CameraTexture", _baker.BakeTexture);
        _memoryComputeShader.SetTexture(kernelHandle, "Cache", _cacheRenderTexture);

        _memoryComputeShader.Dispatch(
            kernelHandle, 
            _baker.TextureSize.x / 4, 
            _baker.TextureSize.y / 4, 
            1);
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnCameraEndRendering;
    }

    private void OnDestroy()
    {
        _baker.BakerInitializedAction -= OnBakerInitialized;
    }

}
