using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퀴즈 진행도를 이미지로 표시하는 UI 컴포넌트입니다.
/// Inspector에서 드래그 앤 드롭으로 연결한 Sprite를 단계별로 교체합니다.
/// 단일 책임: 진행도 이미지 표시/숨김만 수행합니다.
/// </summary>
public class ProgressUI : MonoBehaviour
{
    public static ProgressUI Instance { get; private set; }

    [Header("===== 프로그래스 이미지 표시용 =====")]
    [Tooltip("프로그래스 바를 표시할 Image 컴포넌트를 연결합니다.")]
    [SerializeField] private Image progressImage;

    [Header("===== 단계별 스프라이트 (Inspector에서 드래그 앤 드롭) =====")]
    [Tooltip("Q1~Q4 각 단계에 대응하는 Sprite를 순서대로 연결합니다.")]
    [SerializeField] private Sprite[] progressSprites;

    // 가시성 제어용 CanvasGroup (없으면 자동 추가)
    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        // 싱글톤 처리
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // CanvasGroup 확보 (없으면 자동 추가)
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            Debug.LogWarning("[ProgressUI] CanvasGroup이 없어 자동 추가했습니다.");
        }

        // 유효성 검사
        ValidateReferences();

        // 초기 상태: 숨김
        Hide();
    }

    /// <summary>
    /// Inspector 연결 상태를 검사합니다.
    /// </summary>
    private void ValidateReferences()
    {
        if (progressImage == null)
        {
            Debug.LogError("[ProgressUI] progressImage가 Inspector에 연결되지 않았습니다!");
        }

        if (progressSprites == null || progressSprites.Length == 0)
        {
            Debug.LogError("[ProgressUI] progressSprites 배열이 비어있습니다! Inspector에서 Sprite를 연결해 주세요.");
            return;
        }

        // 개별 슬롯 null 체크
        for (int i = 0; i < progressSprites.Length; i++)
        {
            if (progressSprites[i] == null)
            {
                Debug.LogError($"[ProgressUI] progressSprites[{i}]가 비어있습니다! 해당 슬롯에 Sprite를 연결해 주세요.");
            }
        }

        Debug.Log($"[ProgressUI] 초기화 완료. 프로그래스 스프라이트 {progressSprites.Length}개 연결됨.");
    }

    /// <summary>
    /// 현재 퀴즈 진행 단계에 맞는 이미지로 교체합니다.
    /// </summary>
    /// <param name="stepIndex">0-indexed 단계 번호 (0=Q1, 1=Q2, 2=Q3, 3=Q4)</param>
    public void SetStep(int stepIndex)
    {
        if (progressSprites == null || progressSprites.Length == 0)
        {
            Debug.LogWarning("[ProgressUI] 연결된 스프라이트가 없습니다.");
            return;
        }

        // 범위 초과 시 마지막 이미지 유지 (문항 수가 이미지 수보다 많을 때 안전장치)
        int clampedIndex = Mathf.Clamp(stepIndex, 0, progressSprites.Length - 1);

        if (progressSprites[clampedIndex] == null)
        {
            Debug.LogWarning($"[ProgressUI] 단계 {clampedIndex}에 해당하는 스프라이트가 null입니다.");
            return;
        }

        if (progressImage != null)
        {
            progressImage.sprite = progressSprites[clampedIndex];
        }
    }

    /// <summary>
    /// 프로그래스 UI를 화면에 표시합니다.
    /// </summary>
    public void Show()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = false; // 터치 이벤트를 가로채지 않음
            _canvasGroup.interactable = false;
        }
    }

    /// <summary>
    /// 프로그래스 UI를 화면에서 숨깁니다.
    /// </summary>
    public void Hide()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
        }
    }
}
