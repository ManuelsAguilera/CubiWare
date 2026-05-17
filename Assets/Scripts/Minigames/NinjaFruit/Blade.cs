using UnityEngine;
using ARcadeRush.Hand;

public class Blade : MonoBehaviour
{
    private Camera MainCamera;
    private Collider BladeCollider;
    private TrailRenderer bladeTrail;
    private bool Slicing;

    [Header("Control Settings")]
    public bool useHandTracking = true;
    private HandPositionTracker _handTracker;

    public Vector3 direction { get; private set;}
    public float sliceForce = 5f;
    public float minSliceVelocity = 0.01f;

    private void Awake()
    {
        MainCamera = Camera.main;
        BladeCollider = GetComponent<Collider>();
        bladeTrail = GetComponentInChildren<TrailRenderer>();
        _handTracker = FindFirstObjectByType<HandPositionTracker>();
    }

    private void OnDisable() => StopSlicing();
    private void OnEnable() => StopSlicing();

    private void Update()
    {
        // For hand tracking, we might want "Slicing" to be always true or controlled by a gesture.
        // For now, we simulate "always slicing" if the hand is detected.
        if (useHandTracking && _handTracker != null)
        {
            if (_handTracker.CurrentHandPosition != Vector2.zero)
            {
                if (!Slicing) StartSlicing();
                ContinueSlicing();
            }
            else
            {
                if (Slicing) StopSlicing();
            }
        }
        else
        {
            // Legacy Mouse Logic
            if (Input.GetMouseButtonDown(0)) StartSlicing();
            else if (Input.GetMouseButtonUp(0)) StopSlicing();
            else if (Slicing) ContinueSlicing();
        }
    } 

    private Vector3 GetInputPosition()
    {
        if (useHandTracking && _handTracker != null)
        {
            // MediaPipe is [0,1] with origin at top-left.
            // Screen is [0, width/height] with origin at bottom-left.
            // Y is inverted (pos.y * Screen.height) to match Unity space.
            Vector2 pos = _handTracker.CurrentHandPosition;
            Vector3 screenPos = new Vector3(pos.x * Screen.width, (1f - pos.y) * Screen.height, 10f);
            return MainCamera.ScreenToWorldPoint(screenPos);
        }
        return MainCamera.ScreenToWorldPoint(Input.mousePosition);
    }

    private void StartSlicing()
    {
        Vector3 newPosition = GetInputPosition();
        newPosition.z = 0f;
        transform.position = newPosition;

        Slicing = true;
        BladeCollider.enabled = true;
        bladeTrail.enabled = true;
        bladeTrail.Clear();
    }

    private void StopSlicing()
    {
        Slicing = false;
        BladeCollider.enabled = false;
        bladeTrail.enabled = false;
    }

    private void ContinueSlicing()
    {
        Vector3 newPosition = GetInputPosition();
        newPosition.z = 0f;

        direction = newPosition - transform.position;
        float velocity = direction.magnitude / Time.deltaTime;
        BladeCollider.enabled = velocity > minSliceVelocity;

        transform.position = newPosition;
    }
}
