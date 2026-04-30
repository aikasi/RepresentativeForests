using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 외부 이미지(StreamingAssets/Images)를 온디맨드로 로드/해제합니다.
/// 참조 카운팅 방식으로 같은 이미지를 여러 UI가 공유해도 안전하게 관리합니다.
/// VRAM 점유를 최소화하여 저사양 PC에서도 안정적으로 동작합니다.
/// </summary>
public class ImageManager : MonoBehaviour
{
    public static ImageManager Instance { get; private set; }

    // 캐시 엔트리: 텍스처 + 스프라이트 + 참조 카운트
    private class CacheEntry
    {
        public Texture2D Texture;
        public Sprite Sprite;
        public int RefCount;
    }

    // 현재 로드된 이미지 캐시 (파일명 → 엔트리)
    private Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>();

    // 현재 비동기 로딩이 진행 중인 파일명 세트 (중복 로딩 방지)
    private HashSet<string> _loadingInProgress = new HashSet<string>();

    // Images 폴더 경로 (한 번만 조립)
    private string _imagesPath;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _imagesPath = Path.Combine(Application.streamingAssetsPath, "Images");

        if (!Directory.Exists(_imagesPath))
        {
            Debug.LogError($"[ImageManager] Images 폴더가 없습니다: {_imagesPath}");
            ErrorPopup.Show("Images 폴더가 존재하지 않습니다!");
        }
    }

    // ========== 동기 로드 메서드 (하위 호환 유지) ==========

    /// <summary>
    /// 이미지를 로드하고 참조 카운트를 증가시킵니다.
    /// 이미 로드되어 있으면 캐시에서 즉시 반환합니다 (디스크 I/O 없음).
    /// </summary>
    public Sprite LoadSprite(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;

        // 이미 캐시에 있으면 참조만 증가
        if (_cache.TryGetValue(fileName, out CacheEntry existing))
        {
            existing.RefCount++;
            return existing.Sprite;
        }

        // 디스크에서 새로 로드
        string fullPath = Path.Combine(_imagesPath, fileName);

        if (!File.Exists(fullPath))
        {
            Debug.LogWarning($"[ImageManager] 파일이 없습니다: {fileName}");
            return null;
        }

        byte[] fileData;
        try
        {
            fileData = File.ReadAllBytes(fullPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ImageManager] 파일 읽기 실패 ({fileName}): {e.Message}");
            return null;
        }

        Texture2D texture = new Texture2D(2, 2);
        if (!texture.LoadImage(fileData))
        {
            Debug.LogError($"[ImageManager] 이미지 디코딩 실패: {fileName}");
            Destroy(texture);
            return null;
        }

        // 스프라이트 생성
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f)
        );

        // 캐시에 등록 (참조 카운트 1)
        _cache[fileName] = new CacheEntry
        {
            Texture = texture,
            Sprite = sprite,
            RefCount = 1
        };

        Debug.Log($"[ImageManager] 로드 완료: {fileName} ({texture.width}x{texture.height}, 참조: 1)");
        return sprite;
    }

    // ========== 비동기 로드 메서드 (영상 끊김 방지) ==========

    /// <summary>
    /// 이미지를 비동기로 로드합니다.
    /// 디스크 I/O를 백그라운드 스레드에서 실행하여 메인 스레드 블로킹을 방지합니다.
    /// 캐시에 이미 있으면 즉시 콜백합니다.
    /// </summary>
    /// <param name="fileName">StreamingAssets/Images 내 파일명</param>
    /// <param name="onComplete">로딩 완료 콜백 (실패 시 null 전달)</param>
    public void LoadSpriteAsync(string fileName, Action<Sprite> onComplete)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            onComplete?.Invoke(null);
            return;
        }

        // 이미 캐시에 있으면 즉시 반환 (디스크 I/O 없음)
        if (_cache.TryGetValue(fileName, out CacheEntry existing))
        {
            existing.RefCount++;
            onComplete?.Invoke(existing.Sprite);
            return;
        }

        // 이미 같은 파일에 대한 비동기 로딩이 진행 중이면 대기 코루틴으로 전환
        if (_loadingInProgress.Contains(fileName))
        {
            StartCoroutine(WaitForLoadingAndReturn(fileName, onComplete));
            return;
        }

        // 비동기 로딩 시작
        StartCoroutine(LoadSpriteAsyncCoroutine(fileName, onComplete));
    }

    /// <summary>
    /// 이미 진행 중인 비동기 로딩이 완료될 때까지 대기한 후 캐시에서 반환합니다.
    /// </summary>
    private IEnumerator WaitForLoadingAndReturn(string fileName, Action<Sprite> onComplete)
    {
        // 로딩 완료까지 대기
        while (_loadingInProgress.Contains(fileName))
        {
            yield return null;
        }

        // 캐시에서 결과 반환
        if (_cache.TryGetValue(fileName, out CacheEntry entry))
        {
            entry.RefCount++;
            onComplete?.Invoke(entry.Sprite);
        }
        else
        {
            // 로딩이 실패한 경우
            onComplete?.Invoke(null);
        }
    }

    /// <summary>
    /// 비동기 이미지 로딩 코루틴.
    /// Step 1: 백그라운드 스레드에서 File.ReadAllBytes() 실행 (메인 스레드 블로킹 방지)
    /// Step 2: 메인 스레드에서 texture.LoadImage() 실행 (Unity API 제한)
    /// </summary>
    private IEnumerator LoadSpriteAsyncCoroutine(string fileName, Action<Sprite> onComplete)
    {
        _loadingInProgress.Add(fileName);

        string fullPath = Path.Combine(_imagesPath, fileName);

        // ===== Step 1: 백그라운드 스레드에서 디스크 I/O 실행 =====
        byte[] fileData = null;
        string errorMessage = null;
        bool ioCompleted = false;

        Task.Run(() =>
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    fileData = File.ReadAllBytes(fullPath);
                }
                else
                {
                    errorMessage = $"파일이 없습니다: {fileName}";
                }
            }
            catch (Exception e)
            {
                errorMessage = $"파일 읽기 실패 ({fileName}): {e.Message}";
            }
            finally
            {
                ioCompleted = true;
            }
        });

        // 백그라운드 스레드 완료 대기 (메인 스레드는 블로킹하지 않음)
        while (!ioCompleted)
        {
            yield return null;
        }

        // 디스크 I/O 에러 처리
        if (errorMessage != null)
        {
            Debug.LogWarning($"[ImageManager] {errorMessage}");
            _loadingInProgress.Remove(fileName);
            onComplete?.Invoke(null);
            yield break;
        }

        if (fileData == null || fileData.Length == 0)
        {
            Debug.LogWarning($"[ImageManager] 빈 파일 데이터: {fileName}");
            _loadingInProgress.Remove(fileName);
            onComplete?.Invoke(null);
            yield break;
        }

        // 코루틴 진행 중 캐시에 다른 경로로 등록되었는지 재확인 (경쟁 방지)
        if (_cache.TryGetValue(fileName, out CacheEntry raceEntry))
        {
            raceEntry.RefCount++;
            _loadingInProgress.Remove(fileName);
            onComplete?.Invoke(raceEntry.Sprite);
            yield break;
        }

        // ===== Step 2: 메인 스레드에서 텍스처 생성 (Unity API 제한) =====
        Texture2D texture = new Texture2D(2, 2);
        if (!texture.LoadImage(fileData))
        {
            Debug.LogError($"[ImageManager] 이미지 디코딩 실패: {fileName}");
            Destroy(texture);
            _loadingInProgress.Remove(fileName);
            onComplete?.Invoke(null);
            yield break;
        }

        // 스프라이트 생성
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f)
        );

        // 캐시에 등록 (참조 카운트 1)
        _cache[fileName] = new CacheEntry
        {
            Texture = texture,
            Sprite = sprite,
            RefCount = 1
        };

        _loadingInProgress.Remove(fileName);

        Debug.Log($"[ImageManager] 비동기 로드 완료: {fileName} ({texture.width}x{texture.height}, 참조: 1)");
        onComplete?.Invoke(sprite);
    }

    // ========== 해제 메서드 ==========

    /// <summary>
    /// 참조 카운트를 감소시키고, 0이 되면 텍스처와 스프라이트를 VRAM에서 완전 해제합니다.
    /// </summary>
    public void UnloadSprite(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;

        if (!_cache.TryGetValue(fileName, out CacheEntry entry)) return;

        entry.RefCount--;

        if (entry.RefCount <= 0)
        {
            // VRAM에서 완전 제거
            if (entry.Sprite != null) Destroy(entry.Sprite);
            if (entry.Texture != null) Destroy(entry.Texture);

            _cache.Remove(fileName);
            Debug.Log($"[ImageManager] VRAM 해제: {fileName}");
        }
    }

    /// <summary>
    /// 모든 캐시를 강제 해제합니다. (앱 종료, 긴급 복구 시 사용)
    /// </summary>
    public void UnloadAll()
    {
        foreach (var kvp in _cache)
        {
            if (kvp.Value.Sprite != null) Destroy(kvp.Value.Sprite);
            if (kvp.Value.Texture != null) Destroy(kvp.Value.Texture);
        }

        int count = _cache.Count;
        _cache.Clear();
        _loadingInProgress.Clear();
        Debug.Log($"[ImageManager] 전체 캐시 강제 해제 완료. ({count}개)");
    }

    /// <summary>
    /// 현재 VRAM에 올라간 이미지 수를 반환합니다. (디버그용)
    /// </summary>
    public int GetLoadedCount() => _cache.Count;

    private void OnDestroy()
    {
        // 앱 종료 시 안전하게 모든 리소스 해제
        UnloadAll();
    }
}
