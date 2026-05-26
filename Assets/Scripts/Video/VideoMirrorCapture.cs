using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VideoCamera에 부착하여 렌더링 결과를 RenderTexture로 캡처합니다.
/// Camera.targetTexture를 직접 설정하여 RT에 렌더링하고,
/// Display 1에는 Overlay Canvas의 RawImage로 동일 RT를 표시합니다.
///
/// [동작 원리]
/// - StartCapture(): camera.targetTexture = RT → Display 1용 RawImage 활성화
/// - StopCapture():  camera.targetTexture = null → RawImage 비활성화 (카메라가 Display에 직접 출력)
///
/// [에디터 설정]
/// 1. VideoCamera에 이 스크립트 부착
/// 2. Mirror RT에 RenderTexture 에셋 연결
/// 3. Display 1에 Screen Space Overlay Canvas 생성 (Target Display = 1)
/// 4. 그 안에 전체 화면 RawImage 생성 → Display Raw Image 필드에 연결
/// </summary>
[RequireComponent(typeof(Camera))]
public class VideoMirrorCapture : MonoBehaviour
{
    [Header("===== 미러 캡처 설정 =====")]
    [Tooltip("캡처 결과를 저장할 RenderTexture")]
    [SerializeField] private RenderTexture mirrorRT;

    [Header("===== Display 1 출력용 =====")]
    [Tooltip("Display 1(영상 모니터)에 RT를 표시할 RawImage. Screen Space Overlay Canvas(Target Display=1) 안에 배치합니다.")]
    [SerializeField] private RawImage displayRawImage;

    private Camera _camera;
    private bool _isCapturing = false;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera == null)
        {
            Debug.LogError("[VideoMirrorCapture] Camera 컴포넌트를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        // 초기 상태: RawImage 숨김 (카메라가 Display에 직접 출력)
        if (displayRawImage != null)
            displayRawImage.enabled = false;
    }

    /// <summary>
    /// 캡처를 시작합니다.
    /// 카메라 출력을 RT로 전환하고, Display 1에 RT를 표시합니다.
    /// </summary>
    public void StartCapture()
    {
        if (mirrorRT == null)
        {
            Debug.LogError("[VideoMirrorCapture] mirrorRT가 연결되지 않았습니다!");
            return;
        }

        if (_isCapturing) return;

        // 카메라 출력을 RT로 전환
        _camera.targetTexture = mirrorRT;

        // Display 1에 RT 표시
        if (displayRawImage != null)
        {
            displayRawImage.texture = mirrorRT;
            displayRawImage.enabled = true;
        }

        _isCapturing = true;
        Debug.Log("[VideoMirrorCapture] 미러 캡처 시작: 카메라 → RT, Display 1에 RT 표시.");
    }

    /// <summary>
    /// 캡처를 중지합니다.
    /// 카메라 출력을 Display로 복원하고, RawImage를 숨깁니다.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        // 카메라 출력을 Display로 복원
        _camera.targetTexture = null;

        // Display 1 RawImage 숨김
        if (displayRawImage != null)
            displayRawImage.enabled = false;

        _isCapturing = false;
        Debug.Log("[VideoMirrorCapture] 미러 캡처 중지: 카메라 → Display 직접 출력 복원.");
    }

    private void OnDestroy()
    {
        // 안전한 정리
        StopCapture();
    }
}
