using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TMP_Text textComponent;
    [SerializeField] private float scaleMultiplier = 1.08f;
    [SerializeField] private float scaleDuration = 0.12f;
    [SerializeField] private float waveAmplitude = 4f;
    [SerializeField] private float waveSpeed = 2f;
    [SerializeField] private float wavePhaseStep = 0.5f;
    [SerializeField] private Color colorTop = new Color(1f, 0.910f, 0f, 1f);
    [SerializeField] private Color colorBottom = new Color(1f, 0.416f, 0f, 1f);

    private Vector3 _originalScale;
    private Color _originalColor;
    private bool _isHovering;
    private float _hoverTime;
    private Coroutine _scaleCoroutine;

    void Start()
    {
        _originalScale = transform.localScale;

        if (textComponent == null)
            textComponent = GetComponentInChildren<TMP_Text>();

        if (textComponent != null)
            _originalColor = textComponent.color;
    }

    void Update()
    {
        if (!_isHovering || textComponent == null) return;

        _hoverTime += Time.deltaTime;
        ApplyWave();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovering = true;
        _hoverTime = 0f;

        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(AnimateScale(transform.localScale, _originalScale * scaleMultiplier, scaleDuration));

        if (textComponent == null) return;
        textComponent.enableVertexGradient = true;
        textComponent.colorGradient = new VertexGradient(colorTop, colorTop, colorBottom, colorBottom);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovering = false;

        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(AnimateScale(transform.localScale, _originalScale, scaleDuration));

        if (textComponent == null) return;
        textComponent.enableVertexGradient = false;
        textComponent.color = _originalColor;
        textComponent.ForceMeshUpdate();
    }

    private void ApplyWave()
    {
        textComponent.ForceMeshUpdate();
        TMP_TextInfo textInfo = textComponent.textInfo;

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int matIndex = charInfo.materialReferenceIndex;
            int vertIndex = charInfo.vertexIndex;
            Vector3[] verts = textInfo.meshInfo[matIndex].vertices;

            float offsetY = Mathf.Sin(_hoverTime * waveSpeed + i * wavePhaseStep) * waveAmplitude;
            Vector3 offset = new Vector3(0f, offsetY, 0f);

            verts[vertIndex + 0] += offset;
            verts[vertIndex + 1] += offset;
            verts[vertIndex + 2] += offset;
            verts[vertIndex + 3] += offset;
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            textComponent.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }

    private IEnumerator AnimateScale(Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        transform.localScale = to;
    }
}
