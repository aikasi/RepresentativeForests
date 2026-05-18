using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RenderHeads.Media.AVProVideo;

/// <summary>
/// 두 개의 AVPro MediaPlayer를 핑퐁(Ping-Pong) 방식으로 교차하며
/// 크로스페이드 전환 및 연속 재생을 담당합니다.
/// 크로스페이드 완료 즉시 다음 영상을 사전 로딩하여 검은 화면을 차단합니다.
/// 단일 책임: 영상 로드, 재생, 크로스페이드 전환만 수행합니다.
/// </summary>
public class VideoManager : MonoBehaviour
{
    public static VideoManager Instance { get; private set; }

    [Header("===== AVPro MediaPlayer (Inspector에서 드래그 연결) =====")]
    [SerializeField] private MediaPlayer playerA;
    [SerializeField] private MediaPlayer playerB;

    [Header("===== 비디오 레이어 Alpha 제어용 CanvasGroup =====")]
    [Tooltip("PlayerA의 DisplayUGUI가 포함된 오브젝트의 CanvasGroup")]
    [SerializeField] private CanvasGroup canvasGroupA;
    [Tooltip("PlayerB의 DisplayUGUI가 포함된 오브젝트의 CanvasGroup")]
    [SerializeField] private CanvasGroup canvasGroupB;

    [Header("===== 프리징 감시 (선택사항) =====")]
    [Tooltip("VideoWatchdog 오브젝트를 연결하면 프리징 자동 감시가 활성화됩니다.")]
    [SerializeField] private VideoWatchdog watchdog;

    // 현재 화면에 보이는(활성) 플레이어와 백그라운드 플레이어
    private MediaPlayer _activePlayer;
    private MediaPlayer _backgroundPlayer;
    private CanvasGroup _activeCanvasGroup;
    private CanvasGroup _backgroundCanvasGroup;

    // 크로스페이드 시간 (CSVReader에서 로드)
    private float _crossfadeTime = 1.0f;
    private float _preloadDelayTime = 2.0f;

    // 현재 재생 중인 시퀀스 리스트 및 인덱스
    private List<string> _currentSequence;
    private int _currentSequenceIndex;

    // 상태 플래그
    private bool _isCrossfading = false;
    private bool _isSequencePlaying = false;
    private bool _isIdleSelfCrossfading = false;
    private bool _isPreloadReady = false; // 백그라운드 플레이어가 첫 프레임까지 준비 완료

    // 대기 영상 다중 루프용 인덱스 (IdleVideoPaths 리스트 내 현재 위치)
    private int _idleSequenceIndex = 0;

    // 외부에서 현재 결과 영상 시퀀스 재생 중인지 확인 (퀴즈 중 홈 버튼 분기용)
    public bool IsSequencePlaying => _isSequencePlaying;

    // 코루틴 참조 (인터럽트용)
    private Coroutine _crossfadeCoroutine;
    private Coroutine _preloadCoroutine;
    private Coroutine _delayedPreloadCoroutine;

    // 영상 시퀀스 완료 시 외부 알림 이벤트
    public event Action OnSequenceCompleted;

    private void Awake()
    {
        // 싱글톤 처리
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // AVPro의 자동 재생을 강제로 끄기
        if (playerA != null) { playerA.AutoOpen = false; playerA.AutoStart = false; }
        if (playerB != null) { playerB.AutoOpen = false; playerB.AutoStart = false; }
    }

    private void Start()
    {
        _crossfadeTime = CSVReader.GetFloatValue("CrossfadeTime", 1.0f);
        _preloadDelayTime = CSVReader.GetFloatValue("PreloadDelayTime", 2.0f);

        // 초기 상태: Player A가 활성, Player B가 백그라운드
        _activePlayer = playerA;
        _backgroundPlayer = playerB;
        _activeCanvasGroup = canvasGroupA;
        _backgroundCanvasGroup = canvasGroupB;

        canvasGroupA.alpha = 1f;
        canvasGroupB.alpha = 0f;

        playerA.Events.AddListener(OnPlayerEvent);
        playerB.Events.AddListener(OnPlayerEvent);

        StartCoroutine(WaitForMediaScannerAndStart());

        Debug.Log($"[VideoManager] AVPro v3 초기화 완료. 크로스페이드 시간: {_crossfadeTime}초");
    }

