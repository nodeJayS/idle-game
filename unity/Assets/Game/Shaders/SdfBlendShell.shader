// SDF blend-shell shader (ROADMAP 4, slice 1). A SEAMLESS body wearing a FACETED skin:
//   * BODY = one draw call built from primitive spheres/capsules whose source meshes only
//     need to sit NEAR their primitive. The vertex stage SNAPS every vertex onto the combined
//     smooth-min SDF surface of ALL primitives, so overlapping shapes converge onto one blended
//     surface and the seams between primitives cease to exist. Colors blend by SDF proximity.
//   * SKIN = the hard art rule (monsters stay faceted Tunic style). Normals do NOT come from the
//     SDF gradient (that would look smooth/organic). The fragment stage derives a FLAT FACETED
//     normal from screen-space derivatives of the world position — free, and it faceting-quantises
//     to the mesh triangles regardless of how the verts were snapped.
// Lighting mirrors TunicSurface's URP forward pass: hard N·L main light + SampleSH ambient, fog,
// vertex-colour albedo. No textures, no per-pixel SDF, no loops beyond _PrimCount (mobile-sane).
//
// Primitive data is pushed per-renderer via a MaterialPropertyBlock (see SdfBlobRig.cs):
//   _PrimPosRad[i]  = world pos xyz, radius w
//   _PrimAxisLen[i] = capsule axis xyz (unit), half-length w (0 => sphere)
//   _PrimColor[i]   = rgb color, w = that primitive's blend radius k
//   _PrimCount      = live primitive count
Shader "IdleGame/SdfBlendShell"
{
    Properties
    {
        _ShadowImpact("Shadow Strength", Range(0,1)) = 0.7
        _EdgeSharp   ("Edge Ink Sharpness", Float)   = 6.0   // reuse TunicSurface's inked-crease look
        _EdgeDark    ("Edge Ink Strength", Range(0,1)) = 0.30
        _ColorSharp  ("Color Blend Sharpness", Float) = 2.5  // >1 tightens proximity weights so colours stop washing to pastel
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ---- Main lit pass (same URP boilerplate as TunicSurface's ForwardLit) -------------
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define MAX_PRIMS 16

            CBUFFER_START(UnityPerMaterial)
                float _ShadowImpact;
                float _EdgeSharp;
                float _EdgeDark;
                float _ColorSharp;
            CBUFFER_END

            // Per-renderer primitive arrays (set via MaterialPropertyBlock). These live OUTSIDE
            // UnityPerMaterial on purpose: a MaterialPropertyBlock overrides individual uniforms,
            // and array uniforms are addressed by name at that scope.
            float4 _PrimPosRad[MAX_PRIMS];
            float4 _PrimAxisLen[MAX_PRIMS];
            float4 _PrimColor[MAX_PRIMS];
            // float, not int: MaterialPropertyBlock.SetInt is an alias for SetFloat (float
            // storage), so an int uniform here can read garbage on some platforms. Cast at use.
            float  _PrimCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;   // interpolated -> ddx/ddy give the facet normal
                float4 color      : TEXCOORD1;   // proximity-blended primitive colour
                float  fogFactor  : TEXCOORD2;
            };

            // --- SDF primitives -------------------------------------------------------------
            // Signed distance to primitive i at world point p. Capsule when half-length > 0,
            // else sphere. Standard iq capsule: clamp the projection of (p-a) onto the axis.
            float SdfPrim(float3 p, int i)
            {
                float3 c   = _PrimPosRad[i].xyz;
                float  r   = _PrimPosRad[i].w;
                float3 ax  = _PrimAxisLen[i].xyz;
                float  hl  = _PrimAxisLen[i].w;
                if (hl <= 1e-4)
                    return length(p - c) - r;            // sphere
                float t = clamp(dot(p - c, ax), -hl, hl); // nearest point on the segment
                return length(p - (c + ax * t)) - r;      // capsule
            }

            // Polynomial smooth-min (iq): blends two distances over a soft radius k, so two
            // overlapping surfaces melt into one. k comes from the INCOMING primitive's color.w
            // (documented choice: simplest & stable — each primitive advertises how melty it is;
            // a thin antenna with small k barely blends, a fat body with big k blobs generously).
            float Smin(float a, float b, float k)
            {
                if (k <= 1e-4) return min(a, b);
                float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // Combined SDF of the whole body: fold every primitive in with its own k.
            float SdfScene(float3 p)
            {
                float d = 1e9;
                int n = (int)_PrimCount;
                [loop] for (int i = 0; i < n; i++)
                    d = Smin(d, SdfPrim(p, i), _PrimColor[i].w);
                return d;
            }

            // Gradient by central differences (6 taps). Points "outward" from the surface; used
            // ONLY to march the vertex onto the surface — NEVER as a shading normal.
            float3 SdfGrad(float3 p)
            {
                const float e = 0.01;
                float2 h = float2(e, 0.0);
                return normalize(float3(
                    SdfScene(p + h.xyy) - SdfScene(p - h.xyy),
                    SdfScene(p + h.yxy) - SdfScene(p - h.yxy),
                    SdfScene(p + h.yyx) - SdfScene(p - h.yyx)));
            }

            // Proximity-weighted colour: weight each primitive by exp(-max(0,dist_i)/k_i) so the
            // nearest primitive(s) dominate and colours cross-fade smoothly inside blend regions
            // (documented weighting — matches the "colors blend by SDF proximity" charter).
            float3 BlendColor(float3 p)
            {
                float3 sum = 0.0;
                float  wsum = 1e-5;
                int n = (int)_PrimCount;
                [loop] for (int i = 0; i < n; i++)
                {
                    float k = max(_PrimColor[i].w, 0.05);
                    // _ColorSharp (>1) tightens the falloff so the nearest primitive dominates
                    // harder — cures the slice-1 pastel wash where every colour over-mixed.
                    float w = exp(-max(0.0, SdfPrim(p, i)) * _ColorSharp / k);
                    sum  += _PrimColor[i].rgb * w;
                    wsum += w;
                }
                return sum / wsum;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 p = TransformObjectToWorld(IN.positionOS.xyz);

                // Snap onto the blended surface: 3 Newton-ish steps p -= grad(p)*sdf(p). Source
                // verts start NEAR their primitive so a few steps land them on the combined shell.
                [unroll] for (int it = 0; it < 3; it++)
                    p -= SdfGrad(p) * SdfScene(p);

                OUT.positionWS = p;
                OUT.positionCS = TransformWorldToHClip(p);
                OUT.color      = float4(BlendColor(p), 1.0);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // FACETED normal from screen-space derivatives of the interpolated world pos. The
                // whole variant lives on this line: it quantises to the triangle the pixel is in,
                // so the snapped-smooth body still wears crisp Tunic facets. ddy-cross-ddx (this
                // order) gives an OUTWARD normal for URP's clip-space handedness; if facets read
                // inverted (lit from the wrong side), swap to cross(ddx, ddy).
                float3 N = normalize(cross(ddy(IN.positionWS), ddx(IN.positionWS)));

                half3 albedo = IN.color.rgb;

                // Inked facet edges: fwidth of the derived normal spikes at facet creases (same
                // trick as TunicSurface — flat inside a facet, discontinuous at its border).
                half facet = saturate(length(fwidth(N)) * _EdgeSharp);
                albedo *= 1.0 - facet * _EdgeDark;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Crisp flat lighting: hard N·L + SH ambient, exactly like TunicSurface.
                half ndl = saturate(dot(N, mainLight.direction));
                half shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowImpact);
                half3 ambient = SampleSH(N);
                half3 lit = albedo * (ambient + mainLight.color * (ndl * shadow));

                lit = MixFog(lit, IN.fogFactor);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
