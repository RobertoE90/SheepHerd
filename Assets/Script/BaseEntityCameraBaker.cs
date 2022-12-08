using UnityEngine;

public abstract class BaseEntityCameraBaker : BaseCameraBaker
{
    [Space(20)]
    [SerializeField] protected Mesh _bakeMesh;
    [SerializeField] protected Material _bakeMaterial;

    public abstract void Initialize(int sheepCount, Vector2 vector2, float globalBakeTexturesPPU, float worldScale, Vector3 position, Quaternion rotation);
}
