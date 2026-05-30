using UnityEngine;
using TMPro;

public class LivesManager : MonoBehaviour
{
    [Header("Vidas")]
    public int vidasMaximas = 3;
    private int vidasActuales;

    [Header("Iconos de X (arrastra tus 3 textos aqui)")]
    public TMP_Text[] iconosVidas;

    private GameManagerFruit gameManager;

    void Awake()
    {
        gameManager = FindAnyObjectByType<GameManagerFruit>();
    }

    void Start()
    {
        vidasActuales = vidasMaximas;
        ActualizarIconos();
    }

    public void PerderVida()
    {
        if (vidasActuales <= 0) return;

        // Buscar de nuevo por si acaso
        if (gameManager == null)
            gameManager = FindAnyObjectByType<GameManagerFruit>();

        vidasActuales--;
        ActualizarIconos();

        if (vidasActuales <= 0)
        {
            gameManager.MostrarGameOver();
        }
    }

    void ActualizarIconos()
    {
        for (int i = 0; i < iconosVidas.Length; i++)
        {
            if (iconosVidas[i] != null)
                iconosVidas[i].enabled = i >= vidasActuales;
        }
    }

    public void ResetVidas()
    {
        vidasActuales = vidasMaximas;
        ActualizarIconos();
    }
}