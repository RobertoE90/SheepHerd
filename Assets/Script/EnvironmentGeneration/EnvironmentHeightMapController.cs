using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class EnvironmentHeightMapController : BaseCameraBaker
{
    [SerializeField] private ComputeShader _imageProcessingComputeShader;
    [SerializeField] private Material _meshMaterial;
    [SerializeField] private Color _volumeColor;

    private Vector3 _originPosition;
    private Quaternion _originRotation;
    private Vector2 _horizontalArea;

    private Vector2[] _marchingSquaresSearchSheet = new Vector2[]{
        Vector2.zero,
        new Vector2(1, 0),
        new Vector2(1, 1),
        new Vector2(0, 1)
    };

    private List<int[]> _marchingSquaresMeshSheet = new List<int[]>{
        new int[]{ },
        new int[]{ 0, 1, 0, 3},
        new int[]{ 0, 1, 1, 2},
        new int[]{ 0, 3, 1, 2},
        new int[]{ 1, 2, 2, 3},
        new int[]{}, //dont add on this case
        new int[]{ 1, 0, 2, 3},
        new int[]{ 2, 3, 0, 3},
        new int[]{ 2, 3, 0, 3},
        new int[]{ 0, 1, 2, 3},
        new int[]{}, //dont add on this case 
        new int[]{ 1, 2, 2, 3},
        new int[]{ 0, 3, 1, 2},
        new int[]{ 0, 1, 1, 2},
        new int[]{ 0, 1, 0, 3},
        new int[]{},
    };

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

        ProcessImage();
    }

    private async void ProcessImage()
    {
        await RenderDepthMap(_bakeTexture);
        await Task.Delay(2000);

        ComputeContourProcess(_bakeTexture, _horizontalArea, 15);

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

    private void ComputeContourProcess(RenderTexture cluterTexture, Vector2 worldSpaceArea, int resolution = 50)
    {
        AsyncGPUReadback.Request(
            cluterTexture,
            0,
            (req) =>
            {
                if(resolution < 0 || resolution >= cluterTexture.width || resolution >= cluterTexture.height)
                {
                    Debug.LogError($"Invalid resolution {resolution} for size {cluterTexture.width}: {cluterTexture.height}");
                    return;
                }

                var textureData = req.GetData<byte>();

                var imageSize = new Vector2Int(cluterTexture.width, cluterTexture.height);
                var steps = new Vector2Int(
                    (int)(imageSize.x / resolution), (int)(imageSize.y / resolution));

                for(var y = 0; y < resolution; y++)
                {
                    for(var x = 0; x < resolution; x++)
                    {
                        int mask = 0;
                        var originPos = new Vector2(x, y);
                        
                        for (var i = 0; i < _marchingSquaresSearchSheet.Length; i++)
                        {
                            
                            var searchPos = originPos + _marchingSquaresSearchSheet[i];
                            byte sample = 0;
                            if (
                            searchPos.x != 0 && searchPos.x != resolution - 1 && 
                            searchPos.y != 0 && searchPos.y != resolution - 1)
                            {
                                sample = SampleImageData(
                                    textureData,
                                    imageSize,
                                    Vector2.Scale(searchPos, steps),
                                    1);
                            }

                            if (sample != 0)
                                mask = mask | 1 << i;

                            var test = sample != 0;

                        }

                        try
                        {
                            var meshSheetList = _marchingSquaresMeshSheet[mask];
                            var points = new List<Vector3>();
                            for(var i = 0; i < meshSheetList.Length; i += 2)
                            {
                                var pA = originPos + _marchingSquaresSearchSheet[meshSheetList[i]];
                                var pB = originPos + _marchingSquaresSearchSheet[meshSheetList[i + 1]];

                                points.Add(new Vector3(pA.x + pB.x, 0, pA.y + pB.y) * 0.5f);
                            }

                            for(var i = 0; i < points.Count - 1; i++)
                                Debug.DrawLine(points[i] + Vector3.right * 2, points[i + 1] + Vector3.right * 2, Color.white);
                            
                        }
                        catch(Exception e) { }

                        
                    }
                }
                Debug.Break();
            });
    }

    private byte SampleImageData(NativeArray<byte> data, Vector2Int imageSize, Vector2 samplePoint, int imageChannels = 4, int channel = 0)
    {
        if(samplePoint.x < 0 || samplePoint.x >= imageSize.x ||
            samplePoint.y < 0 || samplePoint.y >= imageSize.y)
        {
            Debug.LogError("Sampling outside the image");
            return 0;
        }

        var index = ((int)samplePoint.x + (int)samplePoint.y * (int)imageSize.x) * imageChannels + channel;
        
        if(index >= data.Length)
        {
            Debug.LogError($"Sampling outside the image with index {index} : {data.Length}");
            return byte.MaxValue;
        }

        return data[index];
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
