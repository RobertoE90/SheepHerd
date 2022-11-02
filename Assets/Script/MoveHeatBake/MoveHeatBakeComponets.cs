using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public struct HeatMapSharedComponentData: ISharedComponentData, IEquatable<HeatMapSharedComponentData> 
{
    public float2 PhysicalRectSize;
    public RenderTexture HeatTexture;

    public bool Equals(HeatMapSharedComponentData other)
    {
        return other.HeatTexture.Equals(HeatTexture);
    }

    public override int GetHashCode()
    {
        return HeatTexture.GetHashCode();
    }
}