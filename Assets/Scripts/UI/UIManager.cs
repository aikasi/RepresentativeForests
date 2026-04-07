using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// 터치 모니터의 UI 패널 전환을 제어합니다.
/// 터치 잠금, 무입력 자동 복귀 기능을 포함합니다.
/// 퀴즈 결과 판별은 QuizManager에게 위임합니다.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("===== UI 패널 참조 (Inspector에서 드래그 연결) =====")]
    [SerializeField] private GameObject standbyPanel;    // 00: 대기 화면
    [SerializeField] private GameObject q1Panel;         // 01: Q1 퀴즈
    [SerializeField] private GameObject q2Panel;         // 02: Q2 퀴즈
    [SerializeField] private GameObject q3Panel;         // 03: Q3 퀴즈
    [SerializeField] private GameObject q4Panel;         // 04: Q4 퀴즈
    [SerializeField] private GameObject loadingPanel;    // 05: 로딩 화면
    [SerializeField] private GameObject resultPanel;     // 06: 결과 화면

    [Header("===== 공통 버튼 참조 =====")]
    [SerializeField] private Button startButton;         // 대기 화면의 시작 버튼
    [SerializeField] private Button homeButton;          // 결과 화면의 처음으로(홈) 버튼

    [Header("===== 터치 잠금 (크로스페이드 중 입력 차단) =====")]
    [Tooltip("터치 캔버스의 최상위 CanvasGroup. 잠금 시 interactable=false로 전체 입력 차단.")]
    [SerializeField] private CanvasGroup touchCanvasGroup;

    // 전환 중 중복 입력 방지 플래그
    private bool _isTransitioning = false;

    // 무입력 자동 복귀 타이머
    private float _inactivityTimeout;   // 무입력 제한 시간 (CSVReader에서 로드)
    private float _inactivityTimer;     // 현재 무입력 경과 시간
    private bool _isInactivityEnabled;  // 타이머 활성화 여부
    private bool _isOnStandby = true;   // 현재 대기 화면인지 (대기 화면에서는 타이머 불필요)

    // 패널 배열 (인덱스로 빠르게 접근하기 위한 캐시)
    private GameObject[] _allPanels;

    // 퀴즈 패널 배열 (Q1~Q4 순서)
    private GameObject[] _quizPanels;

    // 임시 로딩 코루틴 참조 (중복 방지 및 인터럽트용)
    private Coroutine _loadingCoroutine;

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
        // 패널 배열 초기화 (일괄 관리용)
        _allPanels = new GameObject[]
        {
            standbyPanel, q1Panel, q2Panel, q3Panel, q4Panel, loadingPanel, resultPanel
        };

        // 퀴즈 패널 배열 (순서 중요: 인덱스 0=Q1, 1=Q2, 2=Q3, 3=Q4)
        _quizPanels = new GameObject[] { q1Panel, q2Panel, q3Panel, q4Panel };

        // 버튼 이벤트 바인딩
        if (startButton != null)
            startButton.onClick.AddListener(OnStartButtonClicked);

        if (homeButton != null)
            homeButton.onClick.AddListener(OnHomeButtonClicked);

        // 퀴즈 완료 이벤트 구독
        if (QuizManager.Instance != null)
            QuizManager.Instance.OnQuizCompleted += OnQuizCompleted;

        // 무입력 자동 복귀 타이머 설정 (0이면 비활성화)
        _inactivityTimeout = CSVReader.GetFloatValue("InactivityTimeout", 60.0f);
        _isInactivityEnabled = (_inactivityTimeout > 0f);
        _inactivityTimer = 0f;

        // 초기 상태: 대기 화면만 활성화
        ShowPanel(standbyPanel);
        _isOnStandby = true;

        Debug.Log($"[UIManager] UI 초기화 완료. 무입력 복귀: {(_isInactivityEnabled ? $"{_inactivityTimeout}초" : "비활성화")}");
    }

    private void Update()
    {
        // 무입력 자동 복귀 감시 (대기 화면에서는 동작 안 함)
        if (!_isInactivityEnabled || _isOnStandby) return;

        // 터치 또는 마우스 클릭 감지 시 타이머 리셋
        if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            _inactivityTimer = 0f;
            return;
        }

        _inactivityTimer += Time.deltaTime;

        if (_inactivityTimer >= _inactivityTimeout)
        {
            _inactivityTimer = 0f;
            Debug.Log($"[UIManager] {_inactivityTimeout}초 무입력 → 대기 화면으로 자동 복귀.");
            ReturnToStandby();
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        if (QuizManager.Instance != null)
            QuizManager.Instance.OnQuizCompleted -= OnQuizCompleted;
    }
    // ========== 터치 잠금 (외부에서 호출) ==========

    /// <summary>
    /// 터치 입력을 전체 차단합니다. (크로스페이드, 로딩 중 호출)
    /// </summary>
    public void SetTouchLock(bool locked)
    {
        if (touchCanvasGroup != null)
        {
            touchCanvasGroup.interactable = !locked;
            touchCanvasGroup.blocksRaycasts = !locked;
        }
    }

    // ========== 공개 메서드 (버튼 및 외부에서 호출) ==========

    /// <summary>
    /// 퀴즈 선택지 버튼에서 호출합니다.
    /// 각 버튼의 OnClick 이벤트에 이 메서드를 연결하고 인자로 "A", "B", "C", "D"를 넣어주세요.
    /// </summary>
    public void OnChoiceSelected(string answer)
    {
        if (_isTransitioning)
        {
            Debug.LogWarning("[UIManager] 전환 중 중복 입력 차단됨.");
            return;
        }

        if (QuizManager.Instance == null)
        {
            Debug.LogError("[UIManager] QuizManager를 찾을 수 없습니다!");
            return;
        }

        _isTransitioning = true;

        // 퀴즈 매니저에 답변 전달
        QuizManager.Instance.SubmitAnswer(answer);

        // 마지막 문항이 아니면 다음 퀴즈 패널로 이동
        if (!QuizManager.Instance.IsQuizCompleted)
        {
            int nextIndex = QuizManager.Instance.CurrentQuestionIndex;
            if (nextIndex < _quizPanels.Length)
            {
                ShowPanel(_quizPanels[nextIndex]);
                Debug.Log($"[UIManager] Q{nextIndex + 1} 패널로 전환합니다.");
            }
        }
        // 마지막 문항이면 OnQuizCompleted 이벤트가 자동으로 처리

        _isTransitioning = false;
    }

    /// <summary>
    /// 로딩 화면을 표시합니다. (영상 프리로드 완료 대기용)
    /// </summary>
    public void ShowLoadingPanel()
    {
        ShowPanel(loadingPanel);
        Debug.Log("[UIManager] 로딩 화면 표시.");
    }

    /// <summary>
    /// 결과 화면을 표시합니다. (영상 크로스페이드 시작 시점에 호출)
    /// </summary>
    public void ShowResultPanel()
    {
        ShowPanel(resultPanel);
        Debug.Log("[UIManager] 결과 화면 표시.");
    }

    /// <summary>
    /// 대기 화면으로 복귀합니다. (홈 버튼에서 호출 — 영상 + 퀴즈 + UI 전부 리셋)
    /// </summary>
    public void ReturnToStandby()
    {
        // 진행 중인 로딩 코루틴이 있으면 즉시 중단 (홈 버튼 인터럽트 대비)
        if (_loadingCoroutine != null)
        {
            StopCoroutine(_loadingCoroutine);
            _loadingCoroutine = null;
        }

        if (QuizManager.Instance != null)
            QuizManager.Instance.ResetQuiz();

        // 영상 재생 중단 + 대기 영상으로 크로스페이드 복귀
        if (VideoManager.Instance != null)
            VideoManager.Instance.InterruptAndReturnToIdle();

        ShowPanel(standbyPanel);
        Debug.Log("[UIManager] 대기 화면으로 복귀 (홈 버튼).");
    }

    /// <summary>
    /// UI 패널만 대기 화면으로 전환합니다. (VideoManager에서 호출 — 순환 호출 방지)
    /// VideoManager가 이미 영상 복귀를 처리한 후 UI만 전환할 때 사용합니다.
    /// </summary>
    public void ShowStandbyPanelOnly()
    {
        if (_loadingCoroutine != null)
        {
            StopCoroutine(_loadingCoroutine);
            _loadingCoroutine = null;
        }

        ShowPanel(standbyPanel);
        Debug.Log("[UIManager] 대기 화면으로 복귀 (영상 시퀀스 완료).");
    }

    // ========== 내부 이벤트 핸들러 ==========

    /// <summary>
    /// 시작 버튼 클릭 시 호출됩니다.
    /// </summary>
    private void OnStartButtonClicked()
    {
        if (_isTransitioning) return;

        // 퀴즈 상태 초기화 후 Q1 패널 표시
        if (QuizManager.Instance != null)
            QuizManager.Instance.ResetQuiz();

        ShowPanel(q1Panel);
        Debug.Log("[UIManager] 시작 버튼 클릭 → Q1 퀴즈 화면으로 전환.");
    }

    /// <summary>
    /// 홈(처음으로) 버튼 클릭 시 호출됩니다.
    /// </summary>
    private void OnHomeButtonClicked()
    {
        if (_isTransitioning) return;

        ReturnToStandby();
        Debug.Log("[UIManager] 홈 버튼 클릭 → 대기 화면으로 복귀.");
    }

    /// <summary>
    /// QuizManager에서 퀴즈 완료 이벤트가 도착하면 호출됩니다.
    /// </summary>
    private void OnQuizCompleted(string resultId)
    {
        // 로딩 화면 표시
        ShowLoadingPanel();
        Debug.Log($"[UIManager] 퀴즈 완료! 결과 ID: {resultId} → 로딩 화면 전환.");

        // VideoManager가 영상 프리로드 완료 후 ShowResultPanel()을 호출합니다.
    }

    // VideoManager.CrossfadeCoroutine 내부에서 UIManager.ShowResultPanel()을 직접 호출합니다.

    // ========== 유틸리티 ==========

    /// <summary>
    /// 지정된 패널만 활성화하고 나머지는 전부 비활성화합니다.
    /// 널(Null) 체크를 포함하여 Inspector 미연결 시에도 다운을 방지합니다.
    /// </summary>
    private void ShowPanel(GameObject targetPanel)
    {
        foreach (GameObject panel in _allPanels)
        {
            if (panel == null)
            {
                Debug.LogWarning("[UIManager] Inspector에 연결되지 않은 패널이 있습니다! 확인해 주세요.");
                continue;
            }
            panel.SetActive(panel == targetPanel);
        }

        // 대기 화면 여부 업데이트 (무입력 타이머 제어용)
        _isOnStandby = (targetPanel == standbyPanel);
        _inactivityTimer = 0f;
    }
}
