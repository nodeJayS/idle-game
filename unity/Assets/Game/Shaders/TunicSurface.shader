// Tunic-style surface shader (art pivot). The signature look lives here, not in textures:
//   * NORMAL-DRIVEN HEIGHT BLEND — upward-facing facets get grass/moss (_TopColor),
//     slopes & walls get dirt/stone (_SideColor), blended by world-space normal.y. This
//     is what paints moss on top and dirt on the sides of any faceted mesh automatically.
//   * FACETED EDGE INK — facet creases are darkened via the screen-space derivative of
//     the world normal (fwidth), giving the crisp "inked" Tunic edges with no baked AO.
//   * CRISP FLAT LIGHTING — a hard N·L (optionally posterized into bands), NOT the soft
//     wrapped half-lambert of the old StylizedFoliage shader.
//   * FLAT VERTEX COLORS — albedo is multiplied by the mesh vertex colour (default white)
//     so hand-authored flat tints work.
// Optional gentle wind sway (foliage). Pure URP forward-lit; configured per material.
Shader "IdleGame/TunicSurface"
{
    Properties
    {
        _TopColor    ("Top (grass/moss) Color", Color) = (0.42, 0.62, 0.34, 1)
        _SideColor   ("Side (dirt/stone) Color", Color) = (0.40, 0.30, 0.20, 1)
        _SlopeLo     ("Slope Blend Low (N.y)", Range(-1,1))  = 0.45
        _SlopeHi     ("Slope Blend High (N.y)", Range(-1,1)) = 0.80
        _EdgeSharp   ("Edge Ink Sharpness", Float)      = 6.0
        _EdgeDark    ("Edge Ink Strength", Range(0,1))  = 0.35
        _Bands       ("Toon Banding", Range(0,1))       = 0.0
        _ShadowImpact("Shadow Strength", Range(0,1))    = 0.7
        _AmbientFloor("Ambient Floor", Range(0,1))      = 0.0
        _WindStrength("Wind Strength", Float)           = 0.0
        _WindSpeed   ("Wind Speed", Float)              = 1.5
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
            #pragma multi_compile_fragment _ _LIGHT_COOKIES   // sample the sun's dappled cookie
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _SideColor;
                float _SlopeLo;
                float _SlopeHi;
                float _EdgeSharp;
                float _EdgeDark;
                float _Bands;
                float _ShadowImpact;
                float _AmbientFloor;
                float _WindStrength;
                float _WindSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = IN.positionOS.xyz;
                float3 posWS = TransformObjectToWorld(posOS);

                // Sway scales with height above the pivot, so trunks/bottoms stay planted.
                float mask = saturate(posOS.y);
                float phase = _TimeParameters.x * _WindSpeed + posWS.x * 0.5 + posWS.z * 0.5;
                posWS.x += sin(phase) * _WindStrength * mask;
                posWS.z += cos(phase * 0.8) * _WindStrength * mask;

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 N = normalize(IN.normalWS);

                // Height blend: upward-facing facets -> grass/moss, slopes/walls -> dirt.
                half up = smoothstep(_SlopeLo, _SlopeHi, N.y);
                half3 albedo = lerp(_SideColor.rgb, _TopColor.rgb, up);
                albedo *= IN.color.rgb; // flat vertex-colour tint (white = no tint)

                // Inked facet edges: fwidth of the world normal spikes at facet creases
                // (flat-shaded faces are constant inside, discontinuous at their borders).
                half facet = saturate(length(fwidth(N)) * _EdgeSharp);
                albedo *= 1.0 - facet * _EdgeDark;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                // Cookie-aware overload so the sun's dappled light cookie (Tunic dapples)
                // multiplies the main light — the plain GetMainLight(shadowCoord) ignores it.
                Light mainLight = GetMainLight(shadowCoord, IN.positionWS, half4(1, 1, 1, 1));

                // Crisp flat lighting: a hard N·L, optionally posterized into two bands.
                half ndl = saturate(dot(N, mainLight.direction));
                half banded = smoothstep(0.42, 0.58, ndl);
                ndl = lerp(ndl, banded, _Bands);
                ndl = max(ndl, _AmbientFloor); // keep shaded sides from going to pure ambient
                half shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowImpact);

                half3 ambient = SampleSH(N);
                half3 lit = albedo * (ambient + mainLight.color * (ndl * shadow));

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ---- Shadow caster (so surfaces/props drop shadows) ----------------
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
                float4 _TopColor; float4 _SideColor;
                float _SlopeLo; float _SlopeHi; float _EdgeSharp; float _EdgeDark;
                float _Bands; float _ShadowImpact; float _AmbientFloor;
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
                float4 _TopColor; float4 _SideColor;
                float _SlopeLo; float _SlopeHi; float _EdgeSharp; float _EdgeDark;
                float _Bands; float _ShadowImpact; float _AmbientFloor;
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
