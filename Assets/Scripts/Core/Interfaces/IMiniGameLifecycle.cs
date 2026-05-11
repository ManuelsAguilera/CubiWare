namespace CubiWare.Core.Interfaces
{
    /// <summary>
    /// Extends the basic IMiniGame lifecycle with pause/resume and time-warning callbacks.
    /// Minigames implementing this interface can react to lifecycle events beyond start/end.
    /// </summary>
    public interface IMiniGameLifecycle
    {
        /// <summary>
        /// Called when the minigame is paused (e.g., menu overlay, focus lost).
        /// </summary>
        void OnPause();

        /// <summary>
        /// Called when the minigame resumes from a paused state.
        /// </summary>
        void OnResume();

        /// <summary>
        /// Called when the game timer is about to expire.
        /// </summary>
        /// <param name="remainingSeconds">The number of seconds remaining in the game.</param>
        void OnTimeWarning(float remainingSeconds);
    }
}
