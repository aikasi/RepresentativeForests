// SpriteGlow의 외곽선 검출 + 소프트 글로우 확산을 Unity UI에서 사용하는 셰이더.
// 원본: Sprites/Outline (Elringus/SpriteGlow)
// 추가: 거리 기반 그라데이션 글로우, UI 블렌딩/스텐실/클리핑 지원.
Shader "UI/SpriteGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Outline)]
        [MaterialToggle] _IsOutlineEnabled ("Enable Outline", float) = 1
        [HDR] _OutlineColor ("Outline Color", Color) = (0, 2, 0, 1)
        _OutlineSize ("Outline Size", Range(0, 10)) = 3
        _AlphaThreshold ("Alpha Threshold", Range(0, 1)) = 0.01

        [Header(Soft Glow)]
        _GlowSpread ("Glow Spread", Range(0, 30)) = 10
        _GlowSoftness ("Glow Softness", Range(0.5, 4.0)) = 1.5

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
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _IsOutlineEnabled;
            float _OutlineSize;
            float _AlphaThreshold;
            float _GlowSpread;
            float _GlowSoftness;
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

            // 스프라이트 내부에서 가장자리를 향해 외곽선을 그릴지 판별 (0 또는 1)
            int ShouldDrawOutlineInside(fixed4 sampledColor, float2 texCoord, int outlineSize)
            {
                if (outlineSize == 0 || sampledColor.a == 0) return 0;

                float2 texDdx = ddx(texCoord);
                float2 texDdy = ddy(texCoord);

                for (int i = 1; i <= 10; i++)
                {
                    if (i > outlineSize) break;

                    float2 up = texCoord + float2(0, i * _MainTex_TexelSize.y);
                    if (up.y > 1.0 || tex2Dgrad(_MainTex, up, texDdx, texDdy).a <= _AlphaThreshold) return 1;

                    float2 down = texCoord - float2(0, i * _MainTex_TexelSize.y);
                    if (down.y < 0.0 || tex2Dgrad(_MainTex, down, texDdx, texDdy).a <= _AlphaThreshold) return 1;

                    float2 right = texCoord + float2(i * _MainTex_TexelSize.x, 0);
                    if (right.x > 1.0 || tex2Dgrad(_MainTex, right, texDdx, texDdy).a <= _AlphaThreshold) return 1;

                    float2 left = texCoord - float2(i * _MainTex_TexelSize.x, 0);
                    if (left.x < 0.0 || tex2Dgrad(_MainTex, left, texDdx, texDdy).a <= _AlphaThreshold) return 1;
                }
                return 0;
            }

            // 8방향으로 가장 가까운 불투명 픽셀까지 거리를 탐색하여
            // 거리 기반 소프트 글로우 강도를 반환 (0.0 ~ 1.0)
            float ComputeGlowIntensity(float2 texCoord, float glowRange)
            {
                // 현재 픽셀이 불투명이면 글로우 불필요 (내부)
                fixed origAlpha = tex2D(_MainTex, texCoord).a;
                if (origAlpha > _AlphaThreshold) return 0.0;

                float minDist = 999.0;
                int range = (int)glowRange;
                if (range <= 0) return 0.0;

                float2 texDdx = ddx(texCoord);
                float2 texDdy = ddy(texCoord);

                // 8방향 탐색: 상하좌우 + 대각선
                float2 dirs[8] = {
                    float2(0, 1), float2(0, -1), float2(1, 0), float2(-1, 0),
                    float2(0.707, 0.707), float2(-0.707, 0.707),
                    float2(0.707, -0.707), float2(-0.707, -0.707)
                };

                for (int d = 0; d < 8; d++)
                {
                    float2 dir = dirs[d];
                    for (int i = 1; i <= 30; i++)
                    {
                        if (i > range) break;

                        float2 sampleUV = texCoord + dir * float2(
                            i * _MainTex_TexelSize.x,
                            i * _MainTex_TexelSize.y
                        );

                        // UV 범위 밖이면 건너뛰기
                        if (sampleUV.x < 0 || sampleUV.x > 1 || sampleUV.y < 0 || sampleUV.y > 1) break;

                        fixed sampleAlpha = tex2Dgrad(_MainTex, sampleUV, texDdx, texDdy).a;
                        if (sampleAlpha > _AlphaThreshold)
                        {
                            // 대각선은 실제 거리가 sqrt(2)배이므로 보정
                            float dist = (d < 4) ? (float)i : (float)i * 1.414;
                            minDist = min(minDist, dist);
                            break;
                        }
                    }
                }

                // 불투명 픽셀을 찾지 못함 → 글로우 범위 밖
                if (minDist >= 999.0) return 0.0;

                // 거리 기반 감쇠: 가까울수록 1.0, 멀수록 0.0
                float normalized = 1.0 - saturate(minDist / (glowRange + 1.0));
                return pow(normalized, _GlowSoftness);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, IN.texcoord) * IN.color;

                if (_IsOutlineEnabled > 0.5)
                {
                    // 내부 외곽선 (하드엣지)
                    if (_OutlineSize > 0)
                    {
                        int shouldDrawOutline = ShouldDrawOutlineInside(color, IN.texcoord, (int)_OutlineSize);
                        color.rgb = lerp(color.rgb, _OutlineColor.rgb * _OutlineColor.a, shouldDrawOutline);
                    }

                    // 외부 소프트 글로우 (거리 기반 그라데이션)
                    if (_GlowSpread > 0)
                    {
                        float glowIntensity = ComputeGlowIntensity(IN.texcoord, _GlowSpread);
                        if (glowIntensity > 0.001)
                        {
                            // 글로우 색상을 현재 픽셀에 블렌딩
                            color.rgb = lerp(color.rgb, _OutlineColor.rgb, glowIntensity);
                            color.a = max(color.a, glowIntensity * _OutlineColor.a);
                        }
                    }
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
