using UnityEngine;
using UnityEngine.UI;

namespace ARcadeRush.UI
{
    [RequireComponent(typeof(RawImage))]
    public class CameraOverlay : MonoBehaviour
    {
        private RawImage _rawImage;
        private AspectRatioFitter _fitter;

        private void Awake()
        {
            _rawImage = GetComponent<RawImage>();
            
            // Add or get an AspectRatioFitter
            _fitter = GetComponent<AspectRatioFitter>();
            if (_fitter == null)
            {
                _fitter = gameObject.AddComponent<AspectRatioFitter>();
            }

            _fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        }

        private void Update()
        {
            if (_rawImage.texture != null)
            {
                float aspect = (float)_rawImage.texture.width / _rawImage.texture.height;
                if (Mathf.Abs(_fitter.aspectRatio - aspect) > 0.01f)
                {
                    _fitter.aspectRatio = aspect;
                }
            }
        }
    }
}
