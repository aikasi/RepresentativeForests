using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SpriteGlow의 외곽선 + 소프트 글로우 효과를 Unity UI Image에 적용하는 컴포넌트입니다.
/// 내부 외곽선(하드엣지) + 외부 소프트 글로우(거리 기반 감쇠)를 함께 제공합니다.
/// Bloom 없이도 빛 번짐 효과가 가능합니다.
///
/// [사용법]
/// 1. 글로우를 적용할 Image에 이 스크립트 부착
/// 2. OutlineColor로 색상 설정 (HDR 가능)
/// 3. GlowSpread로 빛 퍼짐 범위 조절
/// 4. 스프라이트 텍스처에 충분한 투명 여백 필요 (글로우 확산용)
/// </summary>
[RequireComponent(typeof(Image))]
[ExecuteInEditMode]
public class UIGlowEffect : MonoBehaviour
{
    [Header("===== 외곽선 (하드엣지) =====")]
    [Tooltip("외곽선 색상 (HDR 지원).")]
    [ColorUsage(true, true)]
    [SerializeField] private Color glowColor = new Color(0f, 2f, 0f, 1f);

    [Tooltip("내부 외곽선 두께 (텍셀). 0이면 외곽선 없이 글로우만.")]
    [Range(0, 10)]
    [SerializeField] private int outlineWidth = 2;

    [Tooltip("투명도 기준값. 이 값 이하를 가장자리로 판단.")]
    [Range(0f, 1f)]
    [SerializeField] private float alphaThreshold = 0.01f;

    [Header("===== 소프트 글로우 (빛 번짐) =====")]
    [Tooltip("글로우 확산 범위 (텍셀). 클수록 넓게 퍼짐. 0이면 비활성.")]
    [Range(0, 30)]
    [SerializeField] private int glowSpread = 10;

    [Tooltip("글로우 감쇠 곡선. 클수록 가장자리가 빠르게 사라짐.")]
    [Range(0.5f, 4f)]
    [SerializeField] private float glowSoftness = 1.5f;

    [Header("===== 맥동 애니메이션 =====")]
    [Tooltip("맥동 속도. 0이면 고정.")]
    [Range(0f, 8f)]
    [SerializeField] private float pulseSpeed = 0f;

    [Range(0f, 1f)]
    [SerializeField] private float minBrightness = 0.5f;

    [Range(0f, 1f)]
    [SerializeField] private float maxBrightness = 1.0f;

    // 내부 참조
    private Image _image;
    private Material _materialInstance;

    // 셰이더 프로퍼티 ID (GC 0)
    private static readonly int PropIsOutlineEnabled = Shader.PropertyToID("_IsOutlineEnabled");
    private static readonly int PropOutlineColor     = Shader.PropertyToID("_OutlineColor");
    private static readonly int PropOutlineSize      = Shader.PropertyToID("_OutlineSize");
    private static readonly int PropAlphaThreshold   = Shader.PropertyToID("_AlphaThreshold");
    private static readonly int PropGlowSpread       = Shader.PropertyToID("_GlowSpread");
    private static readonly int PropGlowSoftness     = Shader.PropertyToID("_GlowSoftness");

    private float _timeOffset;
    private bool _isInitialized;
    private bool _isDirty = true; // 정적 파라미터 변경 추적 플래그

    private void Awake()
    {
        _image = GetComponent<Image>();
        if (_image == null)
        {
            Debug.LogError("[UIGlowEffect] Image 컴포넌트를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        _timeOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void OnEnable()
    {
        // 이미 초기화된 경우 중복 Material 생성 방지
        if (!_isInitialized)
            SetupMaterial();
    }

    private void OnDisable()
    {
        if (_materialInstance != null)
            _materialInstance.SetFloat(PropIsOutlineEnabled, 0f);
    }

    private void SetupMaterial()
    {
        if (_image == null) return;

        Shader glowShader = Shader.Find("UI/SpriteGlow");
        if (glowShader == null)
        {
            Debug.LogError("[UIGlowEffect] 'UI/SpriteGlow' 셰이더를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        CleanupMaterial();

        _materialInstance = new Material(glowShader) { name = "UIGlowEffect_Instance" };
        _image.material = _materialInstance;

        ApplyProperties();
        _isInitialized = true;

        Debug.Log($"[UIGlowEffect] UI 글로우 설정 완료: {gameObject.name}");
    }

    private void ApplyProperties()
    {
        if (_materialInstance == null) return;

        _materialInstance.SetFloat(PropIsOutlineEnabled, 1f);
        _materialInstance.SetColor(PropOutlineColor, glowColor);
        _materialInstance.SetFloat(PropOutlineSize, outlineWidth);
        _materialInstance.SetFloat(PropAlphaThreshold, alphaThreshold);
        _materialInstance.SetFloat(PropGlowSpread, glowSpread);
        _materialInstance.SetFloat(PropGlowSoftness, glowSoftness);
    }

    private void Update()
    {
        if (!_isInitialized || _materialInstance == null) return;

        // 정적 파라미터는 변경 시에만 갱신 (매 프레임 SetFloat 호출 방지)
        if (_isDirty)
        {
            ApplyProperties();
            _isDirty = false;
        }

        // 맥동 애니메이션 (intensity만 매 프레임 갱신)
        if (pulseSpeed > 0f)
        {
            float t = (Mathf.Sin((Time.time + _timeOffset) * pulseSpeed) + 1f) * 0.5f;
            float brightness = Mathf.Lerp(minBrightness, maxBrightness, t);
            _materialInstance.SetColor(PropOutlineColor, glowColor * brightness);
        }
    }

    private void CleanupMaterial()
    {
        if (_materialInstance != null)
        {
            if (Application.isPlaying) Destroy(_materialInstance);
            else DestroyImmediate(_materialInstance);
            _materialInstance = null;
        }
    }

    private void OnDestroy()
    {
        CleanupMaterial();
        if (_image != null) _image.material = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Inspector 값 변경 시 다음 프레임에서 갱신
        _isDirty = true;
    }
#endif
}
