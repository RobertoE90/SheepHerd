using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraUtil : MonoBehaviour
{
    [SerializeField][Range(100f, 400f)] private float _heighValue;
    [SerializeField] private float _targerLerpSpeed;
    [SerializeField] private Transform _target;
    private Vector3 _targetPosition = Vector3.zero;

    // Update is called once per frame
    void Update()
    {
        transform.LookAt(_target);

        transform.parent.position = Vector3.Lerp(transform.parent.position, Vector3.up * _heighValue, Time.deltaTime * _targerLerpSpeed);

        _target.position = Vector3.Lerp(_target.position, _targetPosition, Time.deltaTime * _targerLerpSpeed);

        if (Input.GetMouseButtonUp(0))
        {
            var cameraRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(cameraRay, out var raycastInfo, 10000f))
            {
                _targetPosition = raycastInfo.point;
            }
        }
    }
}
