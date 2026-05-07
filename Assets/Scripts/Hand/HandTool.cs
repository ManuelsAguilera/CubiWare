using UnityEngine;

namespace ARcadeRush.Hand
{
    /// <summary>
    /// Abstract base class for post-prototype attachable hand tools.
    /// </summary>
    public abstract class HandTool : MonoBehaviour
    {
        public abstract void Activate();
        public abstract void Deactivate();
    }
}
