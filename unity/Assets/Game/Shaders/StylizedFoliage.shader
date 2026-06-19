// Stylized foliage/prop shader (Phase 2.5 art pass). The soft "painterly low-poly"
// look comes from here, not the meshes: wrapped (half-lambert-ish) diffuse so light
// bleeds softly around forms, a vertical colour gradient (deeper underside -> lighter
// top), softened main-light shadows, and an optional gentle wind sway on foliage.
// Pure URP forward-lit; configured per material from Scenery.cs.
Shader "IdleGame/StylizedFoliage"
{
    Properties
    {
        _BaseColor   ("Base (bottom) Color", Color) = (0.20, 0.40, 0.25, 1)
        _TopColor    ("Top Color", Color)           = (0.46, 0.66, 0.42, 1)
        _GradHeight  ("Gradient Height", Float)     = 1.4
        _GradBias    ("Gradient Bias", Float)       = 0.0
        _Wrap        ("Light Wrap", Range(0,1))     = 0.55
        _ShadowImpact("Shadow Strength", Range(0,1))= 0.5
        _WindStrength("Wind Strength", Float)       = 0.0
        _WindSpeed   ("Wind Speed", Float)          = 1.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ---- Main lit pass -------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float _GradHeight;
                float _GradBias;
                float _Wrap;
                float _ShadowImpact;
                float _WindStrength;
                float _WindSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  gradT      : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = IN.positionOS.xyz;
                OUT.gradT = saturate(posOS.y / max(0.0001, _GradHeight) + _GradBias);

                float3 posWS = TransformObjectToWorld(posOS);
                // Sway scales with height above the pivot, so trunks/bottoms stay planted.
                float mask = saturate(posOS.y);
                float phase = _TimeParameters.x * _WindSpeed + posWS.x * 0.5 + posWS.z * 0.5;
                posWS.x += sin(phase) * _WindStrength * mask;
                posWS.z += cos(phase * 0.8) * _WindStrength * mask;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 N = normalize(IN.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Wrapped diffuse: light bleeds past the terminator for a soft, round look.
                half ndl = dot(N, mainLight.direction);
                half wrapped = saturate(ndl * (1.0 - _Wrap) + _Wrap);
                half shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowImpact);

                half3 albedo = lerp(_BaseColor.rgb, _TopColor.rgb, IN.gradT);
                half3 ambient = SampleSH(N);
                half3 lit = albedo * (ambient + mainLight.color * (wrapped * shadow));

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ---- Shadow caster (so props drop shadows) -------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor; float4 _TopColor;
                float _GradHeight; float _GradBias; float _Wrap; float _ShadowImpact;
                float _WindStrength; float _WindSpeed;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings shadowVert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ---- Depth only (depth prepass / SSAO source) ----------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor; float4 _TopColor;
                float _GradHeight; float _GradBias; float _Wrap; float _ShadowImpact;
                float _WindStrength; float _WindSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings depthVert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 depthFrag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
