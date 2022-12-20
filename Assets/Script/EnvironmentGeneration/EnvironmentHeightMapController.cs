using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;


public class EnvironmentHeightMapController : BaseCameraBaker
{
    [SerializeField] private ComputeShader _imageProcessingComputeShader;
    [SerializeField] private Material _meshMaterial;
    [SerializeField] private Color _volumeColor;

    private Vector3 _originPosition;
    private Quaternion _originRotation;
    private Vector2 _horizontalArea;

    
    
    private void Awake()
    {
        _bakeCamera.enabled = false;
        //Initialize(Vector2.one * 200, 2, 0.01f, Vector3.zero, quaternion.identity);
    }

    private void Update()
    {
        //if (_loop == null)
          //  return;
        
    }

    public override void Initialize(Vector2 bakeArea, float texturePPU, float worldScale, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        base.Initialize(bakeArea, texturePPU, worldScale, centerWorldPosition, centerWorldRotation);
        _originPosition = centerWorldPosition;
        _originRotation = centerWorldRotation;
        _horizontalArea = worldScale * bakeArea;

        ProcessImage();
    }

    private async void ProcessImage()
    {
        await RenderDepthMap(_bakeTexture);
        //await Task.Delay(2000);
        var scaledTexture = ResizeRenderTexture(_bakeTexture, new Vector2Int(52, 52));
        ComputeContourProcess(scaledTexture);

    }

    /// <summary>
    /// Creates a new texture with the same size as the rt parameter
    /// Will NOT copy content
    /// can change the format
    /// </summary>
    /// <param name="rt"></param>
    /// <param name="enableRandomWrite"></param>
    /// <param name="cloneFormat"></param>
    /// <returns></returns>
    private RenderTexture CloneRenderTexureWithProperties(RenderTexture rt, bool enableRandomWrite, RenderTextureFormat cloneFormat)
    {
        RenderTexture clone;
        clone = new RenderTexture(rt.width, rt.height, 0, cloneFormat);
        clone.filterMode = rt.filterMode;
        clone.enableRandomWrite = enableRandomWrite;
        clone.Create();
        return clone;
    }

