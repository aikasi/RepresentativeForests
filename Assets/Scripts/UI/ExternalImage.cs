using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// UI 오브젝트에 부착하여 외부 이미지를 자동 로드/해제합니다.
/// Image와 RawImage 둘 다 자동 감지하여 지원합니다.
/// 
/// [CanvasGroup 최적화 대응]
/// UIManager가 SetActive 대신 CanvasGroup.alpha로 패널을 전환하므로,
/// OnEnable은 앱 시작 시 한 번만 호출됩니다.
/// 이미지는 한 번 로드되면 앱 종료까지 캐시에 유지되어
/// 패널 전환 시 디스크 I/O가 전혀 발생하지 않습니다.
/// </summary>
public class ExternalImage : MonoBehaviour
{
    [Tooltip("StreamingAssets/Images 폴더 내의 정확한 파일명 (예: '01-1-btn.png')")]
    [SerializeField] private string fileName;

    private Image _image;           // 버튼 등 Sprite 기반 UI
    private RawImage _rawImage;     // 배경 등 Texture 기반 UI
    private bool _isLoaded = false;

    private void Awake()
    {
        // 둘 중 하나만 있어도 동작 (둘 다 있으면 Image 우선)
        _image = GetComponent<Image>();
        _rawImage = GetComponent<RawImage>();
    }

    /// <summary>
    /// 앱 시작 시 한 번 호출 → 비동기 이미지 로드
    /// CanvasGroup 방식에서는 SetActive가 변경되지 않으므로 재호출되지 않습니다.
    /// </summary>
    private void OnEnable()
    {
        if (_isLoaded) return; // 이미 로드 완료 시 중복 방지
        if (string.IsNullOrEmpty(fileName)) return;
        if (ImageManager.Instance == null) return;

        // 비동기 로딩 시작 (메인 스레드 블로킹 없음)
        ImageManager.Instance.LoadSpriteAsync(fileName, OnSpriteLoaded);
    }

    /// <summary>
    /// 비동기 로딩 완료 콜백. 스프라이트를 UI 컴포넌트에 적용합니다.
    /// </summary>
    private void OnSpriteLoaded(Sprite sprite)
    {
        // 로딩 완료 시점에 오브젝트가 파괴되었으면 무시
        if (this == null || gameObject == null) return;

        if (sprite != null)
        {
            if (_image != null)
            {
                _image.sprite = sprite;
            }
            else if (_rawImage != null)
            {
                _rawImage.texture = sprite.texture;
            }

            _isLoaded = true;
        }
        // null이면 기존 Inspector 이미지(Fallback) 유지
    }

    /// <summary>
    /// 앱 종료 또는 오브젝트 파괴 시 이미지 VRAM 해제
    /// </summary>
    private void OnDisable()
    {
        if (!_isLoaded) return;
        if (string.IsNullOrEmpty(fileName)) return;
        if (ImageManager.Instance == null) return;

        ImageManager.Instance.UnloadSprite(fileName);
        _isLoaded = false;
    }

    /// <summary>
    /// OnDisable이 호출되지 않는 예외 상황 대비 안전장치
    /// </summary>
    private void OnDestroy()
    {
        if (!_isLoaded) return;
        if (string.IsNullOrEmpty(fileName)) return;
        if (ImageManager.Instance == null) return;

        ImageManager.Instance.UnloadSprite(fileName);
        _isLoaded = false;
    }
}
