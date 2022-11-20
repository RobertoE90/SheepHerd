using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public struct PhysicalSizeTexture: ISharedComponentData, IEquatable<PhysicalSizeTexture> 
{
    public float2 PhysicalTextureSize;
    public RenderTexture TextureReference;
    public TextureTypes Type;

    public bool Equals(PhysicalSizeTexture other)
    {
        return other.TextureReference.Equals(TextureReference);
    }

    public override int GetHashCode()
    {
        return TextureReference.GetHashCode();
    }
}

public enum TextureTypes
{
    MOVE_HEAT_TEXTURE,
    ENTITY_ID_TEXTURE,
    INPUT_BAKE_TEXTURE
}