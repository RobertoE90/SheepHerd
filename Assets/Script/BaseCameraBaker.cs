using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class BaseCameraBaker : MonoBehaviour
{
    [Header("Prefab references")]
    [SerializeField] protected Camera _bakeCamera;
    [SerializeField] protected MeshRenderer _bakeDebugMeshRenderer;
    [SerializeField] private Vector3 _debugMeshSizeUnitsDisplacement;
    [Space(20)]
    [Header("Bake config")]
    [SerializeField] protected string _bakeLayerName;
    [SerializeField] private bool writeToBakedTexture;
    [SerializeField] protected float _cameraDepth = 10;
    [SerializeField] private RenderTextureFormat _bakeTextureFormat;
    protected RenderTexture _bakeTexture;
    public RenderTexture BakeTexture => _bakeTexture;

    protected int _bakeLayerId;
    public int BakeLayerId => _bakeLayerId;
    protected bool _isInitialized = false;
    public bool IsInitialized => _isInitialized;
    protected Vector2Int _textureSize;
    public Vector2Int TextureSize => _textureSize;
    public event Action BakerInitializedAction;

    public virtual void Initialize(Vector2 bakeArea, float texturePPU, float worldScale, Vector3 centerWorldPosition, Quaternion centerWorldRotation)
    {
        _textureSize = new Vector2Int((int)(bakeArea.x * texturePPU), (int)(bakeArea.y * texturePPU));
        _bakeTexture = new RenderTexture(_textureSize.x, _textureSize.y, 0, _bakeTextureFormat, 0);
        _bakeTexture.enableRandomWrite = writeToBakedTexture;
        _bakeTexture.Create();

        _bakeDebugMeshRenderer.transform.localScale = new Vector3(bakeArea.x * worldScale, 1, bakeArea.y * worldScale);

        _bakeDebugMeshRenderer.transform.position = centerWorldPosition + centerWorldRotation * (Vector3.Scale(_debugMeshSizeUnitsDisplacement, _bakeDebugMeshRenderer.transform.localScale));
        _bakeDebugMeshRenderer.transform.rotation = centerWorldRotation;
        var material = _bakeDebugMeshRenderer.material;
        material.SetTexture("_BaseMap", _bakeTexture);

        _bakeCamera.transform.rotation = centerWorldRotation * Quaternion.Euler(90, 0, 0);
        _bakeCamera.transform.position = centerWorldPosition + centerWorldRotation * (Vector3.up * _cameraDepth * worldScale);
        _bakeCamera.nearClipPlane = 0.1f * worldScale;
        _bakeCamera.farClipPlane = (_cameraDepth + 0.1f) * worldScale;
        _bakeCamera.orthographic = true;
        _bakeCamera.orthographicSize = bakeArea.y * 0.5f * worldScale;
        _bakeCamera.targetTexture = _bakeTexture;

        _bakeLayerId = LayerMask.NameToLayer(_bakeLayerName);
        _bakeCamera.cullingMask = 1 << _bakeLayerId;

        _isInitialized = true;
        BakerInitializedAction?.Invoke();
    }

    protected virtual void OnDestroy()
    {
        _bakeTexture.Release();
        Destroy(_bakeTexture);
    }

}
