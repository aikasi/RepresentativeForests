using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유형별 숲 바로가기 패널의 상태 관리, 버튼 이벤트, 무입력 타이머를 전담합니다.
/// UIManager의 ShowPanel 시스템과 독립된 오버레이 방식으로 동작합니다.
/// 단일 책임: 바로가기 패널의 열기/닫기, 버튼 비주얼, 영상 연동만 수행합니다.
/// </summary>
public class ForestShortcutUI : MonoBehaviour
{
    [Header("===== 바로가기 패널 참조 =====")]
    [Tooltip("바로가기 패널 전체를 감싸는 CanvasGroup (alpha로 표시/숨김 제어)")]
    [SerializeField] private CanvasGroup shortcutPanelCanvasGroup;

    [Header("===== 숲 카테고리 버튼 (4개) =====")]
    [Tooltip("인덱스 순서: 0=100대 명품숲, 1=명품숲길50, 2=자연휴양림, 3=수목원·정원")]
    [SerializeField] private Button[] forestButtons;

    [Header("===== 버튼 이미지 교체용 =====")]
    [Tooltip("각 버튼의 Image 컴포넌트 (forestButtons와 동일 순서)")]
    [SerializeField] private Image[] forestButtonImages;
    [Tooltip("기본 상태 스프라이트 (4개, forestButtons와 동일 순서)")]
    [SerializeField] private Sprite[] normalSprites;
    [Tooltip("눌림 상태 스프라이트 (4개, forestButtons와 동일 순서)")]
    [SerializeField] private Sprite[] pressedSprites;

    [Header("===== 뒤로가기 버튼 =====")]
    [SerializeField] private Button backButton;

    [Header("===== 카테고리 목록 영역 =====")]
    [Tooltip("카테고리 버튼들과 뒤로가기 버튼을 감싸는 CanvasGroup (미러 뷰 전환 시 숨김)")]
    [SerializeField] private CanvasGroup categoryListCanvasGroup;

    [Header("===== 영상 미러링 =====")]
    [Tooltip("터치 화면의 영상 미러링 패널 컴포넌트")]
    [SerializeField] private VideoMirrorUI videoMirrorUI;

    [Header("===== 카테고리 → 영상 ID 매핑 =====")]
    [Tooltip("forestButtons 인덱스에 대응하는 영상 ID (MediaScanner의 카테고리 키)")]
    [SerializeField] private string[] videoIds = { "01", "02", "03", "04" };

    [Header("===== 무입력 자동 닫기 =====")]
    [Tooltip("바로가기 패널의 무입력 자동 닫기 제한 시간 (초). 0이면 비활성화.")]
    [SerializeField] private float inactivityTimeout = 60f;

    // 패널 열림 상태
    private bool _isOpen = false;

    // 현재 선택된 버튼 인덱스 (-1 = 선택 없음)
    private int _selectedIndex = -1;

    // 무입력 타이머 관련
    private float _inactivityTimer = 0f;
    private bool _isTimerPaused = false;  // 영상 재생 중 타이머 일시정지

    // 현재 미러 뷰가 표시 중인지 여부
    private bool _isInVideoView = false;

    private void Awake()
    {
        // 유효성 검사: 배열 길이 일치 확인
        if (forestButtons == null || forestButtonImages == null ||
            normalSprites == null || pressedSprites == null || videoIds == null)
        {
            Debug.LogError("[ForestShortcutUI] Inspector에 연결되지 않은 배열이 있습니다! 확인해 주세요.");
            return;
        }

        int buttonCount = forestButtons.Length;
        if (forestButtonImages.Length != buttonCount ||
            normalSprites.Length != buttonCount ||
            pressedSprites.Length != buttonCount ||
            videoIds.Length != buttonCount)
        {
            Debug.LogError($"[ForestShortcutUI] 배열 길이 불일치! 버튼: {buttonCount}, " +
                           $"이미지: {forestButtonImages.Length}, 기본스프라이트: {normalSprites.Length}, " +
                           $"눌림스프라이트: {pressedSprites.Length}, 영상ID: {videoIds.Length}");
        }
    }