    private IEnumerator WaitForMediaScannerAndStart()
    {
        float timeout = 5f;
        float elapsed = 0f;
        while ((MediaScanner.Instance == null || MediaScanner.Instance.IdleVideoPaths == null || MediaScanner.Instance.IdleVideoPaths.Count == 0) && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (MediaScanner.Instance == null || MediaScanner.Instance.IdleVideoPaths == null || MediaScanner.Instance.IdleVideoPaths.Count == 0)
        {
            Debug.LogError("[VideoManager] MediaScanner 준비 타임아웃! 대기 영상을 재생할 수 없습니다.");
            yield break;
        }

        StartIdleVideo();

        if (watchdog != null)
            watchdog.SetTarget(_activePlayer);
    }

    private void OnDestroy()
    {
        if (playerA != null) playerA.Events.RemoveListener(OnPlayerEvent);
        if (playerB != null) playerB.Events.RemoveListener(OnPlayerEvent);
    }

    // ========== 공개 메서드 ==========

    /// <summary>
    /// 대기 영상을 재생합니다. 재생 시작 직후 백그라운드에 같은 영상을 미리 로딩합니다.
    /// </summary>
    public void StartIdleVideo()
    {
        if (MediaScanner.Instance == null || MediaScanner.Instance.IdleVideoPaths == null || MediaScanner.Instance.IdleVideoPaths.Count == 0)
        {
            Debug.LogError("[VideoManager] 대기 영상 경로를 찾을 수 없습니다!");
            return;
        }

        _isSequencePlaying = false;
        _currentSequence = null;
        _isPreloadReady = false;
        _idleSequenceIndex = 0;

        string firstIdlePath = MediaScanner.Instance.IdleVideoPaths[0];

        _activePlayer.Loop = false;
        bool opened = _activePlayer.OpenMedia(
            new MediaPath(firstIdlePath, MediaPathType.AbsolutePathOrURL),
            autoPlay: true
        );

        if (!opened)
        {
            ErrorPopup.Show("대기 영상 열기에 실패했습니다!");
            return;
        }

        Debug.Log($"[VideoManager] 대기 영상 재생 시작: {System.IO.Path.GetFileName(firstIdlePath)} (백그라운드 프리로드는 안정화 후 진행)");

        // ★ 즉시 로딩하지 않고, PlayerA가 완전히 화면에 뜨고 안정화되면 프리로드 시작
        _delayedPreloadCoroutine = StartCoroutine(DelayedPreloadIdleVideo(GetNextIdleVideoPath()));
    }

    /// <summary>
    /// 빌드 초기 시작 시 동시에 2개의 미디어가 디스크 IO를 일으키는 병목을 방지합니다.
    /// </summary>
    private IEnumerator DelayedPreloadIdleVideo(string videoPath)
    {
        yield return new WaitForSeconds(_preloadDelayTime);

        if (!_isSequencePlaying && !_isCrossfading)
        {
            PreloadNextVideoImmediately(videoPath);
        }
    }

    /// <summary>
    /// 결과 영상 시퀀스 재생을 시작합니다.
    /// </summary>
    public void StartResultSequence(string resultId)
    {
        if (MediaScanner.Instance == null)
        {
            ErrorPopup.Show("MediaScanner를 찾을 수 없습니다!");
            UIManager.Instance?.ReturnToStandby();
            return;
        }

        if (!int.TryParse(resultId, out int categoryId))
        {
            Debug.LogError($"[VideoManager] 유효하지 않은 결과 ID: {resultId} (숫자가 아닙니다)");
            ErrorPopup.Show($"에러: 유효하지 않은 결과 ID ({resultId})");
            UIManager.Instance?.ReturnToStandby();
            return;
        }
        if (!MediaScanner.Instance.ResultVideos.ContainsKey(categoryId))
        {
            ErrorPopup.Show($"결과 ID {resultId}에 해당하는 영상이 없습니다!");
            UIManager.Instance?.ReturnToStandby();
            return;
        }

        // ★ 기존 상태를 완전히 정리 (크로스페이드 중이든, 프리로드 중이든 관계없이)
        if (_isIdleSelfCrossfading || _isCrossfading)
        {
            InterruptSelfCrossfade();
        }

        // ★ 반드시 기존 코루틴 중지 + 백그라운드 해제 + 프리로드 플래그 무효화
        // (InterruptSelfCrossfade 후에도 추가 실행하여 잔여 상태까지 모두 정리)
        StopAllVideoCoroutines();
        _backgroundPlayer.CloseMedia();
        _isPreloadReady = false;

        _currentSequence = MediaScanner.Instance.ResultVideos[categoryId];
        _currentSequenceIndex = 0;
        _isSequencePlaying = true;

        Debug.Log($"[VideoManager] 결과 시퀀스 시작: ID {resultId}, 총 {_currentSequence.Count}개 영상");

        // 첫 번째 결과 영상 프리로드 및 크로스페이드
        PreloadAndCrossfade(_currentSequence[0]);
    }

    /// <summary>
    /// 모든 것을 중단하고 대기 영상으로 복귀합니다. (홈 버튼)
    /// </summary>
    public void InterruptAndReturnToIdle()
    {
        Debug.Log("[VideoManager] 인터럽트! 대기 영상으로 강제 복귀합니다.");

        if (watchdog != null)
            watchdog.PauseMonitoring();

        StopAllVideoCoroutines();

        _isCrossfading = false;
        _isSequencePlaying = false;
        _isIdleSelfCrossfading = false;
        _isPreloadReady = false;
        _currentSequence = null;
        _idleSequenceIndex = 0;

        _backgroundPlayer.CloseMedia();

        // ★ 볼륨 복원 안전장치: 활성 플레이어 볼륨 1로 보장
        _activePlayer.Control?.SetVolume(1f);

        string firstIdle = GetFirstIdleVideoPath();
        if (!string.IsNullOrEmpty(firstIdle))
            PreloadAndCrossfade(firstIdle, false);
        else
            Debug.LogError("[VideoManager] 대기 영상 경로를 찾을 수 없어 복귀 실패!");
    }

    // ========== 사전 로딩 (즉시 트리거) ==========

    /// <summary>
    /// 크로스페이드 완료 직후 또는 첫 영상 재생 직후,
    /// 다음 영상을 백그라운드 플레이어에 즉시 로딩합니다.
    /// </summary>
    private void PreloadNextVideoImmediately(string nextPath)
    {
        if (string.IsNullOrEmpty(nextPath))
        {
            Debug.LogWarning("[VideoManager] 사전 로딩: 다음 영상 경로가 비어있습니다.");
            return;
        }

        _isPreloadReady = false;
        _preloadCoroutine = StartCoroutine(PreloadOnlyCoroutine(nextPath));
    }

    /// <summary>
    /// 다음에 재생할 영상 경로를 결정합니다.
    /// </summary>
    private string GetNextVideoPath()
    {
        if (_isSequencePlaying && _currentSequence != null)
        {
            int nextIndex = _currentSequenceIndex + 1;
            if (nextIndex < _currentSequence.Count)
                return _currentSequence[nextIndex];
            else
                return GetFirstIdleVideoPath(); // 마지막 영상 → 대기 복귀
        }

        // 대기 모드: 다음 대기 영상 (순환)
        return GetNextIdleVideoPath();
    }

    /// <summary>
    /// 백그라운드 플레이어에 영상을 로딩하고 첫 프레임까지 대기 후 일시정지합니다.
    /// </summary>
    private IEnumerator PreloadOnlyCoroutine(string videoPath)
    {
        _backgroundPlayer.Loop = false;
        bool opened = _backgroundPlayer.OpenMedia(
            new MediaPath(videoPath, MediaPathType.AbsolutePathOrURL),
            autoPlay: false
        );

        if (!opened)
        {
            Debug.LogError($"[VideoManager] 사전 로딩 실패: {System.IO.Path.GetFileName(videoPath)}");
            yield break;
        }

        // 미디어 오픈 대기
        float timeout = 10f;
        float elapsed = 0f;
        while (!_backgroundPlayer.MediaOpened && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed >= timeout)
        {
            Debug.LogError($"[VideoManager] 사전 로딩 타임아웃: {System.IO.Path.GetFileName(videoPath)}");
            yield break;
        }

        // ★ 프리로딩 중 소리 겹침 방지: 볼륨 0으로 뮤트
        _backgroundPlayer.Control.SetVolume(0f);

        // 재생 시작 (첫 프레임 생성을 위해)
        _backgroundPlayer.Control.Play();

        // 첫 프레임 렌더링 대기
        elapsed = 0f;
        while (elapsed < timeout)
        {
            if (_backgroundPlayer.TextureProducer != null && _backgroundPlayer.TextureProducer.GetTexture() != null)
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 첫 프레임 완료 → 일시정지 후 처음으로 되돌리기
        // ★ AVPro의 Pause()가 즉시 반영되지 않아 영상이 진행될 수 있으므로 Seek(0)으로 보정
        _backgroundPlayer.Control.Pause();
        _backgroundPlayer.Control.Seek(0);

        _isPreloadReady = true;
        _preloadCoroutine = null;

        Debug.Log($"[VideoManager] 사전 로딩 완료 (대기 중): {System.IO.Path.GetFileName(videoPath)}");
    }

    // ========== 셀프 크로스페이드 인터럽트 ==========

    private void InterruptSelfCrossfade()
    {
        Debug.Log("[VideoManager] 크로스페이드 인터럽트! 플레이어 상태 안정화 중...");

        StopAllVideoCoroutines();

        _isCrossfading = false;
        _isIdleSelfCrossfading = false;
        _isPreloadReady = false;

        float activeAlpha = _activeCanvasGroup.alpha;
        float bgAlpha = _backgroundCanvasGroup.alpha;

        if (bgAlpha > activeAlpha)
        {
            _activeCanvasGroup.alpha = 0f;
            _backgroundCanvasGroup.alpha = 1f;
            _activePlayer.CloseMedia();
            SwapPlayers();
        }
        else
        {
            _activeCanvasGroup.alpha = 1f;
            _backgroundCanvasGroup.alpha = 0f;
            _backgroundPlayer.CloseMedia();
        }

        // ★ 볼륨 복원 안전장치: 활성 플레이어 볼륨 1로 보장
        _activePlayer.Control?.SetVolume(1f);

        Debug.Log("[VideoManager] 플레이어 상태 안정화 완료.");
    }

    // ========== 핵심 재생 로직 ==========

    private void StopAllVideoCoroutines()
    {
        if (_crossfadeCoroutine != null) { StopCoroutine(_crossfadeCoroutine); _crossfadeCoroutine = null; }
        if (_preloadCoroutine != null) { StopCoroutine(_preloadCoroutine); _preloadCoroutine = null; }
        if (_delayedPreloadCoroutine != null) { StopCoroutine(_delayedPreloadCoroutine); _delayedPreloadCoroutine = null; }
    }

    /// <summary>
    /// 크로스페이드를 시작합니다.
    /// 사전 로딩이 완료되었으면 즉시, 아니면 Fallback 로딩 후 시작합니다.
    /// </summary>
    private void PreloadAndCrossfade(string videoPath, bool isIdleVideo = false)
    {
        // ★ 기존에 백그라운드 프리로드를 시도 중이던 코루틴이 있다면 확실히 파괴하여 동시 점유 방지
        StopAllVideoCoroutines();
        
        _preloadCoroutine = StartCoroutine(PreloadAndCrossfadeCoroutine(videoPath, isIdleVideo));
    }

    private IEnumerator PreloadAndCrossfadeCoroutine(string videoPath, bool isIdleVideo)
    {
        _isIdleSelfCrossfading = isIdleVideo;

        // ★ 진입 즉시 크로스페이드 플래그 설정 (Fallback 로딩 중 OnCurrentVideoFinished 오작동 방지)
        _isCrossfading = true;

        // 사전 로딩이 이미 완료된 경우 → 로딩 0초! 즉시 크로스페이드!
        if (_isPreloadReady)
        {
            Debug.Log("[VideoManager] 사전 로딩 완료 상태! 즉시 크로스페이드.");

            // ★ Control null 안전 접근 (ForceReload 등 외부 간섭 방어)
            if (_backgroundPlayer.Control != null)
            {
                // ★ Pause 후에도 영상이 진행되었을 수 있으므로 처음으로 되돌린 후 재생
                _backgroundPlayer.Control.Seek(0);
                _backgroundPlayer.Control.Play();
            }
            else
            {
                Debug.LogError("[VideoManager] 프리로드 실패: 백그라운드 플레이어의 Control이 null입니다.");
                _isCrossfading = false;
                _isIdleSelfCrossfading = false;
                _preloadCoroutine = null;
                yield break;
            }

            _isPreloadReady = false;

            _crossfadeCoroutine = StartCoroutine(CrossfadeCoroutine());
            _preloadCoroutine = null;
            yield break;
        }

        // Fallback: 사전 로딩이 안 됐을 때 (첫 실행, 인터럽트 직후 등)
        _backgroundPlayer.Loop = false;
        bool opened = _backgroundPlayer.OpenMedia(
            new MediaPath(videoPath, MediaPathType.AbsolutePathOrURL),
            autoPlay: false
        );

        if (!opened)
        {
            Debug.LogError($"[VideoManager] 영상 열기 실패: {System.IO.Path.GetFileName(videoPath)}");
            _isCrossfading = false;
            _isIdleSelfCrossfading = false;
            yield break;
        }

        Debug.Log($"[VideoManager] Fallback 로딩: {System.IO.Path.GetFileName(videoPath)}");

        float timeout = 10f;
        float elapsed = 0f;
        while (!_backgroundPlayer.MediaOpened && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (elapsed >= timeout)
        {
            Debug.LogError($"[VideoManager] 프리로드 타임아웃: {System.IO.Path.GetFileName(videoPath)}");
            _isCrossfading = false;
            _isIdleSelfCrossfading = false;
            yield break;
        }

        // ★ Fallback 프리로딩: Control null 안전 접근
        if (_backgroundPlayer.Control != null)
        {
            _backgroundPlayer.Control.SetVolume(0f);
            _backgroundPlayer.Control.Play();
        }
        else
        {
            Debug.LogError("[VideoManager] Fallback 프리로드 실패: Control이 null입니다.");
            _isCrossfading = false;
            _isIdleSelfCrossfading = false;
            _preloadCoroutine = null;
            yield break;
        }

        // 첫 프레임 렌더링 대기 (검은 화면 차단)
        elapsed = 0f;
        while (elapsed < timeout)
        {
            if (_backgroundPlayer.TextureProducer != null && _backgroundPlayer.TextureProducer.GetTexture() != null)
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.Log("[VideoManager] Fallback 프리로드 완료! 크로스페이드 시작.");
        _isPreloadReady = false;

        _crossfadeCoroutine = StartCoroutine(CrossfadeCoroutine());
        _preloadCoroutine = null;
    }

    /// <summary>
    /// 크로스페이드 코루틴: Alpha를 교차 전환 후 즉시 다음 영상을 사전 로딩합니다.
    /// </summary>
    private IEnumerator CrossfadeCoroutine()
    {
        _isCrossfading = true;

        if (watchdog != null)
            watchdog.PauseMonitoring();

        // 크로스페이드 중 터치 입력 전체 차단
        if (UIManager.Instance != null)
            UIManager.Instance.SetTouchLock(true);

        // ★ 코루틴이 어떤 이유로든 죽어도 반드시 상태를 복구하기 위한 안전장치
        bool cleanupNeeded = true;
        try
        {
            // 첫 번째 시퀀스 크로스페이드에서만 결과 패널 표시
            if (_isSequencePlaying && _currentSequenceIndex == 0 && UIManager.Instance != null)
            {
                UIManager.Instance.ShowResultPanel();
            }

            // ★ 크로스페이드 시작: 오디오도 함께 페이드 (Control null 안전 접근)
            _activePlayer.Control?.SetVolume(1f);
            _backgroundPlayer.Control?.SetVolume(0f);

            float elapsed = 0f;

            while (elapsed < _crossfadeTime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _crossfadeTime);

                // 비주얼 크로스페이드
                _activeCanvasGroup.alpha = 1f - t;
                _backgroundCanvasGroup.alpha = t;

                // 오디오 크로스페이드 (Control null 안전 접근)
                _activePlayer.Control?.SetVolume(1f - t);
                _backgroundPlayer.Control?.SetVolume(t);

                yield return null;
            }

            _activeCanvasGroup.alpha = 0f;
            _backgroundCanvasGroup.alpha = 1f;

            // ★ 오디오 최종 상태 보장 (Control null 안전 접근)
            _activePlayer.Control?.SetVolume(0f);
            _backgroundPlayer.Control?.SetVolume(1f);

            // 이전 활성 플레이어 VRAM 해제
            _activePlayer.CloseMedia();

            // 역할 교체
            SwapPlayers();

            cleanupNeeded = false; // 정상 완료 시 별도 복구 불필요
        }
        finally
        {
            // ★ 코루틴이 예외로 죽어도 반드시 실행되어 앱 프리징을 방지
            _isCrossfading = false;
            _isIdleSelfCrossfading = false;
            _crossfadeCoroutine = null;

            if (cleanupNeeded)
            {
                // 비정상 종료 시 안전한 상태로 복구
                Debug.LogError("[VideoManager] 크로스페이드 중 예외 발생! 안전 복구를 수행합니다.");
                _activeCanvasGroup.alpha = 1f;
                _backgroundCanvasGroup.alpha = 0f;
                _backgroundPlayer.CloseMedia();
                _activePlayer.Control?.SetVolume(1f);
            }

            // Watchdog 감시 대상 변경
            if (watchdog != null)
                watchdog.SetTarget(_activePlayer);

            // 터치 입력 잠금 해제 (반드시 실행되어야 함)
            if (UIManager.Instance != null)
                UIManager.Instance.SetTouchLock(false);
        }

        // ★ 정상 완료 시에만 다음 프리로드 시작 (finally 밖에서 실행)
        if (!cleanupNeeded)
        {
            Debug.Log("[VideoManager] 크로스페이드 완료. 플레이어 역할 교체됨.");

            string nextPath = GetNextVideoPath();
            if (!string.IsNullOrEmpty(nextPath))
            {
                PreloadNextVideoImmediately(nextPath);
            }
        }
    }

    // ========== AVPro 이벤트 ==========

    private void OnPlayerEvent(MediaPlayer mp, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
    {
        if (eventType == MediaPlayerEvent.EventType.FinishedPlaying && mp == _activePlayer)
        {
            // ★ 프리로드/크로스페이드 코루틴이 이미 진행 중이면 이벤트 무시 (경쟁 조건 방지)
            if (_preloadCoroutine != null || _crossfadeCoroutine != null) return;
            OnCurrentVideoFinished();
        }

        if (eventType == MediaPlayerEvent.EventType.Error)
        {
            Debug.LogError($"[VideoManager] AVPro 에러 발생: {errorCode}");
        }
    }

    /// <summary>
    /// 영상 재생 완료 시 호출됩니다.
    /// 백그라운드가 이미 준비되어 있으므로 즉시 크로스페이드합니다.
    /// </summary>
    private void OnCurrentVideoFinished()
    {
        if (_isCrossfading) return;

        // ===== 시퀀스 재생 모드 =====
        if (_isSequencePlaying && _currentSequence != null)
        {
            _currentSequenceIndex++;

            if (_currentSequenceIndex < _currentSequence.Count)
            {
                Debug.Log($"[VideoManager] 다음 영상 전환: {_currentSequenceIndex + 1}/{_currentSequence.Count}");
                PreloadAndCrossfade(_currentSequence[_currentSequenceIndex]);
            }
            else
            {
                Debug.Log("[VideoManager] 시퀀스 완료! 대기 영상으로 복귀.");
                _isSequencePlaying = false;
                _currentSequence = null;
                _idleSequenceIndex = 0;

                string firstIdle = GetFirstIdleVideoPath();
                if (!string.IsNullOrEmpty(firstIdle))
                    PreloadAndCrossfade(firstIdle, false);
                else
                    Debug.LogError("[VideoManager] 대기 영상 경로를 찾을 수 없어 복귀 실패!");

                OnSequenceCompleted?.Invoke();

                // UI만 대기 화면으로 전환 (VideoManager를 다시 호출하지 않는 안전한 메서드)
                if (UIManager.Instance != null)
                {
                    if (QuizManager.Instance != null)
                        QuizManager.Instance.ResetQuiz();

                    UIManager.Instance.ShowStandbyPanelOnly();
                }
            }
        }
        // ===== 대기 모드: 다음 대기 영상으로 크로스페이드 루프 =====
        else
        {
            if (MediaScanner.Instance != null && MediaScanner.Instance.IdleVideoPaths != null && MediaScanner.Instance.IdleVideoPaths.Count > 0)
            {
                var idlePaths = MediaScanner.Instance.IdleVideoPaths;
                _idleSequenceIndex = (_idleSequenceIndex + 1) % idlePaths.Count;

                Debug.Log($"[VideoManager] 대기 영상 끝 → 다음 대기 영상 크로스페이드: {System.IO.Path.GetFileName(idlePaths[_idleSequenceIndex])} ({_idleSequenceIndex + 1}/{idlePaths.Count})");
                PreloadAndCrossfade(idlePaths[_idleSequenceIndex], true);
            }
        }
    }

    // ========== 유틸리티 ==========

    private void SwapPlayers()
    {
        (_activePlayer, _backgroundPlayer) = (_backgroundPlayer, _activePlayer);
        (_activeCanvasGroup, _backgroundCanvasGroup) = (_backgroundCanvasGroup, _activeCanvasGroup);
    }

    // ========== 대기 영상 경로 헬퍼 ==========

    /// <summary>
    /// 대기 영상 리스트의 첫 번째 경로를 반환합니다. (초기화/복귀 시 사용)
    /// </summary>
    private string GetFirstIdleVideoPath()
    {
        if (MediaScanner.Instance != null && MediaScanner.Instance.IdleVideoPaths != null && MediaScanner.Instance.IdleVideoPaths.Count > 0)
            return MediaScanner.Instance.IdleVideoPaths[0];
        return null;
    }

    /// <summary>
    /// 현재 인덱스 기준으로 다음 대기 영상 경로를 반환합니다. (프리로드용, 순환)
    /// </summary>
    private string GetNextIdleVideoPath()
    {
        if (MediaScanner.Instance == null || MediaScanner.Instance.IdleVideoPaths == null || MediaScanner.Instance.IdleVideoPaths.Count == 0)
            return null;

        var idlePaths = MediaScanner.Instance.IdleVideoPaths;
        int nextIndex = (_idleSequenceIndex + 1) % idlePaths.Count;
        return idlePaths[nextIndex];
    }
}
