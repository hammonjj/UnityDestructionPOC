// Fire-damaged look for destruction pieces. The piece's material colour (_BaseColor, set per destruction
// material and per tinted piece) is pulled toward smoke stain and then char by world-space noise, so soot
// runs continuously across neighbouring pieces and across the fragments of a shattered one. Soot is
// heavier higher up, where smoke poured out, and streaks vertically.
// Lighting: main light with shadows, plus spherical-harmonic ambient. Instancing and SRP-batcher friendly.
Shader "DestructionLab/Burnt Piece"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.7, 0.7, 0.7, 1)
        _Color ("Color (legacy alias)", Color) = (0.7, 0.7, 0.7, 1)
        _Soot ("Soot amount", Range(0, 1)) = 0.85
        _StainColor ("Smoke stain tint", Color) = (0.16, 0.12, 0.09, 1)
        _CharColor ("Char", Color) = (0.018, 0.016, 0.015, 1)
        _NoiseScale ("Blotch scale (1/m)", Float) = 0.9
        _StreakScale ("Streak scale (1/m)", Float) = 2.5
        _SootTop ("Height of full soot, m", Float) = 4.8
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Color;
            half _Soot;
            half4 _StainColor;
            half4 _CharColor;
            float _NoiseScale;
            float _StreakScale;
            float _SootTop;
            half _Smoothness;
        CBUFFER_END

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float3 u = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i);
            float n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0));
            float n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1));
            float n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1));
            float n111 = Hash31(i + float3(1, 1, 1));
            float x00 = lerp(n000, n100, u.x), x10 = lerp(n010, n110, u.x);
            float x01 = lerp(n001, n101, u.x), x11 = lerp(n011, n111, u.x);
            return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
        }

        float Fbm(float3 p)
        {
            float sum = 0, amp = 0.5;
            for (int k = 0; k < 5; k++)
            {
                sum += amp * ValueNoise(p);
                p = p * 2.03 + 17.1;
                amp *= 0.5;
            }
            return sum;
        }

        // 0 = clean, 0.5 = smoke stained, 1 = charred.
        float SootAmount(float3 positionWS)
        {
            float height = lerp(0.6, 1.0, saturate((positionWS.y - 0.3) / max(0.1, _SootTop - 0.3)));
            float blotch = (saturate((Fbm(positionWS * _NoiseScale) - 0.3) / 0.4) - 0.5) * 0.5;
            float streak = (saturate((Fbm(positionWS * float3(_StreakScale, _StreakScale * 0.12, _StreakScale)) - 0.4) / 0.2) - 0.5) * 0.4;
            return saturate((height * _Soot * 1.4 + blotch + streak) / 1.1);
        }

        half3 BurntAlbedo(half3 baseColor, float3 positionWS, out float amount)
        {
            amount = SootAmount(positionWS);
            // colours are linear: a stain that reads as mid grey on screen is already dark here
            half3 stained = baseColor * 0.18 + _StainColor.rgb * 0.25;
            half3 c = lerp(baseColor, stained, saturate(amount / 0.45));
            return lerp(c, _CharColor.rgb, saturate((amount - 0.4) / 0.6));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float amount;
                half3 albedo = BurntAlbedo(_BaseColor.rgb, i.positionWS, amount);
                float3 n = normalize(i.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, light.direction));
                half3 diffuse = albedo * (light.color * ndl * light.shadowAttenuation + SampleSH(n));
                // a faint sheen on clean surfaces only; char is matte
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 h = normalize(light.direction + v);
                half smooth = _Smoothness * (1.0 - amount);
                half spec = pow(saturate(dot(n, h)), exp2(10 * smooth + 1)) * smooth * 0.25;
                half3 color = diffuse + spec * light.color * ndl * light.shadowAttenuation;
                return half4(MixFog(color, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
