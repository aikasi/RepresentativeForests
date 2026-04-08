using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 오브젝트에 부착하여 외부 이미지를 자동 로드/해제합니다.
/// Image와 RawImage 둘 다 자동 감지하여 지원합니다.
/// 패널이 활성화(SetActive true)되면 로드, 비활성화되면 VRAM에서 해제합니다.
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
    /// 패널 활성화 시 자동 호출 → 이미지 로드
    /// </summary>
    private void OnEnable()
    {
        if (string.IsNullOrEmpty(fileName)) return;
        if (ImageManager.Instance == null) return;

        Sprite sprite = ImageManager.Instance.LoadSprite(fileName);

        if (sprite != null)
        {
            // Image 컴포넌트가 있으면 Sprite로 적용
            if (_image != null)
            {
                _image.sprite = sprite;
            }
            // RawImage 컴포넌트가 있으면 Texture로 적용
            else if (_rawImage != null)
            {
                _rawImage.texture = sprite.texture;
            }

            _isLoaded = true;
        }
        // null이면 기존 Inspector 이미지(Fallback) 유지
    }

    /// <summary>
    /// 패널 비활성화 시 자동 호출 → 이미지 VRAM 해제
    /// </summary>
    private void OnDisable()
    {
        if (!_isLoaded) return;
        if (string.IsNullOrEmpty(fileName)) return;
        if (ImageManager.Instance == null) return;

        ImageManager.Instance.UnloadSprite(fileName);
        _isLoaded = false;
    }
}
