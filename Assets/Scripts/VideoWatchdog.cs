using UnityEngine;
using RenderHeads.Media.AVProVideo;

/// <summary>
/// AVPro MediaPlayer의 프리징(멈춤) 상태를 실시간 감시하고 자동 복구합니다.
/// VideoManager의 현재 활성 플레이어만 감시하여, 백그라운드 버퍼링 중인 플레이어는 건드리지 않습니다.
/// 단일 책임: 영상 프리징 감지 및 복구만 수행합니다.
/// </summary>
public class VideoWatchdog : MonoBehaviour
{
    [Header("===== 감시 대상 (Inspector에서 연결 불필요 - 자동 추적) =====")]
    [Tooltip("VideoManager의 활성 플레이어를 자동으로 추적합니다.")]
    private MediaPlayer _targetPlayer;

    // 프리징 판단 기준 (CSVReader에서 로드)
    private float _stallThreshold;
    private int _maxRetryCount;

    // 내부 상태 추적 변수 (GC 방지를 위해 캐싱)
    private double _lastTime;
    private float _stallTimer;
    private int _retryCount;
    private bool _isMonitoring = false;

    private void Start()
    {
        // CSVReader에서 설정값 로드
        _stallThreshold = CSVReader.GetFloatValue("WatchdogTimeout", 5.0f);
        _maxRetryCount = CSVReader.GetIntValue("MaxRetryCount", 3);

        Debug.Log($"[VideoWatchdog] 초기화 완료. 감시 기준: {_stallThreshold}초, 최대 재시도: {_maxRetryCount}회");
    }

    private void Update()
    {
        if (!_isMonitoring || _targetPlayer == null) return;

        // 재생 상태가 아니면 감시 건너뛰기 (일시정지, 로딩 중 등)
        if (_targetPlayer.Control == null || !_targetPlayer.Control.IsPlaying()) return;

        CheckForStalls();
    }

    /// <summary>
    /// 감시 대상 플레이어를 설정하고 모니터링을 시작합니다.
    /// VideoManager에서 크로스페이드 완료 후 활성 플레이어가 바뀔 때마다 호출됩니다.
    /// </summary>
    public void SetTarget(MediaPlayer player)
    {
        _targetPlayer = player;
        _lastTime = 0;
        _stallTimer = 0f;
        _retryCount = 0;
        _isMonitoring = (player != null);

        if (_isMonitoring)
        {
            Debug.Log($"[VideoWatchdog] 감시 대상 변경: {player.gameObject.name}");
        }
    }

    /// <summary>
    /// 감시를 일시 중단합니다. (크로스페이드 진행 중 등)
    /// </summary>
    public void PauseMonitoring()
    {
        _isMonitoring = false;
    }

    /// <summary>
    /// 감시를 재개합니다.
    /// </summary>
    public void ResumeMonitoring()
    {
        if (_targetPlayer != null)
        {
            _stallTimer = 0f;
            _isMonitoring = true;
        }
    }

    /// <summary>
    /// 영상 시간이 진행되고 있는지 매 프레임 체크합니다.
    /// 일정 시간 동안 시간이 변하지 않으면 프리징으로 간주합니다.
    /// GC 발생 최소화를 위해 임시 객체 생성 없이 캐싱된 변수만 비교합니다.
    /// </summary>
    private void CheckForStalls()
    {
        double currentTime = _targetPlayer.Control.GetCurrentTime();

        // 시간이 진행되고 있으면 정상 → 타이머 리셋
        if (!Mathf.Approximately((float)currentTime, (float)_lastTime))
        {
            _lastTime = currentTime;
            _stallTimer = 0f;
            _retryCount = 0;
            return;
        }

        // 시간 정지 감지 → 타이머 누적
        _stallTimer += Time.deltaTime;

        if (_stallTimer >= _stallThreshold)
        {
            _retryCount++;
            _stallTimer = 0f;

            if (_retryCount <= _maxRetryCount)
            {
                // 복구 시도: Play() 재실행
                Debug.LogWarning($"[VideoWatchdog] 프리징 감지! 복구 시도 {_retryCount}/{_maxRetryCount}회...");
                RecoverVideo();
            }
            else
            {
                // 최대 재시도 초과 → 강제 영상 재로드
                Debug.LogError($"[VideoWatchdog] 최대 재시도({_maxRetryCount}회) 초과! 영상을 강제로 다시 로드합니다.");
                ForceReload();
            }
        }
    }

    /// <summary>
    /// Play()를 재실행하여 영상 복구를 시도합니다.
    /// </summary>
    private void RecoverVideo()
    {
        if (_targetPlayer.Control != null)
        {
            _targetPlayer.Control.Play();
        }
    }

    /// <summary>
    /// 영상을 완전히 닫고 다시 여는 강제 복구를 수행합니다.
    /// </summary>
    private void ForceReload()
    {
        if (_targetPlayer == null) return;

        // 현재 재생 중인 영상의 경로를 저장
        string currentPath = _targetPlayer.MediaPath.Path;
        bool isLooping = _targetPlayer.Loop;

        if (string.IsNullOrEmpty(currentPath))
        {
            Debug.LogError("[VideoWatchdog] 강제 복구 실패: 현재 영상 경로를 알 수 없습니다.");
            return;
        }

        // 영상 완전 해제 후 재로드
        _targetPlayer.CloseMedia();
        _targetPlayer.Loop = isLooping;
        _targetPlayer.OpenMedia(
            new MediaPath(currentPath, MediaPathType.AbsolutePathOrURL),
            autoPlay: true
        );

        // ★ 볼륨 복원 (크로스페이드 중 프리징 시 볼륨이 중간값일 수 있음)
        if (_targetPlayer.Control != null)
            _targetPlayer.Control.SetVolume(1f);

        // 재시도 카운터 리셋
        _retryCount = 0;
        _lastTime = 0;

        Debug.Log($"[VideoWatchdog] 영상 강제 재로드 완료: {System.IO.Path.GetFileName(currentPath)}");
    }
}