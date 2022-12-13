using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

public class MarchingCubesTool
{
    private int3 _axisResolution;
    private float3 _volumeDimensions;
    private VolumePoint[,,] _volumePoints; //contains an array with 3d index access to the points that are in the volume
    private List<Vector3> _meshPoints;
    private Mesh _generatedMesh;

    #region CheetTables
    private int3[] _cubeLooperCheet =
    {
        new int3(0, 0, 0),
        new int3(1, 0, 0),
        new int3(1, 0, 1),
        new int3(0, 0, 1),
        new int3(0, 1, 0),
        new int3(1, 1, 0),
        new int3(1, 1, 1),
        new int3(0, 1, 1),
    };

    //relates the cube axis middle point to the cube looper index for relative position finding
    //this tables relates the _cubeLooper table with the _triangulationTable items
    private int[,] _cubeMiddlePointIndexToCubeLooper =
    {
        { 0, 1},
        { 1, 2},
        { 2, 3},
        { 3, 0},
        { 4, 5},
        { 5, 6},
        { 6, 7},
        { 7, 4},
        { 0, 4},
        { 5, 1},
        { 6, 2},
        { 7, 3}
    };

    private List<int[]> _triangulationTable = new List<int[]> { 
        new int [] {},
        new int [] {0, 8, 3},
        new int [] {0, 1, 9},
        new int [] {1, 8, 3, 9, 8, 1},
        new int [] {1, 2, 10,},
        new int [] {0, 8, 3, 1, 2, 10},
        new int [] {9, 2, 10, 0, 2, 9},
        new int [] {2, 8, 3, 2, 10, 8, 10, 9, 8},
        new int [] {3, 11, 2},
        new int [] {0, 11, 2, 8, 11, 0},
        new int [] {1, 9, 0, 2, 3, 11},
        new int [] {1, 11, 2, 1, 9, 11, 9, 8, 11},
        new int [] {3, 10, 1, 11, 10, 3},
        new int [] {0, 10, 1, 0, 8, 10, 8, 11, 10},
        new int [] {3, 9, 0, 3, 11, 9, 11, 10, 9},
        new int [] {9, 8, 10, 10, 8, 11},
        new int [] {4, 7, 8},
        new int [] {4, 3, 0, 7, 3, 4},
        new int [] {0, 1, 9, 8, 4, 7},
        new int [] {4, 1, 9, 4, 7, 1, 7, 3, 1},
        new int [] {1, 2, 10, 8, 4, 7},
        new int [] {3, 4, 7, 3, 0, 4, 1, 2, 10},
        new int [] {9, 2, 10, 9, 0, 2, 8, 4, 7},
        new int [] {2, 10, 9, 2, 9, 7, 2, 7, 3, 7, 9, 4},
        new int [] {8, 4, 7, 3, 11, 2},
        new int [] {11, 4, 7, 11, 2, 4, 2, 0, 4},
        new int [] {9, 0, 1, 8, 4, 7, 2, 3, 11},
        new int [] {4, 7, 11, 9, 4, 11, 9, 11, 2, 9, 2, 1},
        new int [] {3, 10, 1, 3, 11, 10, 7, 8, 4},
        new int [] {1, 11, 10, 1, 4, 11, 1, 0, 4, 7, 11, 4},
        new int [] {4, 7, 8, 9, 0, 11, 9, 11, 10, 11, 0, 3},
        new int [] {4, 7, 11, 4, 11, 9, 9, 11, 10},
        new int [] {9, 5, 4},
        new int [] {9, 5, 4, 0, 8, 3},
        new int [] {0, 5, 4, 1, 5, 0},
        new int [] {8, 5, 4, 8, 3, 5, 3, 1, 5},
        new int [] {1, 2, 10, 9, 5, 4},
        new int [] {3, 0, 8, 1, 2, 10, 4, 9, 5},
        new int [] {5, 2, 10, 5, 4, 2, 4, 0, 2},
        new int [] {2, 10, 5, 3, 2, 5, 3, 5, 4, 3, 4, 8},
        new int [] {9, 5, 4, 2, 3, 11},
        new int [] {0, 11, 2, 0, 8, 11, 4, 9, 5},
        new int [] {0, 5, 4, 0, 1, 5, 2, 3, 11},
        new int [] {2, 1, 5, 2, 5, 8, 2, 8, 11, 4, 8, 5},
        new int [] {10, 3, 11, 10, 1, 3, 9, 5, 4},
        new int [] {4, 9, 5, 0, 8, 1, 8, 10, 1, 8, 11, 10},
        new int [] {5, 4, 0, 5, 0, 11, 5, 11, 10, 11, 0, 3},
        new int [] {5, 4, 8, 5, 8, 10, 10, 8, 11},
        new int [] {9, 7, 8, 5, 7, 9},
        new int [] {9, 3, 0, 9, 5, 3, 5, 7, 3},
        new int [] {0, 7, 8, 0, 1, 7, 1, 5, 7},
        new int [] {1, 5, 3, 3, 5, 7},
        new int [] {9, 7, 8, 9, 5, 7, 10, 1, 2},
        new int [] {10, 1, 2, 9, 5, 0, 5, 3, 0, 5, 7, 3},
        new int [] {8, 0, 2, 8, 2, 5, 8, 5, 7, 10, 5, 2},
        new int [] {2, 10, 5, 2, 5, 3, 3, 5, 7},
        new int [] {7, 9, 5, 7, 8, 9, 3, 11, 2},
        new int [] {9, 5, 7, 9, 7, 2, 9, 2, 0, 2, 7, 11},
        new int [] {2, 3, 11, 0, 1, 8, 1, 7, 8, 1, 5, 7},
        new int [] {11, 2, 1, 11, 1, 7, 7, 1, 5},
        new int [] {9, 5, 8, 8, 5, 7, 10, 1, 3, 10, 3, 11},
        new int [] {5, 7, 0, 5, 0, 9, 7, 11, 0, 1, 0, 10, 11, 10, 0,},
        new int [] {11, 10, 0, 11, 0, 3, 10, 5, 0, 8, 0, 7, 5, 7, 0,},
        new int [] {11, 10, 5, 7, 11, 5},
        new int [] {10, 6, 5},
        new int [] {0, 8, 3, 5, 10, 6},
        new int [] {9, 0, 1, 5, 10, 6},
        new int [] {1, 8, 3, 1, 9, 8, 5, 10, 6},
        new int [] {1, 6, 5, 2, 6, 1},
        new int [] {1, 6, 5, 1, 2, 6, 3, 0, 8},
        new int [] {9, 6, 5, 9, 0, 6, 0, 2, 6},
        new int [] {5, 9, 8, 5, 8, 2, 5, 2, 6, 3, 2, 8},
        new int [] {2, 3, 11, 10, 6, 5},
        new int [] {11, 0, 8, 11, 2, 0, 10, 6, 5},
        new int [] {0, 1, 9, 2, 3, 11, 5, 10, 6},
        new int [] {5, 10, 6, 1, 9, 2, 9, 11, 2, 9, 8, 11},
        new int [] {6, 3, 11, 6, 5, 3, 5, 1, 3},
        new int [] {0, 8, 11, 0, 11, 5, 0, 5, 1, 5, 11, 6},
        new int [] {3, 11, 6, 0, 3, 6, 0, 6, 5, 0, 5, 9},
        new int [] {6, 5, 9, 6, 9, 11, 11, 9, 8},
        new int [] {5, 10, 6, 4, 7, 8},
        new int [] {4, 3, 0, 4, 7, 3, 6, 5, 10},
        new int [] {1, 9, 0, 5, 10, 6, 8, 4, 7},
        new int [] {10, 6, 5, 1, 9, 7, 1, 7, 3, 7, 9, 4},
        new int [] {6, 1, 2, 6, 5, 1, 4, 7, 8},
        new int [] {1, 2, 5, 5, 2, 6, 3, 0, 4, 3, 4, 7},
        new int [] {8, 4, 7, 9, 0, 5, 0, 6, 5, 0, 2, 6},
        new int [] {7, 3, 9, 7, 9, 4, 3, 2, 9, 5, 9, 6, 2, 6, 9,},
        new int [] {3, 11, 2, 7, 8, 4, 10, 6, 5},
        new int [] {5, 10, 6, 4, 7, 2, 4, 2, 0, 2, 7, 11},
        new int [] {0, 1, 9, 4, 7, 8, 2, 3, 11, 5, 10, 6},
        new int [] {9, 2, 1, 9, 11, 2, 9, 4, 11, 7, 11, 4, 5, 10, 6,},
        new int [] {8, 4, 7, 3, 11, 5, 3, 5, 1, 5, 11, 6},
        new int [] {5, 1, 11, 5, 11, 6, 1, 0, 11, 7, 11, 4, 0, 4, 11,},
        new int [] {0, 5, 9, 0, 6, 5, 0, 3, 6, 11, 6, 3, 8, 4, 7,},
        new int [] {6, 5, 9, 6, 9, 11, 4, 7, 9, 7, 11, 9},
        new int [] {10, 4, 9, 6, 4, 10},
        new int [] {4, 10, 6, 4, 9, 10, 0, 8, 3},
        new int [] {10, 0, 1, 10, 6, 0, 6, 4, 0},
        new int [] {8, 3, 1, 8, 1, 6, 8, 6, 4, 6, 1, 10},
        new int [] {1, 4, 9, 1, 2, 4, 2, 6, 4},
        new int [] {3, 0, 8, 1, 2, 9, 2, 4, 9, 2, 6, 4},
        new int [] {0, 2, 4, 4, 2, 6},
        new int [] {8, 3, 2, 8, 2, 4, 4, 2, 6},
        new int [] {10, 4, 9, 10, 6, 4, 11, 2, 3},
        new int [] {0, 8, 2, 2, 8, 11, 4, 9, 10, 4, 10, 6},
        new int [] {3, 11, 2, 0, 1, 6, 0, 6, 4, 6, 1, 10},
        new int [] {6, 4, 1, 6, 1, 10, 4, 8, 1, 2, 1, 11, 8, 11, 1,},
        new int [] {9, 6, 4, 9, 3, 6, 9, 1, 3, 11, 6, 3},
        new int [] {8, 11, 1, 8, 1, 0, 11, 6, 1, 9, 1, 4, 6, 4, 1,},
        new int [] {3, 11, 6, 3, 6, 0, 0, 6, 4},
        new int [] {6, 4, 8, 11, 6, 8},
        new int [] {7, 10, 6, 7, 8, 10, 8, 9, 10},
        new int [] {0, 7, 3, 0, 10, 7, 0, 9, 10, 6, 7, 10},
        new int [] {10, 6, 7, 1, 10, 7, 1, 7, 8, 1, 8, 0},
        new int [] {10, 6, 7, 10, 7, 1, 1, 7, 3},
        new int [] {1, 2, 6, 1, 6, 8, 1, 8, 9, 8, 6, 7},
        new int [] {2, 6, 9, 2, 9, 1, 6, 7, 9, 0, 9, 3, 7, 3, 9,},
        new int [] {7, 8, 0, 7, 0, 6, 6, 0, 2},
        new int [] {7, 3, 2, 6, 7, 2},
        new int [] {2, 3, 11, 10, 6, 8, 10, 8, 9, 8, 6, 7},
        new int [] {2, 0, 7, 2, 7, 11, 0, 9, 7, 6, 7, 10, 9, 10, 7,},
        new int [] {1, 8, 0, 1, 7, 8, 1, 10, 7, 6, 7, 10, 2, 3, 11,},
        new int [] {11, 2, 1, 11, 1, 7, 10, 6, 1, 6, 7, 1},
        new int [] {8, 9, 6, 8, 6, 7, 9, 1, 6, 11, 6, 3, 1, 3, 6,},
        new int [] {0, 9, 1, 11, 6, 7},
        new int [] {7, 8, 0, 7, 0, 6, 3, 11, 0, 11, 6, 0},
        new int [] {7, 11, 6},
        new int [] {7, 6, 11},
        new int [] {3, 0, 8, 11, 7, 6},
        new int [] {0, 1, 9, 11, 7, 6},
        new int [] {8, 1, 9, 8, 3, 1, 11, 7, 6},
        new int [] {10, 1, 2, 6, 11, 7},
        new int [] {1, 2, 10, 3, 0, 8, 6, 11, 7},
        new int [] {2, 9, 0, 2, 10, 9, 6, 11, 7},
        new int [] {6, 11, 7, 2, 10, 3, 10, 8, 3, 10, 9, 8},
        new int [] {7, 2, 3, 6, 2, 7},
        new int [] {7, 0, 8, 7, 6, 0, 6, 2, 0},
        new int [] {2, 7, 6, 2, 3, 7, 0, 1, 9},
        new int [] {1, 6, 2, 1, 8, 6, 1, 9, 8, 8, 7, 6},
        new int [] {10, 7, 6, 10, 1, 7, 1, 3, 7},
        new int [] {10, 7, 6, 1, 7, 10, 1, 8, 7, 1, 0, 8},
        new int [] {0, 3, 7, 0, 7, 10, 0, 10, 9, 6, 10, 7},
        new int [] {7, 6, 10, 7, 10, 8, 8, 10, 9},
        new int [] {6, 8, 4, 11, 8, 6},
        new int [] {3, 6, 11, 3, 0, 6, 0, 4, 6},
        new int [] {8, 6, 11, 8, 4, 6, 9, 0, 1},
        new int [] {9, 4, 6, 9, 6, 3, 9, 3, 1, 11, 3, 6},
        new int [] {6, 8, 4, 6, 11, 8, 2, 10, 1},
        new int [] {1, 2, 10, 3, 0, 11, 0, 6, 11, 0, 4, 6},
        new int [] {4, 11, 8, 4, 6, 11, 0, 2, 9, 2, 10, 9},
        new int [] {10, 9, 3, 10, 3, 2, 9, 4, 3, 11, 3, 6, 4, 6, 3,},
        new int [] {8, 2, 3, 8, 4, 2, 4, 6, 2},
        new int [] {0, 4, 2, 4, 6, 2},
        new int [] {1, 9, 0, 2, 3, 4, 2, 4, 6, 4, 3, 8},
        new int [] {1, 9, 4, 1, 4, 2, 2, 4, 6},
        new int [] {8, 1, 3, 8, 6, 1, 8, 4, 6, 6, 10, 1},
        new int [] {10, 1, 0, 10, 0, 6, 6, 0, 4},
        new int [] {4, 6, 3, 4, 3, 8, 6, 10, 3, 0, 3, 9, 10, 9, 3,},
        new int [] {10, 9, 4, 6, 10, 4},
        new int [] {4, 9, 5, 7, 6, 11},
        new int [] {0, 8, 3, 4, 9, 5, 11, 7, 6},
        new int [] {5, 0, 1, 5, 4, 0, 7, 6, 11},
        new int [] {11, 7, 6, 8, 3, 4, 3, 5, 4, 3, 1, 5},
        new int [] {9, 5, 4, 10, 1, 2, 7, 6, 11},
        new int [] {6, 11, 7, 1, 2, 10, 0, 8, 3, 4, 9, 5},
        new int [] {7, 6, 11, 5, 4, 10, 4, 2, 10, 4, 0, 2},
        new int [] {3, 4, 8, 3, 5, 4, 3, 2, 5, 10, 5, 2, 11, 7, 6,},
        new int [] {7, 2, 3, 7, 6, 2, 5, 4, 9},
        new int [] {9, 5, 4, 0, 8, 6, 0, 6, 2, 6, 8, 7},
        new int [] {3, 6, 2, 3, 7, 6, 1, 5, 0, 5, 4, 0},
        new int [] {6, 2, 8, 6, 8, 7, 2, 1, 8, 4, 8, 5, 1, 5, 8,},
        new int [] {9, 5, 4, 10, 1, 6, 1, 7, 6, 1, 3, 7},
        new int [] {1, 6, 10, 1, 7, 6, 1, 0, 7, 8, 7, 0, 9, 5, 4,},
        new int [] {4, 0, 10, 4, 10, 5, 0, 3, 10, 6, 10, 7, 3, 7, 10,},
        new int [] {7, 6, 10, 7, 10, 8, 5, 4, 10, 4, 8, 10},
        new int [] {6, 9, 5, 6, 11, 9, 11, 8, 9},
        new int [] {3, 6, 11, 0, 6, 3, 0, 5, 6, 0, 9, 5},
        new int [] {0, 11, 8, 0, 5, 11, 0, 1, 5, 5, 6, 11},
        new int [] {6, 11, 3, 6, 3, 5, 5, 3, 1},
        new int [] {1, 2, 10, 9, 5, 11, 9, 11, 8, 11, 5, 6},
        new int [] {0, 11, 3, 0, 6, 11, 0, 9, 6, 5, 6, 9, 1, 2, 10,},
        new int [] {11, 8, 5, 11, 5, 6, 8, 0, 5, 10, 5, 2, 0, 2, 5,},
        new int [] {6, 11, 3, 6, 3, 5, 2, 10, 3, 10, 5, 3},
        new int [] {5, 8, 9, 5, 2, 8, 5, 6, 2, 3, 8, 2},
        new int [] {9, 5, 6, 9, 6, 0, 0, 6, 2},
        new int [] {1, 5, 8, 1, 8, 0, 5, 6, 8, 3, 8, 2, 6, 2, 8,},
        new int [] {1, 5, 6, 2, 1, 6},
        new int [] {1, 3, 6, 1, 6, 10, 3, 8, 6, 5, 6, 9, 8, 9, 6,},
        new int [] {10, 1, 0, 10, 0, 6, 9, 5, 0, 5, 6, 0},
        new int [] {0, 3, 8, 5, 6, 10},
        new int [] {10, 5, 6},
        new int [] {11, 5, 10, 7, 5, 11},
        new int [] {11, 5, 10, 11, 7, 5, 8, 3, 0},
        new int [] {5, 11, 7, 5, 10, 11, 1, 9, 0},
        new int [] {10, 7, 5, 10, 11, 7, 9, 8, 1, 8, 3, 1},
        new int [] {11, 1, 2, 11, 7, 1, 7, 5, 1},
        new int [] {0, 8, 3, 1, 2, 7, 1, 7, 5, 7, 2, 11},
        new int [] {9, 7, 5, 9, 2, 7, 9, 0, 2, 2, 11, 7},
        new int [] {7, 5, 2, 7, 2, 11, 5, 9, 2, 3, 2, 8, 9, 8, 2,},
        new int [] {2, 5, 10, 2, 3, 5, 3, 7, 5},
        new int [] {8, 2, 0, 8, 5, 2, 8, 7, 5, 10, 2, 5},
        new int [] {9, 0, 1, 5, 10, 3, 5, 3, 7, 3, 10, 2},
        new int [] {9, 8, 2, 9, 2, 1, 8, 7, 2, 10, 2, 5, 7, 5, 2,},
        new int [] {1, 3, 5, 3, 7, 5},
        new int [] {0, 8, 7, 0, 7, 1, 1, 7, 5},
        new int [] {9, 0, 3, 9, 3, 5, 5, 3, 7},
        new int [] {9, 8, 7, 5, 9, 7},
        new int [] {5, 8, 4, 5, 10, 8, 10, 11, 8},
        new int [] {5, 0, 4, 5, 11, 0, 5, 10, 11, 11, 3, 0},
        new int [] {0, 1, 9, 8, 4, 10, 8, 10, 11, 10, 4, 5},
        new int [] {10, 11, 4, 10, 4, 5, 11, 3, 4, 9, 4, 1, 3, 1, 4,},
        new int [] {2, 5, 1, 2, 8, 5, 2, 11, 8, 4, 5, 8},
        new int [] {0, 4, 11, 0, 11, 3, 4, 5, 11, 2, 11, 1, 5, 1, 11,},
        new int [] {0, 2, 5, 0, 5, 9, 2, 11, 5, 4, 5, 8, 11, 8, 5,},
        new int [] {9, 4, 5, 2, 11, 3},
        new int [] {2, 5, 10, 3, 5, 2, 3, 4, 5, 3, 8, 4},
        new int [] {5, 10, 2, 5, 2, 4, 4, 2, 0},
        new int [] {3, 10, 2, 3, 5, 10, 3, 8, 5, 4, 5, 8, 0, 1, 9,},
        new int [] {5, 10, 2, 5, 2, 4, 1, 9, 2, 9, 4, 2},
        new int [] {8, 4, 5, 8, 5, 3, 3, 5, 1},
        new int [] {0, 4, 5, 1, 0, 5},
        new int [] {8, 4, 5, 8, 5, 3, 9, 0, 5, 0, 3, 5},
        new int [] {9, 4, 5},
        new int [] {4, 11, 7, 4, 9, 11, 9, 10, 11},
        new int [] {0, 8, 3, 4, 9, 7, 9, 11, 7, 9, 10, 11},
        new int [] {1, 10, 11, 1, 11, 4, 1, 4, 0, 7, 4, 11},
        new int [] {3, 1, 4, 3, 4, 8, 1, 10, 4, 7, 4, 11, 10, 11, 4,},
        new int [] {4, 11, 7, 9, 11, 4, 9, 2, 11, 9, 1, 2},
        new int [] {9, 7, 4, 9, 11, 7, 9, 1, 11, 2, 11, 1, 0, 8, 3,},
        new int [] {11, 7, 4, 11, 4, 2, 2, 4, 0},
        new int [] {11, 7, 4, 11, 4, 2, 8, 3, 4, 3, 2, 4},
        new int [] {2, 9, 10, 2, 7, 9, 2, 3, 7, 7, 4, 9},
        new int [] {9, 10, 7, 9, 7, 4, 10, 2, 7, 8, 7, 0, 2, 0, 7,},
        new int [] {3, 7, 10, 3, 10, 2, 7, 4, 10, 1, 10, 0, 4, 0, 10,},
        new int [] {1, 10, 2, 8, 7, 4},
        new int [] {4, 9, 1, 4, 1, 7, 7, 1, 3},
        new int [] {4, 9, 1, 4, 1, 7, 0, 8, 1, 8, 7, 1},
        new int [] {4, 0, 3, 7, 4, 3},
        new int [] {4, 8, 7},
        new int [] {9, 10, 8, 10, 11, 8},
        new int [] {3, 0, 9, 3, 9, 11, 11, 9, 10},
        new int [] {0, 1, 10, 0, 10, 8, 8, 10, 11},
        new int [] {3, 1, 10, 11, 3, 10},
        new int [] {1, 2, 11, 1, 11, 9, 9, 11, 8},
        new int [] {3, 0, 9, 3, 9, 11, 1, 2, 9, 2, 11, 9},
        new int [] {0, 2, 11, 8, 0, 11},
        new int [] {3, 2, 11},
        new int [] {2, 3, 8, 2, 8, 10, 10, 8, 9},
        new int [] {9, 10, 2, 0, 9, 2},
        new int [] {2, 3, 8, 2, 8, 10, 0, 1, 8, 1, 10, 8},
        new int [] {1, 10, 2},
        new int [] {1, 3, 8, 9, 1, 8},
        new int [] {0, 9, 1},
        new int [] {0, 3, 8},
        new int [] {}
    };
    #endregion
    
