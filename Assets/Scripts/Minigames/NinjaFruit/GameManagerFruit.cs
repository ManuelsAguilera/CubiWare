using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManagerFruit : MonoBehaviour
{
    [Header("UI Juego")]
    public TMP_Text scoreText;

    [Header("UI Game Over")]
    public GameObject panelGameOver;
    public TMP_Text gameOverScoreText;

    private Blade blade;
    private Spawner spawner;
    private int score;

    private void Awake()
    {
        blade = FindFirstObjectByType<Blade>();
        spawner = FindFirstObjectByType<Spawner>();
    }

    private void Start()
    {
        if (panelGameOver != null)
            panelGameOver.SetActive(false);

        NewGame();
    }

    private void NewGame()
    {
        Time.timeScale = 1f;

        if (panelGameOver != null)
            panelGameOver.SetActive(false);

        ClearScene();

        blade.enabled = true;
        spawner.enabled = true;

        score = 0;
        scoreText.text = score.ToString();

        FindFirstObjectByType<LivesManager>()?.ResetVidas();
    }

    private void ClearScene()
    {
        Fruit[] fruits = FindObjectsByType<Fruit>(FindObjectsSortMode.None);
        foreach (Fruit fruit in fruits)
            Destroy(fruit.gameObject);

        Bomb[] bombs = FindObjectsByType<Bomb>(FindObjectsSortMode.None);
        foreach (Bomb bomb in bombs)
            Destroy(bomb.gameObject);
    }

    public void IncreaseScore()
    {
        score++;
        scoreText.text = score.ToString();
    }

    public void MostrarGameOver()
    {
        blade.enabled = false;
        spawner.enabled = false;
        FindFirstObjectByType<GameTimer>()?.PausarTimer(true);
        StartCoroutine(ExplodeSequence());
    }

    public void Explode()
    {
        blade.enabled = false;
        spawner.enabled = false;
        FindFirstObjectByType<GameTimer>()?.PausarTimer(true);
        StartCoroutine(ExplodeSequence());
    }

    private IEnumerator ExplodeSequence()
    {
        yield return new WaitForSecondsRealtime(1f);

        if (panelGameOver != null)
            panelGameOver.SetActive(true);

        if (gameOverScoreText != null)
            gameOverScoreText.text = "Tu puntaje es: " + score;
    }

    public void Reiniciar()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void VolverAlMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }
}