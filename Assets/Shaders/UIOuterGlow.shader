// 버튼 외곽 소프트 글로우 셰이더 (마진 기반 UV 리매핑)
// 스프라이트를 이미지 안쪽에 배치하고, 여백 영역에서 블러 글로우를 렌더링합니다.
Shader "UI/OuterGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Glow)]
        _GlowColor ("Glow Color", Color) = (0.55, 0.95, 0.45, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 2)) = 1.0
        _GlowSpread ("Glow Spread", Range(0.01, 0.5)) = 0.15
        _GlowSoftness ("Glow Softness", Range(0.5, 4.0)) = 1.2

        [Header(Margin)]
        _MarginX ("Margin X", Float) = 0.04
        _AspectRatio ("Aspect Ratio", Float) = 4.0
        _MarginY ("Margin Y", Float) = 0.15

        // Unity UI 스텐실
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True"
            "RenderType"="Transparent" "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }

        Cull Off Lighting Off ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _GlowColor;
            float _GlowIntensity;
            float _GlowSpread;
            float _GlowSoftness;
            float _MarginX;
            float _MarginY;
            float _AspectRatio;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 마진 영역 고려한 알파 샘플링
            // 이미지 UV(0~1) → 내부 스프라이트 UV로 리매핑
            // 마진 영역(스프라이트 바깥)은 alpha = 0 반환
            float sampleAlpha(float2 uv)
            {
                // 내부 스프라이트 UV로 리매핑
                float2 scale = float2(1.0 - 2.0 * _MarginX, 1.0 - 2.0 * _MarginY);
                float2 inner = (uv - float2(_MarginX, _MarginY)) / max(scale, 0.001);

                // 내부 영역 밖이면 0 (마진 = 글로우 공간)
                float inX = step(0.0, inner.x) * step(inner.x, 1.0);
                float inY = step(0.0, inner.y) * step(inner.y, 1.0);

                inner = clamp(inner, 0.001, 0.999);
                return tex2Dlod(_MainTex, float4(inner, 0, 0)).a * inX * inY;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 방사형 5링 x 12방향 = 60 샘플 + 중심 1 = 61 샘플
                // 각 링에 거리 기반 가우시안 가중치 적용 → 외곽이 자연스럽게 감쇠
                float blurredAlpha = 0.0;
                float totalWeight = 0.0;
                const int DIR = 12;
                const float TWO_PI = 6.28318530718;

                // 5개 링의 거리 비율 (0.0 ~ 1.0)
                // 내부는 촘촘하게, 외부는 넓게 분포하여 그라디언트를 부드럽게 채움
                const float ringDist[5] = { 0.15, 0.35, 0.55, 0.78, 1.0 };

                // 중심 샘플 (가우시안 가중치 최대)
                float centerWeight = 1.0;
                blurredAlpha += sampleAlpha(IN.texcoord) * centerWeight;
                totalWeight += centerWeight;

                // 5개 링을 순회하며 가우시안 가중치 적용
                for (int ring = 0; ring < 5; ring++)
                {
                    float dist = ringDist[ring];

                    // 가우시안 가중치: exp(-3.0 * d²) → 중심에서 1.0, 외곽으로 갈수록 부드럽게 0에 수렴
                    float w = exp(-3.0 * dist * dist);

                    // 각 링마다 방향 오프셋을 엇갈려 배치 (모아레 방지)
                    float angleOffset = ring * 0.5;

                    for (int i = 0; i < DIR; i++)
                    {
                        float a = (i + angleOffset) * TWO_PI / DIR;
                        float2 d = float2(cos(a), sin(a));
                        d.y *= _AspectRatio;
                        blurredAlpha += sampleAlpha(IN.texcoord + d * _GlowSpread * dist) * w;
                        totalWeight += w;
                    }
                }

                // 가중 평균
                blurredAlpha /= totalWeight;

                // 소프트니스 커브 (부드러운 감쇠, pow 대신 smoothstep 혼합)
                blurredAlpha = pow(blurredAlpha, _GlowSoftness);
                // 최외곽의 미세한 잔여값을 자연스럽게 0으로 마무리
                blurredAlpha *= smoothstep(0.0, 0.08, blurredAlpha);

                // 현재 픽셀의 원본 알파 (내부인지 판별)
                float origAlpha = sampleAlpha(IN.texcoord);

                // 내부 억제: 스프라이트 내부(origAlpha 높음)는 글로우 숨김
                blurredAlpha *= (1.0 - origAlpha);

                // 글로우 색상 출력
                fixed4 col = _GlowColor;
                col.a = blurredAlpha * _GlowIntensity * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
            ENDCG
        }
    }
}
