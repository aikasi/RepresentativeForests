using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 블러 보더 스프라이트를 런타임에 생성하여 Image에 적용하는 유틸리티입니다.
/// 포럼 방식(blurborder 스프라이트 + alpha 맥동)을 프로그래밍으로 구현합니다.
/// 외부 PNG 파일 없이 코드만으로 글로우 스프라이트를 생성합니다.
///
/// [사용법]
/// 1. 버튼 뒤에 빈 GameObject 생성 → Image + 이 스크립트 부착
/// 2. RectTransform을 버튼보다 상하좌우 20~40px 크게 설정
/// 3. ImageAlphaPulse 스크립트도 함께 부착하면 맥동 완성
/// </summary>
[RequireComponent(typeof(Image))]
[ExecuteInEditMode]
public class BlurBorderGenerator : MonoBehaviour
{
    [Header("===== 보더 설정 =====")]
    [Tooltip("생성할 텍스처 크기 (정사각형). 9-slice로 스케일링되므로 작아도 됩니다.")]
    [Range(64, 512)]
    [SerializeField] private int textureSize = 128;

    [Tooltip("보더 두께 (픽셀). 글로우가 퍼지는 범위.")]
    [Range(4, 64)]
    [SerializeField] private int borderWidth = 24;

    [Tooltip("모서리 라운딩 (픽셀).")]
    [Range(4, 64)]
    [SerializeField] private int cornerRadius = 16;

    [Tooltip("글로우 색상. alpha는 ImageAlphaPulse에서 제어합니다.")]
    [SerializeField] private Color glowColor = Color.white;

    [Header("===== Additive 블렌딩 =====")]
    [Tooltip("Additive 블렌딩 사용 (배경에 빛을 더하는 효과). 비활성화 시 기본 알파 블렌딩.")]
    [SerializeField] private bool useAdditiveBlending = false;

    // 내부 참조
    private Image _image;
    private Sprite _generatedSprite;
    private Texture2D _generatedTexture;
    private Material _additiveMaterial;

    private void Awake()
    {
        _image = GetComponent<Image>();
        GenerateAndApply();
    }

    /// <summary>
    /// 블러 보더 텍스처를 생성하고 9-slice 스프라이트로 Image에 적용합니다.
    /// </summary>
    public void GenerateAndApply()
    {
        if (_image == null) _image = GetComponent<Image>();
        if (_image == null) return;

        // 이전 생성물 해제
        Cleanup();

        // 텍스처 생성
        _generatedTexture = GenerateBlurBorderTexture();

        // 9-slice 보더 설정: 보더 두께 + 코너를 기준으로 슬라이스
        int sliceBorder = borderWidth + cornerRadius;
        Vector4 border = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);

        _generatedSprite = Sprite.Create(
            _generatedTexture,
            new Rect(0, 0, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            border
        );

        _image.sprite = _generatedSprite;
        _image.type = Image.Type.Sliced;
        _image.color = glowColor;
        _image.raycastTarget = false;

        // Additive 블렌딩 적용
        if (useAdditiveBlending)
        {
            ApplyAdditiveBlending();
        }
        else
        {
            _image.material = null;
        }

        Debug.Log($"[BlurBorderGenerator] 블러 보더 스프라이트 생성 완료 ({textureSize}x{textureSize}, 보더 {borderWidth}px)");
    }

    /// <summary>
    /// 둥근 사각형 외곽에 가우시안 블러가 적용된 텍스처를 생성합니다.
    /// 중앙은 완전 투명, 외곽으로 갈수록 밝아졌다가 바깥에서 다시 투명해지는 구조.
    /// </summary>
    private Texture2D GenerateBlurBorderTexture()
    {
        Texture2D tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float halfSize = textureSize * 0.5f;
        float innerHalf = halfSize - borderWidth; // 내부 사각형의 반크기
        float r = Mathf.Min(cornerRadius, innerHalf); // 코너 반경 제한

        Color[] pixels = new Color[textureSize * textureSize];

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                // 중앙 기준 좌표
                float px = x - halfSize;
                float py = y - halfSize;

                // 둥근 사각형 SDF (내부 사각형)
                float dist = RoundedBoxSDF(px, py, innerHalf, innerHalf, r);

                // 거리 → 알파 매핑
                // dist < 0: 내부 → 투명 (alpha = 0)
                // dist = 0: 가장자리 → 최대 밝기 (alpha = 1)
                // dist > 0 ~ borderWidth: 외부 → 점진적 감쇠
                // dist > borderWidth: 완전 바깥 → 투명 (alpha = 0)
                float alpha;
                if (dist < 0)
                {
                    // 내부: 완전 투명
                    alpha = 0f;
                }
                else if (dist <= borderWidth)
                {
                    // 가장자리 ~ 외부: 가우시안 감쇠
                    float t = dist / borderWidth;
                    // 가장자리(t=0)에서 최대, 바깥(t=1)에서 0
                    alpha = Mathf.Exp(-t * t * 3f); // 가우시안 감쇠
                }
                else
                {
                    alpha = 0f;
                }

                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false); // 읽기 가능 유지 (에디터 디버깅용)

        return tex;
    }

    /// <summary>
    /// 둥근 사각형 Signed Distance Field.
    /// 반환값: 음수 = 내부, 0 = 가장자리, 양수 = 외부
    /// </summary>
    private float RoundedBoxSDF(float px, float py, float halfW, float halfH, float radius)
    {
        float qx = Mathf.Abs(px) - halfW + radius;
        float qy = Mathf.Abs(py) - halfH + radius;

        float outside = Mathf.Sqrt(
            Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
            Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)
        );
        float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);

        return outside + inside - radius;
    }

    /// <summary>
    /// Additive 블렌딩 Material을 생성하여 적용합니다.
    /// 배경에 빛을 더하는 효과로, 어두운 배경에서 더 잘 보입니다.
    /// </summary>
    private void ApplyAdditiveBlending()
    {
        if (_additiveMaterial != null)
        {
            if (Application.isPlaying) Destroy(_additiveMaterial);
            else DestroyImmediate(_additiveMaterial);
        }

        // UI Default Additive 셰이더 사용
        Shader additiveShader = Shader.Find("UI/Default");
        if (additiveShader != null)
        {
            _additiveMaterial = new Material(additiveShader);
            _additiveMaterial.name = "BlurBorder_Additive";

            // Blend One One (Additive)
            _additiveMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _additiveMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);

            _image.material = _additiveMaterial;
        }
    }

    private void Cleanup()
    {
        if (_generatedTexture != null)
        {
            if (Application.isPlaying) Destroy(_generatedTexture);
            else DestroyImmediate(_generatedTexture);
            _generatedTexture = null;
        }

        if (_generatedSprite != null)
        {
            if (Application.isPlaying) Destroy(_generatedSprite);
            else DestroyImmediate(_generatedSprite);
            _generatedSprite = null;
        }

        if (_additiveMaterial != null)
        {
            if (Application.isPlaying) Destroy(_additiveMaterial);
            else DestroyImmediate(_additiveMaterial);
            _additiveMaterial = null;
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Inspector에서 값 변경 시 재생성
        if (_image != null)
        {
            GenerateAndApply();
        }
    }
#endif
}
