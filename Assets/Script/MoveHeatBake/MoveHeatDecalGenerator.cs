using UnityEngine;
using Unity.Mathematics;
using System;

public class MoveHeatDecalGenerator
{
    Homogenius2DMappedByte[] _movementHeatDecal;
    private int2 _dealSize;


    private Texture2D _bakedTexture;
    public Texture2D BakedTexture => _bakedTexture;

    private Color _zeroColor = Color.black;
    private Color _oneColor = Color.red;

    private const int MAX_HEAT_VALUE = 30;
    private const int FORWARD_GRADIENT_STEP = 10;
    private const int SIDES_GRADIENT_STEP = 20;



    public MoveHeatDecalGenerator(int xSide, int ySide)
    {
        if (xSide % 2 == 0)
            xSide += 1;

        _dealSize = new int2(xSide, ySide);

        _bakedTexture = new Texture2D(xSide, ySide, TextureFormat.RGB24, false, true);
        _bakedTexture.filterMode = FilterMode.Point;

        _movementHeatDecal = new Homogenius2DMappedByte[xSide * ySide];
        for(var x = 0; x < xSide; x++)
        {
            for(var y = 0; y < ySide; y++)
            {
                var index = IndexFromPos(x, y);
                _movementHeatDecal[index] = new Homogenius2DMappedByte
                {
                    h2DCoords = new int3(x, y, 1),
                    Value = 0,
                };
            }
        }

        GenerateForwardHeatValues();
        for(var i = 0; i < 5; i++)
            GenerateSidesHeatValues();
        BakeDecalToTexture();
    }

    private void GenerateForwardHeatValues()
    {
        var xSide = _dealSize.x;

        //forward gradiend
        int currentHeatValue = MAX_HEAT_VALUE;
        int middleIndex = (int)(math.floor(xSide * 0.5f));
        UpdateHeatValue(middleIndex, (byte)currentHeatValue);

        while (currentHeatValue > 0)
        {
            middleIndex += xSide;
            currentHeatValue -= FORWARD_GRADIENT_STEP;

            if (currentHeatValue < 0)
                break;

            try
            {
                UpdateHeatValue(middleIndex, (byte)currentHeatValue);
            }
            catch (IndexOutOfRangeException e)
            {
                break;
            }
        }
    }

    private void GenerateSidesHeatValues()
    {
        for(var x = 0; x < _dealSize.x; x++)
        {
            for(var y = 0; y < _dealSize.y; y++)
            {
                var currentPixel = _movementHeatDecal[IndexFromPos(x, y)].Value;
                if (currentPixel != 0)
                    continue;
                
                var updateValue = (int)NeighbourdMax(x, y) - SIDES_GRADIENT_STEP;
                if (updateValue <= 0)
                    continue;
                UpdateHeatValue(x, y, (byte)updateValue);
            }
        }
    }
    
    private byte NeighbourdMax(int x, int y)
    {
        byte max = 0;
        for(var xD = -1; xD <= 1; xD++)
        {
            for (var yD = -1; yD <= 1; yD++)
            {
                var inspectIndex = IndexFromPos(x + xD, y + yD);
                try
                {
                    if(max < _movementHeatDecal[inspectIndex].Value)
                        max = _movementHeatDecal[inspectIndex].Value;
                    
                }catch(IndexOutOfRangeException e)
                {
                    continue;
                }
            }
        }
        return max;
    }

    private void UpdateHeatValue(int x, int y, byte value)
    {
        var index = IndexFromPos(x, y);
        UpdateHeatValue(index, value);
    }

    private void UpdateHeatValue(int index, byte value) {
        var pixel = _movementHeatDecal[index];
        pixel.Value = value;
        _movementHeatDecal[index] = pixel;
    }

    private int IndexFromPos(int x, int y)
    {
        return y * _dealSize.x + x;
    }

    private void BakeDecalToTexture()
    {
        var colors = new Color[_movementHeatDecal.Length];

        for(var i = 0; i < _movementHeatDecal.Length; i++)
        {
            var pixel = _movementHeatDecal[i].Value / (float)byte.MaxValue;
            colors[i] = Color.Lerp(_zeroColor, _oneColor, pixel);
        }

        _bakedTexture.SetPixels(colors);
        _bakedTexture.Apply();
    }

    public struct Homogenius2DMappedByte
    {
        public int3 h2DCoords; 
        public byte Value;
    }
}
