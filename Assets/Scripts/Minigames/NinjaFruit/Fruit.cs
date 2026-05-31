using UnityEngine;

public class Fruit : MonoBehaviour
{
    public GameObject whole;
    public GameObject sliced;
    private Rigidbody fruitRigidbody;
    private Collider fruitCollider;
    private ParticleSystem juiceEffect;
    private bool cortada = false;
    private bool vidaPerdida = false;

    private void Awake()
    {
        fruitRigidbody = GetComponent<Rigidbody>();
        fruitCollider = GetComponent<Collider>();
        juiceEffect = GetComponentInChildren<ParticleSystem>();
    }

    private void Update()
    {
        // Si cae por debajo de la pantalla y no fue cortada
        if (!cortada && !vidaPerdida && transform.position.y < -8f)
        {
            vidaPerdida = true;
            LivesManager lm = FindAnyObjectByType<LivesManager>();
            if (lm != null)
                lm.PerderVida();

        }
    }

    private void Slice(Vector3 direction, Vector3 position, float force)
    {
        cortada = true;

        FindAnyObjectByType<GameManagerFruit>().IncreaseScore();

        fruitCollider.enabled = false;
        whole.SetActive(false);

        sliced.SetActive(true);
        juiceEffect.Play();

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        sliced.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        Rigidbody[] slices = sliced.GetComponentsInChildren<Rigidbody>();

        foreach (Rigidbody slice in slices)
        {
            slice.linearVelocity = fruitRigidbody.linearVelocity;
            slice.AddForceAtPosition(direction * force, position, ForceMode.Impulse);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Blade blade = other.GetComponent<Blade>();
            Slice(blade.direction, blade.transform.position, blade.sliceForce);
        }
    }
}