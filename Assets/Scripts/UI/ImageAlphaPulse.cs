using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Image의 알파값을 사인파로 맥동시켜 은은한 강조 효과를 주는 컴포넌트입니다.
/// 글로우 이미지(PNG)를 별도로 배치하고 이 스크립트를 부착하면,
/// 이미지가 부드럽게 나타났다 사라지는 호흡 효과를 구현합니다.
///
/// [사용법]
/// 1. 강조할 대상 뒤에 글로우 이미지(PNG) 배치
/// 2. 이 스크립트를 글로우 이미지에 부착
/// 3. Inspector에서 속도/밝기 범위 조절
/// </summary>
[RequireComponent(typeof(Image))]
public class ImageAlphaPulse : MonoBehaviour
{
    [Header("===== 맥동 설정 =====")]
    [Tooltip("맥동 속도 (높을수록 빠르게 호흡). 0이면 맥동 없이 최대 알파 고정.")]
    [Range(0f, 8f)]
    [SerializeField] private float pulseSpeed = 1.5f;

    [Tooltip("맥동 시 최소 알파값.")]
    [Range(0f, 1f)]
    [SerializeField] private float minAlpha = 0.0f;

    [Tooltip("맥동 시 최대 알파값.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxAlpha = 0.6f;

    [Tooltip("활성화 시 다른 ImageAlphaPulse와 맥동 타이밍을 동기화합니다.")]
    [SerializeField] private bool syncPulse = false;

    // 내부 참조
    private Image _image;
    private Color _baseColor;

    // 사인파 오프셋 (여러 개 배치 시 동기화 방지)
    private float _timeOffset;

    private void Awake()
    {
        _image = GetComponent<Image>();

        if (_image == null)
        {
            Debug.LogError("[ImageAlphaPulse] Image 컴포넌트를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        // 원본 색상 저장 (RGB 유지, A만 변경)
        _baseColor = _image.color;

        // 터치 이벤트를 가로채지 않음
        _image.raycastTarget = false;

        // syncPulse가 true면 동기화 (오프셋 0), false면 랜덤 위상으로 비동기화
        _timeOffset = syncPulse ? 0f : Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        if (_image == null) return;

        // 사인파로 0~1 범위의 부드러운 값 생성
        float t;
        if (pulseSpeed > 0f)
        {
            t = (Mathf.Sin((Time.time + _timeOffset) * pulseSpeed) + 1f) * 0.5f;
        }
        else
        {
            // 맥동 없이 최대 알파 고정
            t = 1f;
        }

        // 알파값 맥동
        Color color = _baseColor;
        color.a = Mathf.Lerp(minAlpha, maxAlpha, t);
        _image.color = color;
    }
}
