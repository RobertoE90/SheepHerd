using UnityEngine;

public static class ComputeShaderUtilities
{
    public static bool CheckComputeShaderTextureSize(ComputeShader computeShader, int kernelId, int textureWidth, int textureHeight)
    {
        computeShader.GetKernelThreadGroupSizes(kernelId, out var xThreads, out var yThreads, out var zThreads);
        if(textureWidth % xThreads != 0 || textureHeight % yThreads != 0)
        {
            Debug.LogError($"Texture sizes {textureWidth}, {textureHeight} dont match with thread shape {xThreads}, {yThreads}");
            return false;
        }
        return true;
    }
}