    public MarchingCubesTool(Vector2 center, Vector3 volumeDimensions, int3 axisResolution, Transform meshParent, Material meshMaterial)
    {
        _axisResolution = axisResolution;
        _volumeDimensions = volumeDimensions;
        _volumePoints = new VolumePoint[axisResolution.x, axisResolution.y, axisResolution.z];
        _meshPoints = new List<Vector3>();
        _generatedMesh = new Mesh();

        var meshGo = new GameObject("Martching Cubes Mesh");
        meshGo.transform.SetParent(meshParent);
        
        meshGo.transform.localPosition = Vector3.right * center.x + Vector3.forward * center.y + Vector3.up * 0.5f * volumeDimensions.y;
        meshGo.transform.localPosition += Vector3.right * 2f;

        meshGo.AddComponent<MeshFilter>().mesh = _generatedMesh;
        meshGo.AddComponent<MeshRenderer>().material = meshMaterial;
    }

    public void FillWithTexture(Texture2D fillTexture) {
        Color[] colorData = fillTexture.GetPixels();

        var xSpace = _volumeDimensions.x / (_axisResolution.x - 1);
        var ySpace = _volumeDimensions.y / (_axisResolution.y - 1);
        var zSpace = _volumeDimensions.z / (_axisResolution.z - 1);

        var widthAspect = (float)fillTexture.width / _axisResolution.x;
        var heightAspect = (float)fillTexture.height / _axisResolution.z;
        for (var y = 0; y < _axisResolution.y; y++)
        {
            for (var z = 0; z < _axisResolution.z; z++)
            {
                for (var x = 0; x < _axisResolution.x; x++)
                {
                    var readUv = new int2((int)(widthAspect * x), (int)(heightAspect * z));
                    var colorAtUv = colorData[readUv.x + readUv.y * fillTexture.width];
        
                    var insideVolume = colorAtUv.r > ((float)y / _axisResolution.y);

                    _volumePoints[x, y, z] = new VolumePoint
                    {
                        InsideVolume = insideVolume,
                        PointLocalPosition = new float3(
                            x * xSpace - _volumeDimensions.x * 0.5f,
                            y * ySpace - _volumeDimensions.y * 0.5f,
                            z * zSpace - _volumeDimensions.z * 0.5f)
                    };
                }
            }
        }
    }