    private async Task RenderDepthMap(RenderTexture outputTexture)
    {
        //configure camera
        _bakeCamera.enabled = false;
        _bakeCamera.transform.position = _originPosition + _originRotation * (Vector3.up * _cameraDepth);
        _bakeCamera.nearClipPlane = 0.0f;
        _bakeCamera.farClipPlane = _cameraDepth;

        var cameraBufferRenderTexture = CloneRenderTexureWithProperties(outputTexture, true, RenderTextureFormat.Default);
        _bakeCamera.targetTexture = cameraBufferRenderTexture;

        var material = _bakeDebugMeshRenderer.material;
        material.SetTexture("_BaseMap", outputTexture);
        await Task.Yield();

        ClearTexture(outputTexture);
        ClearTexture(cameraBufferRenderTexture);

        await Task.Yield();

        var bakeKernel = _imageProcessingComputeShader.FindKernel("BakeHeightCS");

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _imageProcessingComputeShader,
            bakeKernel,
            cameraBufferRenderTexture.width,
            cameraBufferRenderTexture.height))
        {
            return;
        }

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _imageProcessingComputeShader,
            bakeKernel,
            outputTexture.width,
            outputTexture.height))
        {
            return;
        }

        var step = _cameraDepth / 255.0f;
        _imageProcessingComputeShader.SetTexture(bakeKernel, "InputTexture", cameraBufferRenderTexture);
        _imageProcessingComputeShader.SetTexture(bakeKernel, "ResultTexture", outputTexture);

        for (var sliceInt = 254; sliceInt >= 0; sliceInt--)
        {
            _bakeCamera.nearClipPlane = sliceInt * step;
            _bakeCamera.farClipPlane = (sliceInt + 1) * step;
            _bakeCamera.Render();


            _imageProcessingComputeShader.SetFloat("NormalizedBakeHeight", 1f - (float)sliceInt / 255f);
            _imageProcessingComputeShader.Dispatch(
                bakeKernel,
                outputTexture.width / 4,
                outputTexture.height / 4,
                1);
        }
        
        await Task.Yield();
        cameraBufferRenderTexture.Release();
        Destroy(cameraBufferRenderTexture);
    }

    private void ClearTexture(RenderTexture targetTexture)
    {
        var bakeKernel = _imageProcessingComputeShader.FindKernel("ClearBakeCS");

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _imageProcessingComputeShader,
            bakeKernel,
            targetTexture.width,
            targetTexture.height))
        {
            return;
        }


        _imageProcessingComputeShader.SetFloat("NormalizedBakeHeight", 0.1f);
        _imageProcessingComputeShader.SetTexture(bakeKernel, "ResultTexture", targetTexture);

        _imageProcessingComputeShader.Dispatch(
            bakeKernel,
            targetTexture.width / 4,
            targetTexture.height / 4,
            1);
    }

    private RenderTexture ResizeRenderTexture(RenderTexture source, Vector2Int newSize, bool debugTexture = true, bool destroySource = true)
    {
        var scaledRt = new RenderTexture(40, 40, 0, source.format);
        scaledRt.filterMode = FilterMode.Point;
        scaledRt.enableRandomWrite = true;
        Graphics.Blit(source, scaledRt);

        if (destroySource)
        {
            source.Release();
            Destroy(source);
        }

        if (debugTexture)
        {
            var material = _bakeDebugMeshRenderer.material;
            material.SetTexture("_BaseMap", scaledRt);
        }
        
        return scaledRt;
    }

    private void ComputeContourProcess(RenderTexture clusterTexture)
    {
        var kernel = _imageProcessingComputeShader.FindKernel("ExpandMaskCS");

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _imageProcessingComputeShader,
            kernel,
            clusterTexture.width,
            clusterTexture.height))
        {
            clusterTexture.Release();
            Destroy(clusterTexture);
            return;
        }
        
        _imageProcessingComputeShader.SetTexture(kernel, "ResultTexture", clusterTexture);
        _imageProcessingComputeShader.SetInt("MaskChannel", 0);

        _imageProcessingComputeShader.Dispatch(
            kernel, 
            clusterTexture.width / 4, 
            clusterTexture.height / 4, 
            1);

        AsyncGPUReadback.Request(
            clusterTexture,
            0,
            (req) =>
            {
                var textureData = req.GetData<byte>().ToArray();
                var imageSize = new Vector2Int(clusterTexture.width, clusterTexture.height);

                //TODO:add compute of looped land meshes here;
                var loopedLand = new LoopedLandMesh();
                loopedLand.UpdateProcessInfo(textureData, new Rect(), imageSize, _horizontalArea);
            });
        
    }

    private async Task ComputeContour(RenderTexture inputTexture, RenderTexture targetTexture)
    {
        if(inputTexture.width != targetTexture.width || inputTexture.height != targetTexture.height)
        {
            Debug.LogError("Sizes for textures dont match");
            return;
        }

        var kernel = _imageProcessingComputeShader.FindKernel("ContourDetectionCS");

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _imageProcessingComputeShader,
            kernel,
            inputTexture.width,
            inputTexture.height))
        {
            return;
        }

        if (!ComputeShaderUtilities.CheckComputeShaderTextureSize(
            _imageProcessingComputeShader,
            kernel,
            targetTexture.width,
            targetTexture.height))
        {
            return;
        }

        await Task.Yield();

        _imageProcessingComputeShader.SetTexture(kernel, "InputTexture", inputTexture);
        _imageProcessingComputeShader.SetTexture(kernel, "ResultTexture", targetTexture);

        _imageProcessingComputeShader.Dispatch(
            kernel,
            targetTexture.width / 4,
            targetTexture.height / 4,
            1);
    }

    private void OnDrawGizmos()
    {
        if (!_isInitialized)
            return;
        Gizmos.color = _volumeColor;
        Gizmos.matrix = Matrix4x4.TRS(_originPosition, _originRotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.up * 0.5f * _cameraDepth, new Vector3(_horizontalArea.x, _cameraDepth, _horizontalArea.y));
    }
}
