using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class InputEntityManager : MonoBehaviour
{
    private Entity _inputEntity;
    private EntityManager _entityManager;
    private Matrix4x4 _originTransform;

    private static InputEntityManager _instance;
    private Transform[] _targets;

    public static InputEntityManager Instance
    {
        get
        {
            if(_instance == null)
            {
                _instance = GameObject.FindObjectOfType<InputEntityManager>();
                if (_instance == null)
                    Debug.LogError("No input entity manager on scene");
            }
            return _instance;
        }
    }

    private void Awake()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _inputEntity = _entityManager.CreateEntity();
        _originTransform = Matrix4x4.identity;
        var list = new List<Transform>();
        GetComponentsInChildren(list);
        list.RemoveAt(0);
        _targets = list.ToArray();

        var dynamicBuffer = _entityManager.AddBuffer<InputPoint>(_inputEntity);
    }



    private void Update()
    {
        if (_targets == null || _targets.Length == 0)
            return;


        var dynamicBuffer = _entityManager.GetBuffer<InputPoint>(_inputEntity);
        if (dynamicBuffer.Length != _targets.Length)
            for (var i = dynamicBuffer.Length; i < _targets.Length; i++)
            {
                dynamicBuffer.Add(new InputPoint
                {
                    LocalInputPosition = new float2(0, 0),
                });
            }

        var inputBuffer = dynamicBuffer.Reinterpret<float2>();

        if(inputBuffer.Length != _targets.Length)
        {
            Debug.LogError("Buffer different from target size");
            return;
        }

        for (var i = 0; i < inputBuffer.Length; i++)
            inputBuffer[i] = WorldToInputSpace(_targets[i].position);
        
                         
        
    }

    public void SetInputReferenceMatrix(Matrix4x4 inputReference)
    {
        _originTransform = inputReference;
    }

    private Vector2 WorldToInputSpace(Vector3 worldPosition)
    {
        var localPos =_originTransform.MultiplyPoint3x4(worldPosition);
        return new Vector2(localPos.x, localPos.z);
    }

    private void OnDrawGizmos()
    {
        if (_targets == null)
            return;

        Gizmos.color = Color.green;
        foreach(var target in _targets)
            Gizmos.DrawWireSphere(target.position, 10f);
    }
}

[InternalBufferCapacity(4)]
public struct InputPoint: IBufferElementData
{
    public float2 LocalInputPosition;
}
