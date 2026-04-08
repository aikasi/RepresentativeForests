using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 키오스크 환경에서 에러 메시지를 화면에 표시하고 자동으로 소멸시킵니다.
/// 에러 큐를 관리하여 여러 에러가 동시에 발생해도 순서대로 보여줍니다.
/// 단일 책임: 에러 메시지 표시 및 소멸만 수행합니다.
/// </summary>
public class ErrorPopup : MonoBehaviour
{
    public static ErrorPopup Instance { get; private set; }

    [Header("===== UI 참조 (Inspector에서 드래그 연결) =====")]
    [SerializeField] private GameObject popupPanel;           // 팝업 패널 루트
    [SerializeField] private TextMeshProUGUI messageText;     // 에러 메시지 텍스트
    [SerializeField] private CanvasGroup popupCanvasGroup;    // 페이드 인/아웃용
    [Tooltip("에러 팝업을 수동으로 닫을 닫기 버튼")]
    [SerializeField] private Button closeButton;              

    [Header("===== 팝업 설정 =====")]
    [SerializeField] private float fadeTime = 0.3f;           // 페이드 애니메이션 시간

    // 에러 메시지 큐 (동시 다발 에러 대응)
    private Queue<string> _messageQueue = new Queue<string>();
    private bool _isShowing = false;
    private bool _isCloseRequested = false;
    private Coroutine _popupCoroutine;

    private void Awake()
    {
        // 싱글톤 처리
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // 닫기 버튼 이벤트 연결
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }

        // 초기 상태: 팝업 숨김
        if (popupPanel != null)
            popupPanel.SetActive(false);

        if (popupCanvasGroup != null)
            popupCanvasGroup.alpha = 0f;
    }

    // ========== 공개 메서드 ==========

    /// <summary>
    /// 에러 메시지를 화면에 표시합니다.
    /// 이미 팝업이 표시 중이면 큐에 추가하여 순서대로 보여줍니다.
    /// Logger에도 동시에 기록됩니다.
    /// </summary>
    public static void Show(string message)
    {
        // 안전 체크: Instance가 없으면 콘솔에만 출력
        if (Instance == null)
        {
            Debug.LogError($"[ErrorPopup] Instance 없음. 메시지: {message}");
            return;
        }

        Instance.EnqueueMessage(message);
    }

    /// <summary>
    /// 현재 표시 중인 팝업과 큐를 전부 초기화합니다.
    /// 화면 전환 시 잔여 팝업을 깨끗이 정리할 때 사용합니다.
    /// </summary>
    public void ClearAll()
    {
        _messageQueue.Clear();

        if (_popupCoroutine != null)
        {
            StopCoroutine(_popupCoroutine);
            _popupCoroutine = null;
        }

        _isShowing = false;

        if (popupPanel != null)
            popupPanel.SetActive(false);

        if (popupCanvasGroup != null)
            popupCanvasGroup.alpha = 0f;
    }

    // ========== 내부 로직 ==========

    private void EnqueueMessage(string message)
    {
        // Logger에도 기록 (파일 로그)
        Debug.LogError($"[ErrorPopup] {message}");

        _messageQueue.Enqueue(message);

        // 현재 표시 중이 아니면 즉시 시작
        if (!_isShowing)
        {
            ShowNextMessage();
        }
    }

    private void ShowNextMessage()
    {
        if (_messageQueue.Count == 0)
        {
            _isShowing = false;
            return;
        }

        _isShowing = true;
        string message = _messageQueue.Dequeue();

        _popupCoroutine = StartCoroutine(ShowPopupCoroutine(message));
    }

    private IEnumerator ShowPopupCoroutine(string message)
    {
        // 메시지 설정
        if (messageText != null)
            messageText.text = message;

        // 팝업 활성화
        if (popupPanel != null)
            popupPanel.SetActive(true);

        // 페이드 인
        yield return StartCoroutine(FadeCoroutine(0f, 1f));

        // 사용자가 닫기 버튼을 누를 때까지 무한 대기
        _isCloseRequested = false;
        yield return new WaitUntil(() => _isCloseRequested);

        // 페이드 아웃
        yield return StartCoroutine(FadeCoroutine(1f, 0f));

        // 팝업 비활성화
        if (popupPanel != null)
            popupPanel.SetActive(false);

        _popupCoroutine = null;

        // 큐에 남은 메시지가 있으면 다음 표시
        ShowNextMessage();
    }

    /// <summary>
    /// 닫기 버튼을 클릭했을 때 호출됩니다.
    /// </summary>
    private void OnCloseButtonClicked()
    {
        if (_isShowing)
        {
            _isCloseRequested = true;
        }
    }

    /// <summary>
    /// CanvasGroup Alpha를 부드럽게 전환합니다.
    /// </summary>
    private IEnumerator FadeCoroutine(float from, float to)
    {
        if (popupCanvasGroup == null) yield break;

        float elapsed = 0f;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeTime);
            popupCanvasGroup.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        popupCanvasGroup.alpha = to;
    }
}
