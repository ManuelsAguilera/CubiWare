using UnityEngine;

namespace ARcadeRush.Minigames.Shooter
{
    /// <summary>
    /// Controls the visual appearance of the aim preview indicator.
    /// Attached to the aim preview GameObject (either a prefab child or fallback sphere).
    /// Finds a Renderer on itself or its children, creates a unique material,
    /// and exposes SetPreviewColor() so GunController can update it each frame.
    /// </summary>
    public class AimPreview : MonoBehaviour
    {
        private Renderer _renderer;
        private Material _material;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
                _renderer = GetComponentInChildren<Renderer>();

            if (_renderer != null)
            {
                // Create a unique material instance so color changes don't affect other objects
                Material source = _renderer.sharedMaterial != null
                    ? _renderer.sharedMaterial
                    : new Material(Shader.Find("Sprites/Default"));
                _material = new Material(source);
                _renderer.material = _material;
            }
        }

        /// <summary>
        /// Set the preview indicator color. Called by GunController every frame.
        /// </summary>
        public void SetPreviewColor(Color color)
        {
            if (_material != null)
                _material.color = color;
        }
    }
}
