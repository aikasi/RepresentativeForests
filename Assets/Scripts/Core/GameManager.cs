using UnityEngine;

/// <summary>
/// 어플리케이션의 초기화 및 글로벌 상태를 관리하는 메인 스크립트입니다.
/// CSVReader가 (-1000 순위로) 셋팅 구문을 읽은 뒤, 이 스크립트가 해상도 등을 화면에 적용합니다.
/// 퀴즈 완료 → 영상 시작 등 시스템 간 연결(브릿지) 역할을 수행합니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        // 씬 전환에도 GameManager가 파괴되지 않도록 싱글톤 유지
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 프로그램 진입 시 해상도 세팅 강제 적용
        ApplyStartupSettings();
    }

    private void Start()
    {
        // 퀴즈 완료 이벤트 구독 → 영상 시퀀스 시작 명령
        if (QuizManager.Instance != null)
        {
            QuizManager.Instance.OnQuizCompleted += OnQuizCompleted;
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        if (QuizManager.Instance != null)
        {
            QuizManager.Instance.OnQuizCompleted -= OnQuizCompleted;
        }
    }

    private void ApplyStartupSettings()
    {
        // CSVReader에서 파싱한 값을 꺼냅니다. 값이 없으면 우측의 기본값을 사용합니다.
        int resX = CSVReader.GetIntValue("ResolutionX", 1920);
        int resY = CSVReader.GetIntValue("ResolutionY", 1080);
        bool isFullScreen = CSVReader.GetStringValue("FullScreen", "true").ToLower() == "true";
        bool hideCursor = CSVReader.GetStringValue("HideCursor", "false").ToLower() == "true";
        
        // 서브 모니터 설정 추가
        bool useSubMonitor = CSVReader.GetStringValue("UseSubMonitor", "false").ToLower() == "true";
        int subResX = CSVReader.GetIntValue("SubResolutionX", 1920);
        int subResY = CSVReader.GetIntValue("SubResolutionY", 1080);

        // 메인 모니터 적용
        Screen.SetResolution(resX, resY, isFullScreen);

        // 서브 모니터 활성화 (연결된 모니터가 2개 이상일 때만)
        if (useSubMonitor && Display.displays.Length > 1)
        {
            Display.displays[1].Activate(subResX, subResY, 60); // 기본 주사율 60Hz로 고정
            Debug.Log($"[GameManager] 서브 모니터 활성화 완료: {subResX}x{subResY}");
        }
        else if (useSubMonitor && Display.displays.Length <= 1)
        {
            Debug.LogWarning("[GameManager] 서브 모니터 설정을 켰으나 물리적으로 2번째 모니터가 연결되어 있지 않습니다!");
        }

        // 커서 숨김 옵션 적용
        Cursor.visible = !hideCursor;

        Debug.Log($"[GameManager] 키오스크 해상도 적용 완료: 메인 {resX}x{resY} (전체화면: {isFullScreen}, 커서숨김: {hideCursor})");
    }

    /// <summary>
    /// 퀴즈 완료 시 호출되어 영상 시퀀스 재생을 시작합니다.
    /// videoId로 영상을 재생하고, imageId는 UIManager가 별도로 처리합니다.
    /// </summary>
    private void OnQuizCompleted(string videoId, string imageId)
    {
        Debug.Log($"[GameManager] 퀴즈 결과 수신: 영상={videoId}, 이미지={imageId} → 영상 시퀀스 시작 지시.");

        if (VideoManager.Instance != null)
        {
            VideoManager.Instance.StartResultSequence(videoId);
        }
        else
        {
            Debug.LogWarning("[GameManager] VideoManager가 없습니다. 영상 재생을 건너뜁니다.");
        }
    }
}
