using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using ARcadeRush.Core;

public class MainMenuNinjaFruit : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "FruitNinja"; 

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private Button playButton;
    [SerializeField] private Button _backButton;

    void Start()
    {
        if (playButton != null)
        {
            playButton.onClick.AddListener(StartGame);
        }

        if (_backButton != null)
        {
            _backButton.onClick.AddListener(GoBackToMainMenu);
        }

        SetAlpha(titleText, 0f);
        SetAlpha(subtitleText, 0f);
        if (playButton != null)
            playButton.gameObject.SetActive(false);

        StartCoroutine(AnimateUI());
    }

    void SetAlpha(TMP_Text t, float alpha)
    {
        if (t == null) return;
        Color c = t.color;
        c.a = alpha;
        t.color = c;
    }

    IEnumerator AnimateUI()
    {
        yield return StartCoroutine(FadeIn(titleText, 1.0f));

        yield return new WaitForSeconds(0.4f);
        yield return StartCoroutine(FadeIn(subtitleText, 1.0f));

        yield return new WaitForSeconds(0.6f);
        if (playButton != null)
            playButton.gameObject.SetActive(true);
    }

    IEnumerator FadeIn(TMP_Text textElement, float duration)
    {
        if (textElement == null) yield break;

        float elapsed = 0f;
        Color color = textElement.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            color.a = Mathf.Clamp01(elapsed / duration);
            textElement.color = color;
            yield return null;
        }

        color.a = 1f;
        textElement.color = color;
    }

    public void StartGame()
    {
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadSceneAsync(gameSceneName);
        }
        else
        {
            SceneManager.LoadScene(gameSceneName);
        }
    }

    public void GoBackToMainMenu()
    {
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadSceneAsync("MainMenu");
        }
        else
        {
            SceneManager.LoadScene("MainMenu");
        }
    }
}