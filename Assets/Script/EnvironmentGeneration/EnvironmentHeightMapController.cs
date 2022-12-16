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
        new int[]{},
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
        //await Task.Delay(2000);
        ComputeContourProcess(_bakeTexture, _horizontalArea);

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

    private void ComputeContourProcess(RenderTexture clusterTexture, Vector2 worldSpaceArea)
    {
        var scaledRt = new RenderTexture(50, 50, 0, clusterTexture.format);
        scaledRt.filterMode = FilterMode.Point;
        Graphics.Blit(clusterTexture, scaledRt);
   
        var material = _bakeDebugMeshRenderer.material;
        material.SetTexture("_BaseMap", scaledRt);

        AsyncGPUReadback.Request(
            scaledRt,
            0,
            (req) =>
            {

                var textureData = req.GetData<byte>();
                var imageSize = new Vector2Int(scaledRt.width, scaledRt.height);
                
                var points = new List<Vector2>();
                var edges = new List<int>();

                for (var y = -1; y <= imageSize.y; y++)
                {
                    for (var x = -1; x <= imageSize.x; x++) {

                        var searchPos = new Vector2Int(x, y);
                        var meshSheetIndex = GetMaskFromSquare(textureData, imageSize, searchPos);
                        UpdateShapePerimeter(meshSheetIndex, searchPos, ref points, ref edges);
                    }
                }

                for (var i = 0; i < edges.Count; i+=2)
                {
                    var a = new Vector3(points[edges[i]].x, 0, points[edges[i]].y);
                    var b = new Vector3(points[edges[i + 1]].x, 0, points[edges[i + 1]].y);
                    Debug.DrawLine(a + Vector3.right * 3, b + Vector3.right * 3, Color.white);
                }

                Debug.Break();
            });
    }

    private int GetMaskFromSquare(NativeArray<byte> data, Vector2Int imageSize, Vector2 squareZeroPos)
    {
        int mask = 0;
        for (var i = 0; i < _marchingSquaresSearchSheet.Length; i++)
        {
            var searchPos = squareZeroPos + _marchingSquaresSearchSheet[i];
            byte sample = SampleImageData(
                    data,
                    imageSize,
                    searchPos,
                    1);
            
            if (sample != 0)
                mask = mask | 1 << i;
        }

        return mask;
    }

    private void UpdateShapePerimeter(int meshSheetIndex, Vector2Int currentSquarePosition, ref List<Vector2> points, ref List<int> edges)
    {
        var meshSheetList = _marchingSquaresMeshSheet[meshSheetIndex];
        for (var i = 0; i < meshSheetList.Length; i += 2)
        {
            var pA = currentSquarePosition + _marchingSquaresSearchSheet[meshSheetList[i]];
            var pB = currentSquarePosition + _marchingSquaresSearchSheet[meshSheetList[i + 1]];
            //var pointKey = new Vector2Int((int)(pA.x + pB.x), (int)(pA.y + pB.y));
            edges.Add(points.Count);
            points.Add((pA + pB) * 0.5f);
        }
    }

    private byte SampleImageData(NativeArray<byte> data, Vector2Int imageSize, Vector2 samplePoint, int imageChannels = 4, int channel = 0)
    {
        if(samplePoint.x < 0 || samplePoint.x >= imageSize.x ||
            samplePoint.y < 0 || samplePoint.y >= imageSize.y)
            return 0;

        var index = ((int)samplePoint.x + (int)samplePoint.y * (int)imageSize.x) * imageChannels + channel;
        
        if(index >= data.Length)
            return 0;
        
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
