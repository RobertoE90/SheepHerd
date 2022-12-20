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
        ComputeContourProcess(scaledTexture, _horizontalArea);

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

    private void ComputeContourProcess(RenderTexture clusterTexture, Vector2 worldSpaceArea)
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
                var imageCoordNormalizer = new Vector2(1f / imageSize.x, 1f / imageSize.y);

                var points = new List<Vector2>();
                var pointsToIndexDic = new Dictionary<Vector2Int, int>();
                var edgesDic = new Dictionary<int, int2>();

                for (var y = -1; y <= imageSize.y; y++)
                {
                    for (var x = -1; x <= imageSize.x; x++)
                    {
                        var searchPos = new Vector2Int(x, y);
                        var meshSheetIndex = GetMaskFromSquare(textureData, searchPos, Vector2Int.zero, imageSize);
                        
                        var meshSheetList = _marchingSquaresMeshSheet[meshSheetIndex];
                        var indexConnectionList = new List<int>();
                        for (var i = 0; i < meshSheetList.Length; i += 2)
                        {
                            var pA = searchPos + _marchingSquaresSearchSheet[meshSheetList[i]];
                            var pB = searchPos + _marchingSquaresSearchSheet[meshSheetList[i + 1]];
            
                            var pointKey = new Vector2Int((int) (pA.x + pB.x), (int) (pA.y + pB.y));
                            if (!pointsToIndexDic.ContainsKey(pointKey))
                            {
                                pointsToIndexDic.Add(pointKey, points.Count);
                                points.Add(Vector2.Scale((pA + pB) * 0.5f, imageCoordNormalizer)); //the point dimensions are normalized to the image size
                            }

                            indexConnectionList.Add(pointsToIndexDic[pointKey]);
                        }

                        for (var i = 0; i < indexConnectionList.Count; i += 2)
                        {
                            var edgePointA = indexConnectionList[i];
                            var edgePointB = indexConnectionList[i + 1];
            
                            if (!edgesDic.ContainsKey(edgePointA))
                            {
                                edgesDic.Add(edgePointA, new int2(edgePointB, -1));
                            }
                            else
                            {
                                var edge = edgesDic[edgePointA];
                                edge.y = edgePointB;
                                edgesDic[edgePointA] = edge;
                            }
            
                            if (!edgesDic.ContainsKey(edgePointB))
                            {
                                edgesDic.Add(edgePointB, new int2(edgePointA, -1));
                            }
                            else
                            {
                                var edge = edgesDic[edgePointB];
                                edge.y = edgePointA;
                                edgesDic[edgePointB] = edge;
                            }
                        }
                    }
                }

                var resultPoints = new List<Vector3>();
                var addedHash = new HashSet<int>();
                var currentPointIndex = 0;
                
                AddPointToResultList(points[currentPointIndex]);

                while (resultPoints.Count != points.Count)
                {
                    if (!addedHash.Contains(edgesDic[currentPointIndex].x))
                        currentPointIndex = edgesDic[currentPointIndex].x;
                    else if(!addedHash.Contains(edgesDic[currentPointIndex].y))
                        currentPointIndex = edgesDic[currentPointIndex].y;
                    else
                    {
                        Debug.LogError("Both point edges are added");
                        break;
                    }

                    AddPointToResultList(points[currentPointIndex]);
                }

                void AddPointToResultList(Vector2 point)
                {
                    resultPoints.Add(new Vector3(point.x * worldSpaceArea.x, 0, point.y * worldSpaceArea.y));
                    addedHash.Add(currentPointIndex);
                }
                
                for (var i = 0; i < resultPoints.Count - 1; i++)
                {
                    Debug.DrawLine(resultPoints[i] * 5, resultPoints[(i + 1)] * 5, Color.green);
                }

                Debug.Break();
                /*
                _loop = loopPoints;
                
                StartCoroutine(TestLoop());
                IEnumerator TestLoop()
                {
                    while (_loop.Count > 30)
                    {
                        int minCurvatureIndex = 0;
                        float minCurvatureValue = float.MaxValue;
                        for (var i = 0; i < _loop.Count; i++)
                        {
                            var curvature = _loop[i].ComputeCurvature();
                            if (minCurvatureValue > curvature)
                            {
                                minCurvatureIndex = i;
                                minCurvatureValue = curvature;
                            }
                        }
                        
                        //RemovePointFromLoop(minCurvatureIndex, ref _loop);
                        
                        yield return new WaitForSeconds(1);
                    }
                }
                */

            });
        
    }

    private int GetMaskFromSquare(byte[] data, Vector2 squareZeroPos, Vector2Int imageRectOrigin,  Vector2Int imageSize)
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
    private void UpdateShapePerimeter(int meshSheetIndex, Vector2Int currentSquarePosition, ref List<Vector2> points, ref Dictionary<int, int2> edgesDic, ref Dictionary<Vector2Int, int> pointsToIndexDic)
    {
        
    }

    /*
    /// <summary>
    /// Will remove the point but maintain the loop
    /// </summary>
    private void RemovePointFromLoop(int pointIndex, ref List<Point2DLoop> loop)
    {
        var toRemovePoint = loop[pointIndex];
        var connA = toRemovePoint.EdgeA;
        var connB = toRemovePoint.EdgeB;
        
        RemoveConnectionFromPoint(connA, toRemovePoint);
        RemoveConnectionFromPoint(connB, toRemovePoint);
        
        ConnectPointIfAvailable(connA, connB);
        ConnectPointIfAvailable(connB, connA);
        
        loop.RemoveAt(pointIndex);
    }

    
    private void RemoveConnectionFromPoint(Point2DLoop origin, Point2DLoop connected)
    {
        if (origin.EdgeA == connected)
            origin.EdgeA = null;
        
        if (origin.EdgeB == connected)
            origin.EdgeB = null;
    }

    private void ConnectPointIfAvailable(Point2DLoop origin, Point2DLoop candidate)
    {
        if (origin.EdgeA == null)
        {
            origin.EdgeA = candidate;
            return;
        }

        origin.EdgeB ??= candidate;
    }
    */
    
    private byte SampleImageData(byte[] data, Vector2Int imageSize, Vector2 samplePoint, int imageChannels = 4, int channel = 0)
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
