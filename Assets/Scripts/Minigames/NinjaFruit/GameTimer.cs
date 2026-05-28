using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GameTimer : MonoBehaviour
{
    [Header("Configuracion")]
    public float tiempoTotal = 60f; // 1 minuto

    [Header("UI")]
    public TMP_Text timerText; 

    private float tiempoRestante;
    private bool corriendo = false;

    void Start()
    {
        tiempoRestante = tiempoTotal;
        corriendo = true;
    }

    void Update()
    {
        if (!corriendo) return;

        tiempoRestante -= Time.deltaTime;

        if (tiempoRestante <= 0f)
        {
            tiempoRestante = 0f;
            corriendo = false;
            ActualizarTexto();
            GameOver();
            return;
        }

        ActualizarTexto();
    }

    void ActualizarTexto()
    {
        if (timerText == null) return;

        int segundos = Mathf.CeilToInt(tiempoRestante);
        timerText.text = segundos.ToString();

        
        if (tiempoRestante <= 10f)
            timerText.color = Color.red;
        else
            timerText.color = Color.white;
    }

    void GameOver()
    {
        // Avisa al GameManager que se acabo el tiempo
        GameManagerFruit gm = FindFirstObjectByType<GameManagerFruit>();
        if (gm != null)
            gm.MostrarGameOver();
    }

    // Llama esto para pausar/reanudar el timer desde otro script
    public void PausarTimer(bool pausar)
    {
        corriendo = !pausar;
    }
}