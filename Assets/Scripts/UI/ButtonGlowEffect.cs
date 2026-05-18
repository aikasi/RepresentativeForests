using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 버튼 외곽에 소프트 글로우 효과를 적용하는 셰이더 기반 컴포넌트입니다.
/// 마진 기반 UV 리매핑으로 스프라이트를 안쪽에 배치하고,
/// 여백 영역에서 블러 글로우를 렌더링합니다.
/// 모든 파라미터가 Play 모드에서 실시간 반영됩니다.
/// </summary>
[RequireComponent(typeof(Image))]
public class ButtonGlowEffect : MonoBehaviour
{
    [Header("===== 글로우 대상 =====")]
    [Tooltip("글로우 형태를 복사할 원본 버튼의 Image 컴포넌트")]
    [SerializeField] private Image sourceImage;

    [Header("===== 글로우 색상 =====")]
    [SerializeField] private Color glowColor = new Color(0.55f, 0.95f, 0.45f, 1f);

    [Header("===== 셰이더 파라미터 =====")]
    [Tooltip("글로우 퍼짐 범위. 클수록 넓게 퍼짐.")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float glowSpread = 0.15f;

    [Tooltip("글로우 페이드 곡선. 클수록 가장자리가 빠르게 사라짐.")]
    [Range(0.5f, 4f)]
    [SerializeField] private float glowSoftness = 1.2f;

    [Header("===== 맥동 애니메이션 =====")]
    [Range(0f, 8f)]
    [SerializeField] private float pulseSpeed = 1.5f;

    [Range(0f, 2f)]
    [SerializeField] private float minIntensity = 0.3f;

    [Range(0f, 2f)]
    [SerializeField] private float maxIntensity = 0.8f;

    [Header("===== 디버그 (자동 계산, 읽기 전용) =====")]
    [Tooltip("자동 계산된 마진값 (읽기 전용)")]
    [SerializeField] private float debugMarginX;
    [SerializeField] private float debugMarginY;

    // 내부 참조
    private Image _glowImage;
    private RectTransform _rectTransform;
    private Material _materialInstance;

    // 셰이더 프로퍼티 ID
    private static readonly int PropGlowColor    = Shader.PropertyToID("_GlowColor");
    private static readonly int PropGlowIntensity = Shader.PropertyToID("_GlowIntensity");
    private static readonly int PropGlowSpread   = Shader.PropertyToID("_GlowSpread");
    private static readonly int PropGlowSoftness = Shader.PropertyToID("_GlowSoftness");
    private static readonly int PropMarginX      = Shader.PropertyToID("_MarginX");
    private static readonly int PropMarginY      = Shader.PropertyToID("_MarginY");
    private static readonly int PropAspectRatio   = Shader.PropertyToID("_AspectRatio");

    private float _timeOffset;
    private bool _isInitialized;
    private bool _isDirty = true; // 정적 파라미터 변경 추적 플래그

    private void Awake()
    {
        _glowImage = GetComponent<Image>();
        _rectTransform = GetComponent<RectTransform>();

        if (_glowImage == null || sourceImage == null)
        {
            Debug.LogError("[ButtonGlowEffect] Image 또는 sourceImage가 없습니다!");
            enabled = false;
            return;
        }

        _timeOffset = Random.Range(0f, Mathf.PI * 2f);
        SetupMaterial();
    }

    private void Start()
    {
        // 런타임 스프라이트 로드 대비 재시도
        if (!_isInitialized && sourceImage != null && sourceImage.sprite != null)
            SetupMaterial();
    }

    private void SetupMaterial()
    {
        if (sourceImage == null || sourceImage.sprite == null) return;

        Shader glowShader = Shader.Find("UI/OuterGlow");
        if (glowShader == null)
        {
            Debug.LogError("[ButtonGlowEffect] 'UI/OuterGlow' 셰이더를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        _materialInstance = new Material(glowShader) { name = "ButtonGlowEffect_Instance" };

        // 스프라이트 복사 (Simple 타입으로 균일 UV 매핑)
        _glowImage.sprite = sourceImage.sprite;
        _glowImage.type = Image.Type.Simple;
        _glowImage.material = _materialInstance;
        _glowImage.raycastTarget = false;

        _isInitialized = true;
        Debug.Log($"[ButtonGlowEffect] 글로우 설정 완료: {sourceImage.gameObject.name}");
    }

    private void Update()
    {
        if (!_isInitialized || _materialInstance == null) return;

        // 정적 파라미터는 변경 시에만 갱신 (매 프레임 SetFloat 호출 방지)
        if (_isDirty)
        {
            _materialInstance.SetColor(PropGlowColor, glowColor);
            _materialInstance.SetFloat(PropGlowSpread, glowSpread);
            _materialInstance.SetFloat(PropGlowSoftness, glowSoftness);
            UpdateMargins();
            _isDirty = false;
        }

        // 맥동 애니메이션 (intensity만 매 프레임 갱신)
        float t = pulseSpeed > 0f
            ? (Mathf.Sin((Time.time + _timeOffset) * pulseSpeed) + 1f) * 0.5f
            : 1f;
        float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);
        _materialInstance.SetFloat(PropGlowIntensity, intensity);
    }

    /// <summary>
    /// 글로우 이미지와 버튼 크기 차이에서 마진을 자동 계산합니다.
    /// </summary>
    private void UpdateMargins()
    {
        if (sourceImage == null || _rectTransform == null) return;

        Rect glowRect = _rectTransform.rect;
        Rect buttonRect = sourceImage.rectTransform.rect;

        if (glowRect.width > 0f && glowRect.height > 0f)
        {
            float marginX = Mathf.Max(0f, (glowRect.width - buttonRect.width) / (2f * glowRect.width));
            float marginY = Mathf.Max(0f, (glowRect.height - buttonRect.height) / (2f * glowRect.height));

            _materialInstance.SetFloat(PropMarginX, marginX);
            _materialInstance.SetFloat(PropMarginY, marginY);

            // 종횡비 전달 (세로 블러 보정용)
            float aspectRatio = glowRect.width / glowRect.height;
            _materialInstance.SetFloat(PropAspectRatio, aspectRatio);

            // 디버그 표시 (Inspector에서 확인용)
            debugMarginX = marginX;
            debugMarginY = marginY;
        }
    }

    private void OnDestroy()
    {
        if (_materialInstance != null)
        {
            if (Application.isPlaying) Destroy(_materialInstance);
            else DestroyImmediate(_materialInstance);
            _materialInstance = null;
        }
    }
}