    public void ComputeMesh()
    {
        for (var y = 0; y < _axisResolution.y - 1; y++)
        {
            for (var z = 0; z < _axisResolution.z - 1; z++)
            {
                for (var x = 0; x < _axisResolution.x - 1; x++)
                {
                    var index = MarchCube(new int3(x, y, z));
                    GenerateCubeMesh(new int3(x, y, z), index);
                }
            }
        }

        _generatedMesh.Clear();
        _generatedMesh.SetVertices(_meshPoints);
        var triangles = new int[_meshPoints.Count];
        for(var i = 0; i < _meshPoints.Count; i++)
        {
            triangles[i] = i;
        }
        _generatedMesh.triangles = triangles;

        Debug.Log("done messhing");
    }

    private int MarchCube(int3 cubeIndex)
    {
        int triangulationIndex = 0;
        for (var i = 0; i < _cubeLooperCheet.Length; i++)
        {
            var delta = _cubeLooperCheet[i];
            var point = _volumePoints[
                cubeIndex.x + delta.x,
                cubeIndex.y + delta.y,
                cubeIndex.z + delta.z];
            if (point.InsideVolume)
                triangulationIndex |= 1 << i;
        }
        return triangulationIndex;
    }

    private void GenerateCubeMesh(int3 cubeIndex, int triangulationArrayIndex)
    {
        var triangulationSheet = _triangulationTable[triangulationArrayIndex];
        for(var i = 0; i < triangulationSheet.Length; i++)
            _meshPoints.Add(GetEdgePointFromTriangulationIndex(cubeIndex, triangulationSheet[i]));
    }

