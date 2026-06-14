using ARcadeRush.Face;

namespace ARcadeRush.Minigames.SceneDirector
{
    /// <summary>
    /// Spanish display names (adjectives, uppercase) for EmotionLabel values.
    /// Used everywhere an emotion is shown to the player.
    /// </summary>
    public static class EmotionEs
    {
        public static string ToSpanish(EmotionLabel label) => label switch
        {
            EmotionLabel.Happy     => "FELIZ",
            EmotionLabel.Surprised => "SORPRENDIDO",
            EmotionLabel.Angry     => "ENOJADO",
            EmotionLabel.Disgust   => "ASQUEADO",
            EmotionLabel.Fear      => "ASUSTADO",
            EmotionLabel.Sad       => "TRISTE",
            _                      => "NEUTRAL",
        };
    }
}
