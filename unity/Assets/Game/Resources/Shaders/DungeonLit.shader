// Dungeon level-geometry shader (roguelite slice 2 — client render of the DungeonGen data).
//
// TunicSurface is MAIN-LIGHT-ONLY (verified: it never loops URP additional lights), but the
// dungeon look is a sea of tiny warm POINT lights — so this is a separate minimal URP Lit pass
// that DOES loop the additional lights. The geometry is flat-shaded via duplicated verts (the
// mesh owns the faceting, as in ArenaTerrain/Ground), so plain interpolated normals are fine
// here — no fwidth facet ink, no slope blend, no textures.
//
// Lighting matches the reference's Lambert exactly: diffuse only (N·L, no specular), main
// directional + SH ambient + per-fragment additional point lights (GetAdditionalLight loop).
// No shadows received or cast (the reference has none — keeps the draw budget for the torch sea).
// Albedo = per-vertex colour; the floor/wall colour pipeline is baked into those vertex colours
// by DungeonRenderer, so the shader is deliberately dumb.
Shader "IdleGame/DungeonLit"
{
    Properties
    {
        _AmbientBoost ("Ambient Boost", Range(0,2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // NOTE: no URP additional-light keywords on purpose — the point lights come from our
            // own global arrays (see below), so no _ADDITIONAL_LIGHTS/_CLUSTER_LIGHT_LOOP variants.
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _AmbientBoost;
            CBUFFER_END

            // The dungeon's own point-light table (globals pushed by DungeonFlicker every frame,
            // flicker folded into the colours). A dungeon runs ~12 static lights we fully own, so
            // looping our own arrays sidesteps URP's per-path light plumbing entirely — the URP
            // additional-light route behaved differently in the game view, scene view, and
            // SingleCameraRequest renders (the Forward+ cluster macros rendered BLACK offscreen),
            // while globals are identical everywhere. Pos.w = range; Color.rgb premultiplied by
            // intensity.
            #define DUNGEON_MAX_LIGHTS 16
            float4 _DungeonLightPos[DUNGEON_MAX_LIGHTS];
            half4  _DungeonLightColor[DUNGEON_MAX_LIGHTS];
            float  _DungeonLightCount;

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

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
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
                half3 albedo = IN.color.rgb;

                // Main directional: crisp diffuse Lambert (no shadow sampling — reference has none).
                Light mainLight = GetMainLight();
                half ndl = saturate(dot(N, mainLight.direction));
                half3 ambient = SampleSH(N) * _AmbientBoost;
                half3 lit = albedo * (ambient + mainLight.color * ndl);

                // The torch/key point-light sea, from OUR light table (see the declaration above).
                // Per-fragment Lambert with URP-style smooth range falloff: 1/d² windowed by
                // (1-(d/range)⁴)² so a light dies exactly at its range, like the reference's
                // three.js decay-2 lights.
                int count = min((int)_DungeonLightCount, DUNGEON_MAX_LIGHTS);
                for (int li = 0; li < count; li++)
                {
                    float3 toL = _DungeonLightPos[li].xyz - IN.positionWS;
                    float dsq = max(dot(toL, toL), 0.0001);
                    float range = max(_DungeonLightPos[li].w, 0.001);
                    float w2 = saturate(1.0 - (dsq * dsq) / (range * range * range * range));
                    float atten = (w2 * w2) / dsq;
                    half nl = saturate(dot(N, normalize(toL)));
                    lit += albedo * _DungeonLightColor[li].rgb * (nl * atten);
                }

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