    private float3 GetEdgePointFromTriangulationIndex(int3 cubeIndex, int cubeMiddlePointIndex)
    {
        var looperIndexA = _cubeMiddlePointIndexToCubeLooper[cubeMiddlePointIndex, 0];
        var looperIndexB = _cubeMiddlePointIndexToCubeLooper[cubeMiddlePointIndex, 1];

        var pointAIndex = cubeIndex + _cubeLooperCheet[looperIndexA];
        var pointBIndex = cubeIndex + _cubeLooperCheet[looperIndexB];

        return (_volumePoints[pointAIndex.x, pointAIndex.y, pointAIndex.z].PointLocalPosition + _volumePoints[pointBIndex.x, pointBIndex.y, pointBIndex.z].PointLocalPosition) * 0.5f;
    }
    public void DrawGizmo(Vector3 centerPosition)
    {
        Gizmos.matrix = Matrix4x4.Translate(centerPosition);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(Vector3.zero, _volumeDimensions);
        /*
        for (var y = 0; y < _axisResolution.y; y++)
        {
            for (var z = 0; z < _axisResolution.z; z++)
            {
                for (var x = 0; x < _axisResolution.x; x++)
                {
                    var point = _volumePoints[x, y, z];
                    Gizmos.color = point.InsideVolume ? Color.red : Color.gray;
                    Gizmos.DrawSphere(point.PointLocalPosition, point.InsideVolume ? 0.025f : 0.0125f);
                }
            }
        }
        */
        var pointTriLenght = Mathf.FloorToInt(_meshPoints.Count / 3f) * 3;
        for(var i = 0; i < pointTriLenght; i+= 3)
        {
            Gizmos.DrawLine(_meshPoints[i], _meshPoints[i + 1]);
            Gizmos.DrawLine(_meshPoints[i + 1], _meshPoints[i + 2]);
            Gizmos.DrawLine(_meshPoints[i + 2], _meshPoints[i]);
        }
    }

    public struct VolumePoint
    {
        public bool InsideVolume;
        public float3 PointLocalPosition;
    }
}

