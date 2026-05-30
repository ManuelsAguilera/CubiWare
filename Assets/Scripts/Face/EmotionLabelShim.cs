// Backward compatibility shim — keeps SceneDirector components compiling during migration.
// TODO: migrate all SceneDirector files (ApprovalBarController, CameraController,
// ScriptController, MaskController, HUDController) to EmotionDetection.EmotionType
// and delete this file.
namespace ARcadeRush.Face
{
    public enum EmotionLabel { Neutral = 0, Happy = 1, Surprised = 2, Angry = 3 }
}