    private void Start()
    {
        // 버튼 이벤트 바인딩 (클로저로 인덱스 캡처)
        for (int i = 0; i < forestButtons.Length; i++)
        {
            if (forestButtons[i] == null) continue;
            int index = i;  // 클로저 캡처용 로컬 변수 (GC-free)
            forestButtons[i].onClick.AddListener(() => OnForestButtonClicked(index));
        }

        if (backButton != null)
            backButton.onClick.AddListener(OnBackButtonClicked);

        // VideoManager 이벤트 구독 (바로가기 시퀀스 완료 알림)
        if (VideoManager.Instance != null)
            VideoManager.Instance.OnShortcutSequenceCompleted += OnShortcutSequenceCompleted;

        // VideoMirrorUI '목록으로' 이벤트 구독
        if (videoMirrorUI != null)
            videoMirrorUI.OnBackToListRequested += OnBackToListFromMirror;

        // 초기 상태: 패널 숨김
        SetPanelVisible(false);

        Debug.Log($"[ForestShortcutUI] 초기화 완료. 버튼 {forestButtons.Length}개, 무입력 제한: {inactivityTimeout}초");
    }

    private void Update()
    {
        // 패널이 닫혀있거나, 영상 재생 중(타이머 일시정지)이면 타이머 비작동
        if (!_isOpen || _isTimerPaused || inactivityTimeout <= 0f) return;

        // 터치/마우스 입력 감지
        bool hasInput = false;

#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null &&
            UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            hasInput = true;
        if (UnityEngine.InputSystem.Touchscreen.current != null &&
            UnityEngine.InputSystem.Touchscreen.current.touches.Count > 0)
            hasInput = true;
#else
        if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
            hasInput = true;
#endif

        // 입력 감지 시 타이머 리셋
        if (hasInput)
        {
            _inactivityTimer = 0f;
            return;
        }

        _inactivityTimer += Time.deltaTime;

        if (_inactivityTimer >= inactivityTimeout)
        {
            Debug.Log($"[ForestShortcutUI] {inactivityTimeout}초 무입력 → 바로가기 패널 자동 닫기.");
            _inactivityTimer = 0f;
            OnBackButtonClicked();  // 뒤로가기와 동일한 동작 수행
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        if (VideoManager.Instance != null)
            VideoManager.Instance.OnShortcutSequenceCompleted -= OnShortcutSequenceCompleted;

        if (videoMirrorUI != null)
            videoMirrorUI.OnBackToListRequested -= OnBackToListFromMirror;
    }

    // ========== 공개 메서드 (UIManager에서 호출) ==========

    /// <summary>
    /// 바로가기 패널을 엽니다. UIManager.OnShortcutOpenButtonClicked()에서 호출합니다.
    /// </summary>
    public void Open()
    {
        if (_isOpen) return;

        // 버튼 비주얼 초기화 (모두 기본 상태)
        ResetAllButtonVisuals();
        _selectedIndex = -1;

        // 패널 표시
        SetPanelVisible(true);
        _isOpen = true;

        // 무입력 타이머 시작
        _inactivityTimer = 0f;
        _isTimerPaused = false;

        Debug.Log("[ForestShortcutUI] 바로가기 패널 열림.");
    }

    /// <summary>
    /// 외부에서 강제로 패널을 닫습니다. (영상 인터럽트는 하지 않음)
    /// UIManager.ReturnToStandby() 등 외부 시스템 리셋 시 사용합니다.
    /// </summary>
    public void ForceClose()
    {
        if (!_isOpen) return;

        // 미러 뷰가 열려있으면 함께 정리
        if (_isInVideoView && videoMirrorUI != null)
        {
            videoMirrorUI.Hide();
            _isInVideoView = false;
        }

        ResetAllButtonVisuals();
        _selectedIndex = -1;
        _isTimerPaused = false;
        _inactivityTimer = 0f;

        SetPanelVisible(false);
        _isOpen = false;

        Debug.Log("[ForestShortcutUI] 바로가기 패널 강제 닫힘 (외부 호출).");
    }

    // ========== 내부 이벤트 핸들러 ==========

    /// <summary>
    /// 숲 카테고리 버튼 클릭 시 호출됩니다.
    /// </summary>
    private void OnForestButtonClicked(int index)
    {
        // 같은 버튼 재클릭 시 무시 (이미 해당 영상 재생 중)
        if (_selectedIndex == index && VideoManager.Instance != null && VideoManager.Instance.IsSequencePlaying)
        {
            Debug.Log($"[ForestShortcutUI] 동일 버튼 재클릭 무시 (인덱스: {index}).");
            return;
        }

        // 범위 유효성 검사
        if (index < 0 || index >= videoIds.Length)
        {
            Debug.LogError($"[ForestShortcutUI] 유효하지 않은 버튼 인덱스: {index}");
            return;
        }

        // 모든 버튼 기본 상태로 리셋 후, 클릭된 버튼만 눌림 상태로 교체
        ResetAllButtonVisuals();
        SetButtonVisual(index, true);
        _selectedIndex = index;

        // 무입력 타이머 일시정지 (영상 재생 중에는 자동 닫기 비활성화)
        _isTimerPaused = true;

        // 카테고리 목록 → 미러 뷰 전환 (검은 오버레이가 대기화면을 가림)
        SwitchToVideoView();

        // 영상 시퀀스 시작 (바로가기 모드)
        if (VideoManager.Instance != null)
        {
            VideoManager.Instance.StartResultSequence(videoIds[index], true);
            Debug.Log($"[ForestShortcutUI] 숲 버튼 클릭: 인덱스={index}, 영상ID={videoIds[index]}");
        }
        else
        {
            Debug.LogError("[ForestShortcutUI] VideoManager를 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 뒤로가기 버튼 클릭 또는 무입력 타이머 만료 시 호출됩니다.
    /// </summary>
    private void OnBackButtonClicked()
    {
        if (!_isOpen) return;

        // 영상 재생 중이면 인터럽트하여 대기 영상으로 복귀
        if (VideoManager.Instance != null && VideoManager.Instance.IsSequencePlaying)
        {
            VideoManager.Instance.InterruptAndReturnToIdle();
            Debug.Log("[ForestShortcutUI] 뒤로가기: 영상 인터럽트 → 대기 영상 복귀.");
        }
        else
        {
            Debug.Log("[ForestShortcutUI] 뒤로가기: 대기 영상 재생 중 → UI만 닫기.");
        }

        // 비주얼 리셋 및 패널 닫기
        ResetAllButtonVisuals();
        _selectedIndex = -1;
        _isTimerPaused = false;
        _inactivityTimer = 0f;

        SetPanelVisible(false);
        _isOpen = false;

        // UIManager에 패널 닫힘 알림
        if (UIManager.Instance != null)
            UIManager.Instance.OnShortcutPanelClosed();
    }

    /// <summary>
    /// VideoManager에서 바로가기 시퀀스 완료 이벤트가 도착하면 호출됩니다.
    /// 영상이 전부 재생 완료 → 버튼 비주얼 복원 + 무입력 타이머 재시작
    /// </summary>
    private void OnShortcutSequenceCompleted()
    {
        Debug.Log("[ForestShortcutUI] 바로가기 시퀀스 완료 → 미러 뷰 닫기, 카테고리 목록 복귀.");

        // 미러 뷰 → 카테고리 목록 복귀
        SwitchToCategoryList();

        // 선택된 버튼의 눌림 표시 해제
        ResetAllButtonVisuals();
        _selectedIndex = -1;

        // 무입력 타이머 재활성화 및 리셋
        _isTimerPaused = false;
        _inactivityTimer = 0f;
    }

    /// <summary>
    /// VideoMirrorUI의 '목록으로 돌아가기' 버튼에서 호출됩니다.
    /// 영상을 인터럽트하고 카테고리 목록으로 복귀합니다.
    /// </summary>
    private void OnBackToListFromMirror()
    {
        Debug.Log("[ForestShortcutUI] '목록으로' 요청 → 영상 인터럽트 + 카테고리 목록 복귀.");

        // 영상 재생 중이면 인터럽트
        if (VideoManager.Instance != null && VideoManager.Instance.IsSequencePlaying)
        {
            VideoManager.Instance.InterruptAndReturnToIdle();
        }

        // 미러 뷰 → 카테고리 목록 복귀
        SwitchToCategoryList();

        // 버튼 비주얼 리셋
        ResetAllButtonVisuals();
        _selectedIndex = -1;

        // 무입력 타이머 재시작
        _isTimerPaused = false;
        _inactivityTimer = 0f;
    }

    // ========== 상태 전환 ==========

    /// <summary>
    /// 카테고리 목록을 숨기고 미러 뷰를 표시합니다.
    /// </summary>
    private void SwitchToVideoView()
    {
        // 카테고리 목록 숨김
        SetCanvasGroupVisible(categoryListCanvasGroup, false);

        // 미러 뷰 표시
        if (videoMirrorUI != null)
            videoMirrorUI.Show();

        _isInVideoView = true;
        Debug.Log("[ForestShortcutUI] 상태 전환: CategoryList → VideoView.");
    }

    /// <summary>
    /// 미러 뷰를 숨기고 카테고리 목록을 다시 표시합니다.
    /// </summary>
    private void SwitchToCategoryList()
    {
        // 미러 뷰 숨김
        if (videoMirrorUI != null)
            videoMirrorUI.Hide();

        // 카테고리 목록 표시
        SetCanvasGroupVisible(categoryListCanvasGroup, true);

        _isInVideoView = false;
        Debug.Log("[ForestShortcutUI] 상태 전환: VideoView → CategoryList.");
    }

    // ========== 유틸리티 ==========

    /// <summary>
    /// 패널의 가시성을 CanvasGroup으로 제어합니다. (SetActive 미사용 → Canvas Rebuild 방지)
    /// </summary>
    private void SetPanelVisible(bool visible)
    {
        if (shortcutPanelCanvasGroup == null)
        {
            Debug.LogError("[ForestShortcutUI] shortcutPanelCanvasGroup이 연결되지 않았습니다!");
            return;
        }

        shortcutPanelCanvasGroup.alpha = visible ? 1f : 0f;
        shortcutPanelCanvasGroup.blocksRaycasts = visible;
        shortcutPanelCanvasGroup.interactable = visible;
    }

    /// <summary>
    /// 범용 CanvasGroup 가시성 제어 유틸리티입니다.
    /// </summary>
    private void SetCanvasGroupVisible(CanvasGroup cg, bool visible)
    {
        if (cg == null) return;

        cg.alpha = visible ? 1f : 0f;
        cg.blocksRaycasts = visible;
        cg.interactable = visible;
    }

    /// <summary>
    /// 지정된 버튼의 이미지를 기본 또는 눌림 스프라이트로 교체합니다.
    /// </summary>
    private void SetButtonVisual(int index, bool pressed)
    {
        if (index < 0 || index >= forestButtonImages.Length) return;
        if (forestButtonImages[index] == null) return;

        Sprite targetSprite = pressed ? pressedSprites[index] : normalSprites[index];
        if (targetSprite != null)
        {
            forestButtonImages[index].sprite = targetSprite;
        }
        else
        {
            Debug.LogWarning($"[ForestShortcutUI] 스프라이트가 null입니다: 인덱스={index}, 눌림={pressed}");
        }
    }

    /// <summary>
    /// 모든 버튼의 이미지를 기본 상태로 복원합니다.
    /// </summary>
    private void ResetAllButtonVisuals()
    {
        for (int i = 0; i < forestButtonImages.Length; i++)
        {
            SetButtonVisual(i, false);
        }
    }
}
