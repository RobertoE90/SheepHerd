using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseEntityCameraBaker : MonoBehaviour
{
    [Header("Prefab references")]
    [SerializeField] protected Camera _bakeCamera;
    [SerializeField] protected MeshRenderer _bakeDebugMeshRenderer;
    [Space(20)]
    [SerializeField] protected Mesh _bakeMesh;
    [SerializeField] protected Material _bakeMaterial;
    [Space(20)]
    [Header("Bake config")]
    [SerializeField] protected string _bakeLayerName;
    [SerializeField] private bool writeToBakedTexture;
    [SerializeField] protected int _bakeTexturePPU = 2;
    [SerializeField] protected int CAMERA_DEPTH = 10;


    protected RenderTexture _bakeTexture;
    public RenderTexture BakeTexture => _bakeTexture;


    protected int _bakeLayerId;

    public int BakeLayerId => _bakeLayerId;

    protected bool _isInitialized = false;
    public bool IsInitialized => _isInitialized;

    protected Vector2Int _textureSize;
    public Vector2Int TextureSize => _textureSize;

    public event Action InitializedAction;

    public virtual void Initialize(int referencesCount, Vector2 bakeArea, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        _textureSize = new Vector2Int((int)(bakeArea.x * _bakeTexturePPU), (int)(bakeArea.y * _bakeTexturePPU));
        _bakeTexture = new RenderTexture(_textureSize.x, _textureSize.y, 0, RenderTextureFormat.ARGB32, 0);
        _bakeTexture.enableRandomWrite = writeToBakedTexture;
        _bakeTexture.Create();

        _bakeDebugMeshRenderer.transform.localScale = new Vector3(bakeArea.x, 1, bakeArea.y);

        _bakeDebugMeshRenderer.transform.position = centerWorldPosition + centerWorldRotation * (Vector3.up * 0.01f);
        _bakeDebugMeshRenderer.transform.rotation = centerWorldRotation;
        var material = _bakeDebugMeshRenderer.material;
        material.SetTexture("_BaseMap", _bakeTexture);

        _bakeCamera.transform.rotation = centerWorldRotation * Quaternion.Euler(90, 0, 0);
        _bakeCamera.transform.position = centerWorldPosition + centerWorldRotation * (Vector3.up * CAMERA_DEPTH);
        _bakeCamera.nearClipPlane = 0.1f;
        _bakeCamera.farClipPlane = CAMERA_DEPTH + 0.1f;
        _bakeCamera.orthographic = true;
        _bakeCamera.orthographicSize = bakeArea.y * 0.5f;
        _bakeCamera.targetTexture = _bakeTexture;
        
        _bakeLayerId = LayerMask.NameToLayer(_bakeLayerName);
        _bakeCamera.cullingMask = 1 << _bakeLayerId;

        _isInitialized = true;
        InitializedAction?.Invoke();
    }

    private void OnDestroy()
    {
        _bakeTexture.Release();
        Destroy(_bakeTexture);
    }
}
