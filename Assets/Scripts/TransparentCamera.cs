using UnityEngine;

public class TransparentCamera : MonoBehaviour 
{
    void Awake() 
    {
        Camera cam = GetComponent<Camera>();
        
        // Set background to transparent
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); // Transparent!
        
        Debug.Log("Camera set to transparent background");
    }
}