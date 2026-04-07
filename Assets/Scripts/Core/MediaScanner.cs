using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// StreamingAssets/Videos/ 폴더를 스캔하여 영상 파일을 검증하고
/// Dictionary 형태로 메모리에 캐싱하는 스캐너입니다.
/// 단일 책임: 파일 탐색, 파싱, 무결성 검사만 수행합니다.
/// </summary>
public class MediaScanner : MonoBehaviour
{
    public static MediaScanner Instance { get; private set; }

    // 대기 영상 경로 (00.xxx)
    public string IdleVideoPath { get; private set; }

    // 결과 영상 캐시: Key = 결과 카테고리 ID, Value = 재생 순서대로 정렬된 절대 경로 리스트
    public Dictionary<int, List<string>> ResultVideos { get; private set; } = new Dictionary<int, List<string>>();

    // 치명적 에러 발생 여부 (에러 UI 팝업 트리거용)
    public bool HasCriticalError { get; private set; } = false;
    public List<string> ErrorMessages { get; private set; } = new List<string>();

    // 에러 발생 시 외부(UI 등)에 알리기 위한 이벤트
    public event Action<List<string>> OnCriticalError;

    // 허용 확장자 목록
    private readonly string[] _allowedExtensions = { ".mp4", ".mov", ".avi", ".wmv" };

    // 결과 영상 파일명 정규식 패턴: "숫자-숫자" (예: 01-1, 2-10)
    private static readonly Regex ResultFilePattern = new Regex(@"^(\d+)-(\d+)$", RegexOptions.Compiled);

