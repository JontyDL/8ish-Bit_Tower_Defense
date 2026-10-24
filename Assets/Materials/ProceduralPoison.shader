Shader "Custom/ProceduralPoison"
{
    Properties
    {
        _FogColor ("Poison Color", Color) = (0.25, 0.85, 0.18, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.65
        _NoiseScale ("Noise Scale", Float) = 0.8
        _DriftSpeed ("Drift Speed", Float) = 0.25
        _PatternStrength ("Pattern Strength", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float _Opacity;
                float _NoiseScale;
                float _DriftSpeed;
                float _PatternStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 drift = float2(
                    _Time.y * _DriftSpeed,
                    _Time.y * _DriftSpeed * 0.63
                );

                float2 p = input.positionWS.xz * max(_NoiseScale, 0.001);

                float broadNoise = Noise(p + drift);
                float fineNoise = Noise(p * 2.1 - drift * 0.7);

                float noiseValue = lerp(
                    broadNoise,
                    fineNoise,
                    0.35
                );

                float variation = lerp(
                    1.0,
                    smoothstep(0.15, 0.85, noiseValue),
                    _PatternStrength
                );

                float alpha = saturate(_Opacity * variation);

                return half4(_FogColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
