using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 사용자의 퀴즈 응답을 추적하고, JSON 규칙 파일을 기반으로 최종 결과 ID를 도출합니다.
/// 단일 책임: 답변 수집 + 규칙 매칭만 수행합니다.
/// OCP 준수: 질문/결과 확장 시 코드 수정 없이 JSON만 편집하면 됩니다.
/// </summary>
public class QuizManager : MonoBehaviour
{
    public static QuizManager Instance { get; private set; }

    // 현재 총 문항 수 (JSON에서 로드)
    public int TotalQuestions { get; private set; }

    // 현재 진행 중인 문항 인덱스 (0부터 시작)
    public int CurrentQuestionIndex { get; private set; } = 0;

    // 퀴즈가 모두 끝났는지 여부
    public bool IsQuizCompleted { get; private set; } = false;

    // 최종 도출된 결과 ID (예: "01", "02"...)
    public string ResultId { get; private set; }

    // 퀴즈 완료 시 외부(UI, VideoManager 등)에 알리기 위한 이벤트
    public event Action<string> OnQuizCompleted;

    // 사용자 응답 저장 배열
    private string[] _userAnswers;

    // JSON에서 로드된 규칙 리스트
    private List<QuizRule> _rules = new List<QuizRule>();

    // 매칭 실패 시 사용할 안전망 결과 ID
    private string _fallbackResultId = "01";

    private void Awake()
    {
        // 싱글톤 처리
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Awake에서 규칙을 로드하여 다른 스크립트가 Start()에서 안전하게 참조 가능
        LoadRules();
    }

    /// <summary>
    /// StreamingAssets/QuizRules.json 파일을 읽어 규칙을 메모리에 로드합니다.
    /// </summary>
    private void LoadRules()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "QuizRules.json");

        if (!File.Exists(path))
        {
            Debug.LogError("[QuizManager] QuizRules.json 파일을 찾을 수 없습니다! 기본값으로 동작합니다.");
            TotalQuestions = 4;
            _userAnswers = new string[TotalQuestions];
            return;
        }

        try
        {
            string jsonText = File.ReadAllText(path);
            QuizRuleSet ruleSet = JsonUtility.FromJson<QuizRuleSet>(jsonText);

            TotalQuestions = ruleSet.totalQuestions;
            _fallbackResultId = ruleSet.fallbackResultId;
            _userAnswers = new string[TotalQuestions];

            // 규칙을 우선순위(priority) 기준 오름차순으로 정렬 (1순위가 먼저 검사됨)
            _rules = ruleSet.rules;
            _rules.Sort((a, b) => a.priority.CompareTo(b.priority));

            Debug.Log($"[QuizManager] 퀴즈 규칙 로드 완료! 총 {TotalQuestions}문항, {_rules.Count}개 규칙 등록됨.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[QuizManager] JSON 파싱 중 치명적 에러 발생: {e.Message}");
            TotalQuestions = 4;
            _userAnswers = new string[TotalQuestions];
        }
    }

    /// <summary>
    /// UI 버튼에서 호출하는 답변 제출 메서드입니다.
    /// 현재 문항에 대한 응답을 저장하고 다음 문항으로 넘어갑니다.
    /// 마지막 문항이면 자동으로 결과를 평가합니다.
    /// </summary>
    /// <param name="answer">사용자가 선택한 응답 ("A", "B", "C", "D")</param>
    public void SubmitAnswer(string answer)
    {
        if (IsQuizCompleted)
        {
            Debug.LogWarning("[QuizManager] 이미 퀴즈가 완료된 상태입니다. 중복 입력을 무시합니다.");
            return;
        }

        if (CurrentQuestionIndex >= TotalQuestions)
        {
            Debug.LogWarning("[QuizManager] 유효하지 않은 문항 인덱스입니다. 입력을 무시합니다.");
            return;
        }

        // 현재 문항에 답변 저장
        _userAnswers[CurrentQuestionIndex] = answer.ToUpper();
        Debug.Log($"[QuizManager] Q{CurrentQuestionIndex + 1} 답변 저장: {answer.ToUpper()}");

        CurrentQuestionIndex++;

        // 마지막 문항이면 결과 평가
        if (CurrentQuestionIndex >= TotalQuestions)
        {
            EvaluateResult();
        }
    }

    /// <summary>
    /// 저장된 응답 배열을 JSON 규칙과 대조하여 최종 결과 ID를 도출합니다.
    /// 우선순위가 높은(priority 낮은) 규칙부터 위에서 아래로 비교합니다.
    /// </summary>
    private void EvaluateResult()
    {
        string answersLog = string.Join(", ", _userAnswers);
        Debug.Log($"[QuizManager] 전체 응답: [{answersLog}] - 규칙 매칭을 시작합니다...");

        foreach (QuizRule rule in _rules)
        {
            if (IsRuleMatched(rule))
            {
                ResultId = rule.resultId;
                IsQuizCompleted = true;
                Debug.Log($"[QuizManager] 퀴즈 결과: {rule.resultId}번 카테고리 매칭! (규칙: {rule.ruleName})");
                OnQuizCompleted?.Invoke(ResultId);
                return;
            }
        }

        // 어떤 규칙에도 매칭되지 않은 경우 안전망(Fallback) 적용
        ResultId = _fallbackResultId;
        IsQuizCompleted = true;
        Debug.LogWarning($"[QuizManager] 매칭되는 규칙이 없어 Fallback 결과 적용: {_fallbackResultId}번");
        OnQuizCompleted?.Invoke(ResultId);
    }

    /// <summary>
    /// 하나의 규칙이 사용자 응답과 매칭되는지 검사합니다.
    /// "ANY" 와일드카드는 해당 문항의 응답을 무조건 통과시킵니다.
    /// </summary>
    private bool IsRuleMatched(QuizRule rule)
    {
        // 규칙의 조건 배열 길이와 문항 수가 다르면 매칭 불가
        if (rule.conditions == null || rule.conditions.Length != TotalQuestions)
        {
            Debug.LogWarning($"[QuizManager] 규칙 '{rule.ruleName}'의 조건 수({rule.conditions?.Length})가 문항 수({TotalQuestions})와 다릅니다. 무시합니다.");
            return false;
        }

        for (int i = 0; i < TotalQuestions; i++)
        {
            string condition = rule.conditions[i].ToUpper();

            // "ANY"는 어떤 응답이든 통과
            if (condition == "ANY") continue;

            // 사용자 응답과 조건이 다르면 매칭 실패
            if (_userAnswers[i] != condition)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 퀴즈를 초기 상태로 완전히 리셋합니다.
    /// 홈(처음으로) 버튼이나 영상 종료 후 복귀 시 호출됩니다.
    /// </summary>
    public void ResetQuiz()
    {
        CurrentQuestionIndex = 0;
        IsQuizCompleted = false;
        ResultId = null;
        _userAnswers = new string[TotalQuestions];
        Debug.Log("[QuizManager] 퀴즈 상태가 초기화되었습니다.");
    }

    // ========== JSON 직렬화용 데이터 클래스 (JsonUtility 호환) ==========

    [Serializable]
    private class QuizRuleSet
    {
        public int totalQuestions;
        public string fallbackResultId;
        public List<QuizRule> rules;
    }

    [Serializable]
    private class QuizRule
    {
        public int priority;
        public string ruleName;
        public string[] conditions;
        public string resultId;
    }
}
