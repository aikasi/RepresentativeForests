using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 터치 Canvas의 영상 미러링 패널을 제어합니다.
/// VideoMirrorCapture가 캡처한 RenderTexture를 RawImage로 표시하고,
/// "목록으로 돌아가기" 버튼 이벤트를 ForestShortcutUI에 전달합니다.
/// 단일 책임: 미러 패널의 표시/숨김 + 버튼 이벤트만 담당합니다.
/// </summary>
public class VideoMirrorUI : MonoBehaviour
{
    [Header("===== 미러 패널 참조 =====")]
    [Tooltip("미러 뷰 전체를 감싸는 CanvasGroup (alpha로 표시/숨김 제어)")]
    [SerializeField] private CanvasGroup mirrorPanelCanvasGroup;

    [Header("===== 영상 표시 =====")]
    [Tooltip("RenderTexture를 표시할 RawImage (전체 화면 Stretch)")]
    [SerializeField] private RawImage mirrorRawImage;
    [Tooltip("VideoMirrorCapture와 동일한 RenderTexture 에셋")]
    [SerializeField] private RenderTexture mirrorRT;

    [Header("===== 버튼 =====")]
    [Tooltip("우하단 '목록으로 돌아가기' 버튼")]
    [SerializeField] private Button backToListButton;

    [Header("===== 전환 오버레이 =====")]
    [Tooltip("영상 전환 시 대기화면을 가리는 검은 Image (최상위 자식으로 배치)")]
    [SerializeField] private Image blackOverlay;

    [Header("===== 캡처 제어 =====")]
    [Tooltip("VideoCamera에 부착된 VideoMirrorCapture 컴포넌트")]
    [SerializeField] private VideoMirrorCapture mirrorCapture;

    /// <summary>
    /// ForestShortcutUI가 구독할 "목록으로 돌아가기" 이벤트입니다.
    /// </summary>
    public event Action OnBackToListRequested;

    /// <summary>
    /// 미러 패널의 CanvasGroup입니다. ForestShortcutUI의 크로스페이드 alpha 제어에 사용됩니다.
    /// </summary>
    public CanvasGroup MirrorCanvasGroup => mirrorPanelCanvasGroup;

    // 검은 오버레이 페이드아웃 코루틴 참조
    private Coroutine _fadeCoroutine = null;

    private void Start()
    {
        // 버튼 이벤트 바인딩
        if (backToListButton != null)
            backToListButton.onClick.AddListener(OnBackToListClicked);

        // 초기 상태: 패널 숨김
        SetPanelVisible(false);

        Debug.Log("[VideoMirrorUI] 초기화 완료.");
    }

    /// <summary>
    /// 미러 뷰를 표시합니다. 캡처를 시작하고 RawImage에 RenderTexture를 연결합니다.
    /// </summary>
    public void Show()
    {
        // RawImage에 RenderTexture 연결
        if (mirrorRawImage != null && mirrorRT != null)
        {
            mirrorRawImage.texture = mirrorRT;
        }
        else
        {
            Debug.LogError("[VideoMirrorUI] mirrorRawImage 또는 mirrorRT가 연결되지 않았습니다!");
        }

        // 캡처 시작
        if (mirrorCapture != null)
        {
            mirrorCapture.StartCapture();
        }
        else
        {
            Debug.LogError("[VideoMirrorUI] mirrorCapture가 연결되지 않았습니다!");
        }

        // 패널 표시
        SetPanelVisible(true);

        // 검은 오버레이: alpha=1로 덮은 후 페이드아웃 (대기화면 가림)
        if (blackOverlay != null)
        {
            blackOverlay.color = new Color(0f, 0f, 0f, 1f);
            blackOverlay.gameObject.SetActive(true);

            float fadeDuration = (VideoManager.Instance != null) ? VideoManager.Instance.CrossfadeTime : 1f;

            if (_fadeCoroutine != null)
                StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeOutBlackOverlay(fadeDuration));
        }

        Debug.Log("[VideoMirrorUI] 미러 뷰 표시.");
    }

    /// <summary>
    /// 패널은 표시하지 않고 캡처만 시작합니다.
    /// 크로스페이드 대기 중 RT를 백그라운드에서 준비할 때 사용합니다.
    /// </summary>
    public void StartCaptureOnly()
    {
        // RawImage에 RenderTexture 연결 (미리 준비)
        if (mirrorRawImage != null && mirrorRT != null)
        {
            mirrorRawImage.texture = mirrorRT;
        }

        // 캡처 시작
        if (mirrorCapture != null)
        {
            mirrorCapture.StartCapture();
        }
        else
        {
            Debug.LogError("[VideoMirrorUI] mirrorCapture가 연결되지 않았습니다!");
        }

        Debug.Log("[VideoMirrorUI] 캡처만 시작 (패널 미표시).");
    }

    /// <summary>
    /// 미러 뷰를 숨깁니다. 캡처를 중지하여 GPU 부하를 해제합니다.
    /// </summary>
    public void Hide()
    {
        // 진행 중인 페이드 코루틴 정리
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        // 검은 오버레이 숨김
        if (blackOverlay != null)
            blackOverlay.gameObject.SetActive(false);

        // 캡처 중지
        if (mirrorCapture != null)
            mirrorCapture.StopCapture();

        // 패널 숨김
        SetPanelVisible(false);

        Debug.Log("[VideoMirrorUI] 미러 뷰 숨김.");
    }

    /// <summary>
    /// "목록으로 돌아가기" 버튼 클릭 시 호출됩니다.
    /// ForestShortcutUI에 이벤트를 전달하여 상태 전환을 위임합니다.
    /// </summary>
    private void OnBackToListClicked()
    {
        Debug.Log("[VideoMirrorUI] '목록으로 돌아가기' 버튼 클릭.");
        OnBackToListRequested?.Invoke();
    }

    /// <summary>
    /// 검은 오버레이를 서서히 투명하게 만듭니다.
    /// 영상 크로스페이드와 동일 시간 동안 진행되어
    /// 대기화면이 가려진 채로 결과 영상이 드러납니다.
    /// </summary>
    private IEnumerator FadeOutBlackOverlay(float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / duration);
            blackOverlay.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        // 완료: 완전 투명 + 비활성화
        blackOverlay.color = new Color(0f, 0f, 0f, 0f);
        blackOverlay.gameObject.SetActive(false);
        _fadeCoroutine = null;

        Debug.Log("[VideoMirrorUI] 검은 오버레이 페이드아웃 완료.");
    }

    /// <summary>
    /// 패널의 가시성을 CanvasGroup으로 제어합니다. (SetActive 미사용 → Canvas Rebuild 방지)
    /// </summary>
    private void SetPanelVisible(bool visible)
    {
        if (mirrorPanelCanvasGroup == null)
        {
            Debug.LogError("[VideoMirrorUI] mirrorPanelCanvasGroup이 연결되지 않았습니다!");
            return;
        }

        mirrorPanelCanvasGroup.alpha = visible ? 1f : 0f;
        mirrorPanelCanvasGroup.blocksRaycasts = visible;
        mirrorPanelCanvasGroup.interactable = visible;
    }
}