    // 대기 영상 파일명 정규식 패턴: "00" (확장자 제외)
    private static readonly Regex IdleFilePattern = new Regex(@"^0+$", RegexOptions.Compiled);

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
        // CSVReader(-1000)보다 늦게 실행되므로 안전하게 호출 가능
        ScanVideos();
    }

    /// <summary>
    /// StreamingAssets/Videos/ 폴더를 스캔하여 영상 파일을 검증하고 캐싱합니다.
    /// </summary>
    public void ScanVideos()
    {
        string videosPath = Path.Combine(Application.streamingAssetsPath, "Videos");

        // 폴더 존재 여부 확인
        if (!Directory.Exists(videosPath))
        {
            AddCriticalError($"Videos 폴더를 찾을 수 없습니다: {videosPath}");
            return;
        }

        // 허용된 확장자를 가진 모든 파일 수집
        List<string> allVideoFiles = new List<string>();
        foreach (string ext in _allowedExtensions)
        {
            string[] found = Directory.GetFiles(videosPath, "*" + ext, SearchOption.TopDirectoryOnly);
            allVideoFiles.AddRange(found);
        }

        if (allVideoFiles.Count == 0)
        {
            AddCriticalError("Videos 폴더에 지원되는 영상 파일이 하나도 없습니다.");
            return;
        }

        // ========== 무결성 검사 (Integrity Check) ==========

        // 1. 0KB 파일 검사
        foreach (string filePath in allVideoFiles)
        {
            FileInfo fi = new FileInfo(filePath);
            if (fi.Length == 0)
            {
                AddCriticalError($"0KB 손상 파일 발견: {fi.Name}");
            }
        }

        // 2. 동일 이름에 확장자만 다른 중복 파일 검사
        var fileNameGroups = allVideoFiles
            .Select(f => new { FullPath = f, NameWithoutExt = Path.GetFileNameWithoutExtension(f) })
            .GroupBy(f => f.NameWithoutExt)
            .Where(g => g.Count() > 1);

        foreach (var group in fileNameGroups)
        {
            string duplicates = string.Join(", ", group.Select(g => Path.GetFileName(g.FullPath)));
            AddCriticalError($"확장자만 다른 중복 파일 발견: {duplicates}");
        }

        // 치명적 에러가 하나라도 있으면 스캔 중단 및 에러 이벤트 발화
        if (HasCriticalError)
        {
            Debug.LogError("[MediaScanner] 무결성 검사 실패! 프로그램 진입을 차단합니다.");
            OnCriticalError?.Invoke(ErrorMessages);
            return;
        }

        // ========== 파싱 및 캐싱 (Parsing & Caching) ==========

        foreach (string filePath in allVideoFiles)
        {
            string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);

            // 대기 영상(00) 분류
            if (IdleFilePattern.IsMatch(fileNameNoExt))
            {
                if (!string.IsNullOrEmpty(IdleVideoPath))
                {
                    Debug.LogWarning($"[MediaScanner] 대기 영상이 2개 이상 발견되었습니다. 마지막 파일을 사용합니다: {Path.GetFileName(filePath)}");
                }
                IdleVideoPath = filePath;
                Debug.Log($"[MediaScanner] 대기 영상 확인: {Path.GetFileName(filePath)}");
                continue;
            }

            // 결과 영상 분류 (숫자-숫자 패턴)
            Match match = ResultFilePattern.Match(fileNameNoExt);
            if (!match.Success)
            {
                Debug.LogWarning($"[MediaScanner] 파일명 규칙에 맞지 않아 무시합니다: {Path.GetFileName(filePath)}");
                continue;
            }

            int categoryId = int.Parse(match.Groups[1].Value);
            // Groups[2]는 순서 번호 - 정렬 시 사용

            // Dictionary에 카테고리 키가 없으면 새 리스트 생성
            if (!ResultVideos.ContainsKey(categoryId))
            {
                ResultVideos[categoryId] = new List<string>();
            }
            ResultVideos[categoryId].Add(filePath);
        }

        // 대기 영상 존재 여부 최종 확인
        if (string.IsNullOrEmpty(IdleVideoPath))
        {
            AddCriticalError("대기 영상(00.mp4 등)을 찾을 수 없습니다. Videos 폴더를 확인하세요.");
            OnCriticalError?.Invoke(ErrorMessages);
            return;
        }

        // ========== 각 카테고리 내부 재생 순서 정렬 ==========

        foreach (var kvp in ResultVideos)
        {
            kvp.Value.Sort((a, b) =>
            {
                // 파일명에서 '-' 뒤의 순서 번호를 추출하여 자연수(숫자 크기) 기준으로 비교
                int orderA = ExtractOrder(a);
                int orderB = ExtractOrder(b);
                return orderA.CompareTo(orderB);
            });

            // 캐싱 결과 로그 출력
            string fileList = string.Join(", ", kvp.Value.Select(Path.GetFileName));
            Debug.Log($"[MediaScanner] 결과 {kvp.Key:D2}번: {kvp.Value.Count}개 영상 캐싱 완료 → [{fileList}]");
        }

        Debug.Log($"[MediaScanner] 스캔 완료! 대기영상 1개 + 결과 카테고리 {ResultVideos.Count}개 캐싱됨.");
    }

    /// <summary>
    /// 파일 절대 경로에서 '-' 뒤의 순서 번호(int)를 추출합니다.
    /// 예: "C:/.../01-3.mp4" → 3
    /// </summary>
    private int ExtractOrder(string filePath)
    {
        string nameNoExt = Path.GetFileNameWithoutExtension(filePath);
        Match match = ResultFilePattern.Match(nameNoExt);
        if (match.Success && int.TryParse(match.Groups[2].Value, out int order))
        {
            return order;
        }
        return int.MaxValue; // 파싱 실패 시 맨 뒤로 밀기
    }

    /// <summary>
    /// 에러 메시지를 누적하고 치명적 에러 플래그를 활성화합니다.
    /// </summary>
    private void AddCriticalError(string message)
    {
        HasCriticalError = true;
        ErrorMessages.Add(message);
        Debug.LogError($"[MediaScanner] 치명적 에러: {message}");
    }
}
