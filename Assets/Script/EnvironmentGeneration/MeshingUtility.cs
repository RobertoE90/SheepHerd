using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MeshingUtility
{
    private static Vector2[] _marchingSquaresSearchSheet = new Vector2[]{
        Vector2.zero,
        new Vector2(1, 0),
        new Vector2(1, 1),
        new Vector2(0, 1)
    };

    public static int GetMarchingSquaresSearchItemCount()
    {
        return _marchingSquaresSearchSheet.Length;
    }

    public static Vector2 MarchingSquaresSearchSheedAt(int index)
    {
        return _marchingSquaresSearchSheet[index];
    }

    private static List<int[]> _marchingSquaresMeshSheet = new List<int[]>{
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


    public static int[] MarchingSquaresMeshSheedAt(int index)
    {
        return _marchingSquaresMeshSheet[index];
    }
}
