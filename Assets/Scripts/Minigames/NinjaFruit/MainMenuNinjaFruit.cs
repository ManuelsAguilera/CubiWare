using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class MainMenuNinjaFruit : MonoBehaviour
{
    private string gameSceneName = "FruitNinja"; 

    private TMP_Text titleText;
    private TMP_Text subtitleText;
    private Button playButton;

    void Start()
    {
        GameObject titleObj    = GameObject.Find("TitleText");
        GameObject subtitleObj = GameObject.Find("SubtitleText");
        GameObject buttonObj   = GameObject.Find("PlayButton");

        if (titleObj != null)
            titleText = titleObj.GetComponent<TMP_Text>();

        if (subtitleObj != null)
            subtitleText = subtitleObj.GetComponent<TMP_Text>();

        if (buttonObj != null)
        {
            playButton = buttonObj.GetComponent<Button>();
            playButton.onClick.AddListener(StartGame);
        }

        Debug.Log("TitleText encontrado: "    + (titleText != null));
        Debug.Log("SubtitleText encontrado: " + (subtitleText != null));
        Debug.Log("PlayButton encontrado: "   + (playButton != null));

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
        SceneManager.LoadScene(gameSceneName);
    }
}