using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

public class LoopedLandMesh
{
    private Thread _meshingThread;
    
    //process info
    private byte[] _textureData;
    private Rect _region;
    private Vector2Int _texturePixelSize;
    private Vector2 _worldSpaceArea;

    public LoopedLandMesh()
    {
        _meshingThread = new Thread(ComputeHorizontalLoop);
    }

    public void UpdateProcessInfo(byte[] textureData, Rect processRegion, Vector2Int textureSize, Vector2 worldSpaceArea)
    {
        _textureData = textureData;
        _region = processRegion;
        _texturePixelSize = textureSize;
        _worldSpaceArea = worldSpaceArea;

        _meshingThread.Start();
    }

    private void ComputeHorizontalLoop()
    {
        var imageCoordNormalizer = new Vector2(1f / _texturePixelSize.x, 1f / _texturePixelSize.y);

        var points = new List<Vector2>();
        var pointsToIndexDic = new Dictionary<Vector2Int, int>();
        var edgesDic = new Dictionary<int, int2>();

        for (var y = -1; y <= _texturePixelSize.y; y++)
        {
            for (var x = -1; x <= _texturePixelSize.x; x++)
            {
                var searchPos = new Vector2Int(x, y);
                var meshSheetIndex = GetMaskFromSquare(_textureData, searchPos, Vector2Int.zero, _texturePixelSize);

                var meshSheetList = MeshingUtility.MarchingSquaresMeshSheedAt(meshSheetIndex);
                var indexConnectionList = new List<int>();
                for (var i = 0; i < meshSheetList.Length; i += 2)
                {
                    var pA = searchPos + MeshingUtility.MarchingSquaresSearchSheedAt(meshSheetList[i]);
                    var pB = searchPos + MeshingUtility.MarchingSquaresSearchSheedAt(meshSheetList[i + 1]);

                    var pointKey = new Vector2Int((int)(pA.x + pB.x), (int)(pA.y + pB.y));
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
            else if (!addedHash.Contains(edgesDic[currentPointIndex].y))
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
            resultPoints.Add(new Vector3(point.x * _worldSpaceArea.x, 0, point.y * _worldSpaceArea.y));
            addedHash.Add(currentPointIndex);
        }

        for (var i = 0; i < resultPoints.Count - 1; i++)
        {
            Debug.DrawLine(resultPoints[i] * 5, resultPoints[(i + 1)] * 5, Color.green, 100);
        }

        Debug.Log("done");
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
    }

    private int GetMaskFromSquare(byte[] data, Vector2 squareZeroPos, Vector2Int imageRectOrigin, Vector2Int imageSize)
    {
        int mask = 0;
        for (var i = 0; i < MeshingUtility.GetMarchingSquaresSearchItemCount(); i++)
        {
            var searchPos = imageRectOrigin + squareZeroPos + MeshingUtility.MarchingSquaresSearchSheedAt(i);
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

    private byte SampleImageData(byte[] data, Vector2Int imageSize, Vector2 samplePoint, int imageChannels = 4, int channel = 0)
    {
        if (samplePoint.x < 0 || samplePoint.x >= imageSize.x ||
            samplePoint.y < 0 || samplePoint.y >= imageSize.y)
            return 0;

        var index = ((int)samplePoint.x + (int)samplePoint.y * (int)imageSize.x) * imageChannels + channel;

        if (index >= data.Length)
            return 0;

        return data[index];
    }

}
