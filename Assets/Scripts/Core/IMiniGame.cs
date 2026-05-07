namespace ARcadeRush.Core
{
    public class MiniGameDependencies
    {
        public GameManager GameManager;
        public CameraFeedCtrl Camera;
        public MediaPipeController MediaPipe;
        public LLMConnector LLM;
    }

    public interface IMiniGame
    {
        void OnStart(MiniGameDependencies deps);
        void OnEnd();
        int SceneIndex { get; }
    }
}
