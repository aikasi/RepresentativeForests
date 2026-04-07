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

        Screen.SetResolution(resX, resY, isFullScreen);

        Debug.Log($"[GameManager] 키오스크 해상도 적용 완료: {resX}x{resY} (전체화면: {isFullScreen})");
    }

    /// <summary>
    /// 퀴즈 완료 시 호출되어 영상 시퀀스 재생을 시작합니다.
    /// </summary>
    private void OnQuizCompleted(string resultId)
    {
        Debug.Log($"[GameManager] 퀴즈 결과 수신: {resultId}번 → 영상 시퀀스 시작 지시.");

        if (VideoManager.Instance != null)
        {
            VideoManager.Instance.StartResultSequence(resultId);
        }
        else
        {
            Debug.LogWarning("[GameManager] VideoManager가 없습니다. 영상 재생을 건너뜁니다.");
        }
    }
}
