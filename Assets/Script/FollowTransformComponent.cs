using Unity.Entities;


public struct CopyTransformReferenceComponent : IComponentData
{
    public int ReferenceIndex;
    public bool CopyTranslation;
    public bool CopyRotation;
}