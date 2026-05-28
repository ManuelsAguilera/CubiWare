using UnityEngine;

public class Blade : MonoBehaviour
{
    private Camera MainCamera;
    private Collider BladeCollider;
    private TrailRenderer bladeTrail;
    private bool Slicing;

    public Vector3 direction { get; private set;}
    public float sliceForce = 5f;
    public float minSliceVelocity = 0.01f;
    private void Awake()
    {
        MainCamera= Camera.main;
        BladeCollider = GetComponent<Collider>();
        bladeTrail = GetComponentInChildren<TrailRenderer>();
    }
    private void OnDisable()
    {
        StopSlicing();

    }
    private void OnEnable()
    {
        StopSlicing();
    }
    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            StartSlicing();
                    
        } else if (Input.GetMouseButtonUp(0)){
            StopSlicing();
            
            
        }else if (Slicing)
        {
            ContinueSlicing();
        }
    } 
    private void StartSlicing()
    {
        Vector3 newPosition = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        newPosition.z = 0f;
        transform.position = newPosition;

        Slicing = true;
        BladeCollider.enabled = true;
        bladeTrail.enabled = true;
        bladeTrail.Clear();
    }

    private void StopSlicing()
    {
        Slicing= false;
        BladeCollider.enabled = false;
        bladeTrail.enabled = false;
    }

    private void ContinueSlicing()
    {
        Vector3 newPosition = MainCamera.ScreenToWorldPoint(Input.mousePosition);
        newPosition.z = 0f;

        direction = newPosition - transform.position;
        float velocity = direction.magnitude / Time.deltaTime;
        BladeCollider.enabled = velocity > minSliceVelocity;

        transform.position = newPosition;

        
    }
}
