using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class EnvironmentHeightMapController : BaseCameraBaker
{
    [SerializeField] private ComputeShader _heightGeneratorComputeShader;
    [SerializeField] private Color _volumeColor;

    private RenderTexture _cameraRenderTexture;
    private Vector3 _originPosition;
    private Quaternion _originRotation;
    private Vector2 _horizontalArea;

    private bool _renderingFlag = false;
    private Stopwatch _renderTimer;

    private MarchingCubesTool _meshingTool;

    private void Awake()
    {
        _bakeCamera.enabled = false;
    }

    public override void Initialize(Vector2 bakeArea, float texturePPU, float worldScale, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        base.Initialize(bakeArea, texturePPU, worldScale, centerWorldPosition, centerWorldRotation);
        _originPosition = centerWorldPosition;
        _originRotation = centerWorldRotation;
        _horizontalArea = worldScale * bakeArea;

        _bakeCamera.transform.position = centerWorldPosition + centerWorldRotation * (Vector3.up * _cameraDepth);
        _bakeCamera.nearClipPlane = 0.0f;
        _bakeCamera.farClipPlane = _cameraDepth;

        _cameraRenderTexture = new RenderTexture(_bakeTexture.width, _bakeTexture.height, 0, RenderTextureFormat.Default);
        _cameraRenderTexture.enableRandomWrite = true;
        _cameraRenderTexture.Create();
        _bakeCamera.targetTexture = _cameraRenderTexture;


        RenderPipelineManager.endCameraRendering += OnCameraRenderEnded;
        _renderTimer = Stopwatch.StartNew();
        _meshingTool = new MarchingCubesTool(new float3(1f, 2.5f, 1.25f), new int3(5, 7, 5));
        
        CameraRender();
    }

    private void OnCameraRenderEnded(ScriptableRenderContext context, Camera camera)
    {
        if (camera != _bakeCamera)
            return;

        _renderingFlag = false;
        _renderTimer.Stop();
        Debug.Log($"done rendering {_renderTimer.ElapsedTicks}");
    }

    private async void CameraRender()
    {
        await _meshingTool.ComputeMesh();
        return;
        var step = _cameraDepth / 255.0f;
        for (var j = 0; j < 500; j++)
        {
            ClearBake(_bakeTexture);
            if (!Application.isPlaying)
                return;
            /*
            var material = _bakeDebugMeshRenderer.material;
            material.SetTexture("_BaseMap", _bakeTexture);
            for (var sliceInt = 254; sliceInt >= 0; sliceInt--)
            {
                

                
                if (!_renderingFlag)
                {
                    _renderTimer.Reset();
                    _renderingFlag = true;
                    _bakeCamera.nearClipPlane = sliceInt * step;
                    _bakeCamera.farClipPlane = (sliceInt + 1) * step;
                    _bakeCamera.Render();
                }
                await Task.Yield();
                await Task.Yield();
                
                BakeHeightMap(1f - sliceInt / 255f);
            }
            */
            
            /*
            await Task.Delay(5000);
            material.SetTexture("_BaseMap", _cameraRenderTexture);
            for (var i = 0f; i < 1; i+= 0.25f)
            {
                GetIsland(_bakeTexture, _cameraRenderTexture, new float2(i, i + 0.25f));
                await Task.Delay(3000);
            }
            */
        }
    }

    private void BakeHeightMap(float normalizedHeight)
    {
        if(_bakeTexture.width != _cameraRenderTexture.width || _bakeTexture.height != _cameraRenderTexture.height)
        {
            Debug.LogError("Input and output texture size dont match");
            return;
        }
        var bakeKernel = _heightGeneratorComputeShader.FindKernel("BakeHeightCS");
        
        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _heightGeneratorComputeShader,
            bakeKernel,
            _cameraRenderTexture.width,
            _cameraRenderTexture.height))
        {
            return;
        }

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _heightGeneratorComputeShader,
            bakeKernel,
            _bakeTexture.width,
            _bakeTexture.height))
        {
            return;
        }


        _heightGeneratorComputeShader.SetTexture(bakeKernel, "InputTexture", _cameraRenderTexture);
        _heightGeneratorComputeShader.SetTexture(bakeKernel, "ResultTexture", _bakeTexture);
        _heightGeneratorComputeShader.SetFloat("NormalizedBakeHeight", normalizedHeight);

        _heightGeneratorComputeShader.Dispatch(
            bakeKernel,
            _bakeTexture.width / 4,
            _bakeTexture.height / 4,
            1);
    }

    private void ClearBake(RenderTexture targetTexture)
    {
        var bakeKernel = _heightGeneratorComputeShader.FindKernel("ClearBakeCS");

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _heightGeneratorComputeShader,
            bakeKernel,
            targetTexture.width,
            targetTexture.height))
        {
            return;
        }
        
        _heightGeneratorComputeShader.SetTexture(bakeKernel, "ResultTexture", targetTexture);

        _heightGeneratorComputeShader.Dispatch(
            bakeKernel,
            targetTexture.width / 4,
            targetTexture.height / 4,
            1);
    }

    private void GetIsland(RenderTexture inputTexture, RenderTexture targetTexture, float2 islandHeightRange)
    {
        var kernel = _heightGeneratorComputeShader.FindKernel("GetIslandCS");

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _heightGeneratorComputeShader,
            kernel,
            inputTexture.width,
            inputTexture.height))
        {
            return;
        }

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _heightGeneratorComputeShader,
            kernel,
            targetTexture.width,
            targetTexture.height))
        {
                return;
        }

        _heightGeneratorComputeShader.SetTexture(kernel, "InputTexture", inputTexture);
        _heightGeneratorComputeShader.SetTexture(kernel, "ResultTexture", targetTexture);

        _heightGeneratorComputeShader.SetFloat("IslandMinValue", islandHeightRange.x);
        _heightGeneratorComputeShader.SetFloat("IslandMaxValue", islandHeightRange.y);


        _heightGeneratorComputeShader.Dispatch(
            kernel,
            _bakeTexture.width / 4,
            _bakeTexture.height / 4,
            1);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _cameraRenderTexture.Release();
        Destroy(_cameraRenderTexture);
    }

    private void OnDrawGizmos()
    {
        if (!_isInitialized)
            return;
        Gizmos.color = _volumeColor;
        Gizmos.matrix = Matrix4x4.TRS(_originPosition, _originRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.up * 0.5f * _cameraDepth, new Vector3(_horizontalArea.x, _cameraDepth, _horizontalArea.y));

        _meshingTool.DrawGizmo(Vector3.forward * -3);
    }
}

