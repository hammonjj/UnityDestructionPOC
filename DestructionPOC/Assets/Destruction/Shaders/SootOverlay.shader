// Soot and ash on the ground after a fire: a quad laid just above the lot surface that multiplies what is
// under it toward black with world-space noise. Unlit and order-independent (multiply), so it darkens the
// asphalt, paint lines and anything else it overlaps without needing its own lighting.
Shader "DestructionLab/Soot Overlay"
{
    Properties
    {
        _SootColor ("Soot", Color) = (0.08, 0.07, 0.065, 1)
        _Coverage ("Coverage", Range(0, 1)) = 0.6
        _Strength ("Strength", Range(0, 1)) = 0.85
        _Scale ("Blotch scale (1/m)", Float) = 0.12
        _FineScale ("Fine scale (1/m)", Float) = 1.3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }

        Pass
        {
            Name "SootMultiply"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SootColor;
                half _Coverage;
                half _Strength;
                float _Scale;
                float _FineScale;
            CBUFFER_END

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0, amp = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    sum += amp * ValueNoise(p);
                    p = p * 2.07 + 11.3;
                    amp *= 0.5;
                }
                return sum;
            }

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;
                float broad = Fbm(p * _Scale);
                float fine = Fbm(p * _FineScale + 5.0);
                // coverage lowers the threshold: 1 covers nearly everything
                float t = lerp(0.62, 0.3, _Coverage);
                float mask = saturate((broad + (fine - 0.5) * 0.35 - t) / 0.18) * _Strength;
                return half4(lerp(half3(1, 1, 1), _SootColor.rgb, mask), 1);
            }
            ENDHLSL
        }
    }
}
