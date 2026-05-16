using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    public TMP_Text scoreText;

    private int score;

    public void IncreaseScore()
    {
        score++;
        scoreText.text = score.ToString();
    }
}