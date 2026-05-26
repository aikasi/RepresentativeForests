using UnityEngine;

/// <summary>
/// RectTransform의 localScale을 사인파로 맥동시켜
/// 버튼이 미세하게 숨쉬듯 움직이는 효과를 부여합니다.
/// ButtonGlowEffect와 함께 사용하면 글로우 + 스케일 맥동으로
/// 더욱 강렬한 시각적 강조가 가능합니다.
///
/// [사용법]
/// 1. 강조할 버튼에 이 스크립트를 부착
/// 2. Inspector에서 속도/스케일 범위 조절
/// 3. (선택) ButtonGlowEffect와 syncPulse를 true로 설정하면 타이밍 동기화
/// </summary>
public class ButtonScalePulse : MonoBehaviour
{
    [Header("===== 스케일 맥동 설정 =====")]
    [Tooltip("맥동 속도 (높을수록 빠르게 맥동). 0이면 맥동 없이 최대 스케일 고정.")]
    [Range(0f, 8f)]
    [SerializeField] private float pulseSpeed = 1.5f;

    [Tooltip("맥동 시 최소 스케일 배율.")]
    [Range(0.9f, 1.0f)]
    [SerializeField] private float minScale = 1.0f;

    [Tooltip("맥동 시 최대 스케일 배율.")]
    [Range(1.0f, 1.15f)]
    [SerializeField] private float maxScale = 1.03f;

    [Header("===== 동기화 =====")]
    [Tooltip("활성화 시 다른 맥동 스크립트와 타이밍을 동기화합니다. (시간 오프셋 0)")]
    [SerializeField] private bool syncPulse = false;

    // 내부 참조
    private RectTransform _rectTransform;
    private Vector3 _originalScale;
    private float _timeOffset;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();

        if (_rectTransform == null)
        {
            Debug.LogError("[ButtonScalePulse] RectTransform을 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        // 원본 스케일 저장 (초기 스케일이 1이 아닌 경우에도 안전)
        _originalScale = _rectTransform.localScale;

        // syncPulse가 true면 동기화 (오프셋 0), false면 랜덤 위상으로 비동기화
        _timeOffset = syncPulse ? 0f : Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        if (_rectTransform == null) return;

        // 사인파로 0~1 범위의 부드러운 값 생성
        float t;
        if (pulseSpeed > 0f)
        {
            t = (Mathf.Sin((Time.time + _timeOffset) * pulseSpeed) + 1f) * 0.5f;
        }
        else
        {
            // 맥동 없이 최대 스케일 고정
            t = 1f;
        }

        // 스케일 맥동 (원본 스케일 기준으로 균일하게 확대/축소)
        float scale = Mathf.Lerp(minScale, maxScale, t);
        _rectTransform.localScale = _originalScale * scale;
    }

    private void OnDisable()
    {
        // 비활성화 시 원본 스케일로 복원 (레이아웃 안전)
        if (_rectTransform != null)
        {
            _rectTransform.localScale = _originalScale;
        }
    }
}
