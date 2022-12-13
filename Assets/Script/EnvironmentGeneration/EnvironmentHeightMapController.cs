using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class EnvironmentHeightMapController : BaseCameraBaker
{
    [SerializeField] private ComputeShader _heightGeneratorComputeShader;
    [SerializeField] private Material _meshMaterial;
    [SerializeField] private Color _volumeColor;

    private Dictionary<string, int> _computeShaderKernels;

    private RenderTexture _cameraRenderTexture;
    private Vector3 _originPosition;
    private Quaternion _originRotation;
    private Vector2 _horizontalArea;

    private bool _renderingFlag = false;

    private Thread _processThread;

    private void Awake()
    {
        _bakeCamera.enabled = false;
    }

    public override void Initialize(Vector2 bakeArea, float texturePPU, float worldScale, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        base.Initialize(bakeArea, texturePPU, worldScale, centerWorldPosition, centerWorldRotation);

        InitializeKernelDictionary();

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

        var material = _bakeDebugMeshRenderer.material;
        material.SetTexture("_BaseMap", _bakeTexture);

        _processThread = new Thread(RenderDepthMap);
        _processThread.Start();

        //CameraRender();
    }
    
    private void InitializeKernelDictionary()
    {
        _computeShaderKernels = new Dictionary<string, int>();
        var kernelNames = new string[]
        {
            "ClearBakeCS",
            "GetIslandCS",
            "BakeHeightCS"
        };

        foreach(var kName in kernelNames)
            _computeShaderKernels.Add(kName, _heightGeneratorComputeShader.FindKernel(kName));
        
    }

    private void RenderDepthMap()
    {
        var step = _cameraDepth / 255.0f;


        ClearBake(_bakeTexture);

        for (var sliceInt = 254; sliceInt >= 0; sliceInt--)
        {

            if (!Application.isPlaying)
                return;

            _renderingFlag = true;
            _bakeCamera.nearClipPlane = sliceInt * step;
            _bakeCamera.farClipPlane = (sliceInt + 1) * step;
            _bakeCamera.Render();
                
            BakeHeightMap(1f - sliceInt / 255f);
        }

        /*
        for (var i = 0f; i < 1; i+= 0.25f)
        {
            GetIsland(_bakeTexture, _cameraRenderTexture, new float2(i, i + 0.25f));
            Mesh(_cameraRenderTexture);
        }
        */
    }

    private void Mesh(RenderTexture renderTexture)
    {
        var meshingTool = new MarchingCubesTool(
            Vector2.zero,
            new float3(_horizontalArea.x, _cameraDepth, _horizontalArea.y),
            new int3(30, 40, 30),
            transform,
            _meshMaterial);

        var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.R8, false);
        var prevActive = RenderTexture.active;
        RenderTexture.active = renderTexture;
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        RenderTexture.active = prevActive;

        var material = _bakeDebugMeshRenderer.material;
        material.SetTexture("_BaseMap", texture);
        meshingTool.FillWithTexture(texture);
        meshingTool.ComputeMesh();
    }

    private void BakeHeightMap(float normalizedHeight)
    {
        if(_bakeTexture.width != _cameraRenderTexture.width || _bakeTexture.height != _cameraRenderTexture.height)
        {
            Debug.LogError("Input and output texture size dont match");
            return;
        }

        var bakeKernel = _computeShaderKernels["BakeHeightCS"];
        
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
        var bakeKernel = _computeShaderKernels["ClearBakeCS"];

        /*
        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _heightGeneratorComputeShader,
            bakeKernel,
            targetTexture.width,
            targetTexture.height))
        {
            return;
        }
        */


        _heightGeneratorComputeShader.SetFloat("NormalizedBakeHeight", 0.1f);
        _heightGeneratorComputeShader.SetTexture(bakeKernel, "ResultTexture", targetTexture);

        _heightGeneratorComputeShader.Dispatch(
            bakeKernel,
            targetTexture.width / 4,
            targetTexture.height / 4,
            1);
    }

    private void GetIsland(RenderTexture inputTexture, RenderTexture targetTexture, float2 islandHeightRange)
    {
        var kernel = _computeShaderKernels["GetIslandCS"];

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

        //_meshingTool.DrawGizmo(Vector3.right * _horizontalArea.x + Vector3.up * _cameraDepth * 0.5f);
    }
}
