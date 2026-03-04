using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class TestScript : MonoBehaviour
{
    [SerializeField]
    private Transform presentator;
    [SerializeField]
    private Transform pointA;
    [SerializeField]
    private Transform pointB;

    private float easeTime = 1f;

    private bool isAtPointB = false;

    public void MovePresentatorEvent()
    {
        if (isAtPointB)
            StartCoroutine(MovePresentator(pointB, pointA));
        else
            StartCoroutine(MovePresentator(pointA, pointB));
    }

    private void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            MovePresentatorEvent();
    }


    IEnumerator MovePresentator(Transform currentLocation, Transform targetLocation)
    {
        float time = 0f;
        while (time < easeTime)
        {
            presentator.position = Vector3.Lerp(currentLocation.position, targetLocation.position, easeInOutQuart(time));
            time += Time.deltaTime;
            yield return null;
        }
        presentator.position = targetLocation.position;
        if (isAtPointB)
            isAtPointB = false;
        else
            isAtPointB = true;
    }

    float easeInOutQuart(float x) {
        return x< 0.5 ? 8 * x* x* x* x : 1 - Mathf.Pow(-2 * x + 2, 4) / 2;
    }
}
