// Shell fur for URP, cheap enough for mobile AR.
//
// The body mesh is drawn several extra times ("shells"), each pushed a little further out along the normals.
// Each shell keeps only the pixels that belong to a hair strand at that height and discards the rest, so the
// stacked shells read as thousands of short tapered hairs. FurBrainFur.cs creates one material per shell from this
// shader and sets _ShellHeight (0 = skin, 1 = fur tips) on each.
//
// Strands are procedural: the UVs are divided into a grid of cells (_Density cells per UV unit), and each cell
// holds one strand with a random length. No noise texture is needed.
Shader "FurBrains/ShellFur"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _FurMask("Fur Mask (R = fur length, black = no fur)", 2D) = "white" {}

        _FurLength("Fur Length (model units)", Float) = 0.012
        _Density("Strands per UV unit", Float) = 1200
        _Thickness("Strand Thickness", Range(0.05, 1.5)) = 1
        _Taper("Taper (low = blunt, high = pointed)", Range(0.1, 2)) = 0.5
        _LengthVariation("Length Variation", Range(0, 1)) = 0.5
        _Undercoat("Undercoat (solid inner layer)", Range(0, 1)) = 0.3
        _Gravity("Gravity Droop", Range(0, 1)) = 0.3
        _RootDarkness("Root Darkness", Range(0, 1)) = 0.55
        _Ambient("Ambient Boost", Range(0, 2)) = 1

        // Set per shell by FurBrainFur; not meant to be edited by hand.
        [HideInInspector] _ShellHeight("Shell Height", Range(0, 1)) = 0.5
    }

    SubShader
    {
        // AlphaTest queue: drawn after the solid body, with depth writes, so no transparency sorting is needed.
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Everything per-material lives in this buffer so the SRP Batcher can batch all shells cheaply.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _FurMask_ST;
            float _FurLength;
            float _Density;
            float _Thickness;
            float _Taper;
            float _LengthVariation;
            float _Undercoat;
            float _Gravity;
            float _RootDarkness;
            float _Ambient;
            float _ShellHeight;
        CBUFFER_END

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_FurMask);    SAMPLER(sampler_FurMask);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float  mask       : TEXCOORD2;
        };

        // Cheap 2D -> 2D hash (Dave Hoskins), gives each grid cell its own stable random numbers.
        float2 Hash22(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.xx + p3.yz) * p3.zy);
        }

        Varyings FurVertex(Attributes input)
        {
            Varyings output;
            float h = _ShellHeight;
            float mask = SAMPLE_TEXTURE2D_LOD(_FurMask, sampler_FurMask, input.uv, 0).r;

            // Push out along the normal, and bend toward the ground more the further out we are (h * h).
            // Done in object space so the fur scales with the character when the player pinch-scales it.
            float3 normalOS = normalize(input.normalOS);
            float3 downOS = TransformWorldToObjectDir(float3(0, -1, 0));
            float3 offset = (normalOS * h + downOS * (_Gravity * h * h)) * (_FurLength * mask);

            output.positionCS = TransformObjectToHClip(input.positionOS.xyz + offset);
            output.normalWS = TransformObjectToWorldNormal(normalOS);
            output.uv = input.uv;
            output.mask = mask;
            return output;
        }

        // How far this pixel is inside one layer of strands (> 0 = inside a strand).
        // Each grid cell holds one strand at a random spot in the cell with a random length.
        float StrandLayer(float2 grid)
        {
            float2 cell = floor(grid);
            float2 rnd = Hash22(cell);
            float2 rnd2 = Hash22(cell + 17.31);

            // Relative to the local fur length: the mask already shortens the shells themselves in FurVertex.
            float strandLength = 1.0 - _LengthVariation * rnd.x;
            float t = _ShellHeight / max(strandLength, 1e-4);   // 0 at the root, 1 at this strand's tip

            // Distance from the strand's centre, in half-cells (1 = from the centre to the cell's edge).
            float2 centre = lerp(0.25, 0.75, rnd2);
            float dist = length(frac(grid) - centre) * 2.0;

            // Radius shrinks toward the tip. _Taper < 1 keeps strands thick for most of their length (soft, blunt
            // tufts); 1 is a cone (needles). Thickness > 1 lets neighbouring strands overlap so no gaps show.
            float radius = _Thickness * pow(saturate(1.0 - t), _Taper);
            return min(1.0 - t, radius - dist);
        }

        // Discards pixels of this shell that aren't inside a strand.
        void ClipToStrand(float2 uv, float mask)
        {
            // Two strand layers on different, rotated grids with different densities. Their patterns don't line up,
            // which hides the regular grid and fills the gaps a single grid leaves.
            float2 grid1 = uv * _Density;
            float2 grid2 = mul(float2x2(0.866, -0.5, 0.5, 0.866), uv) * (_Density * 0.73) + 0.37;

            // No fur here (under clothing, around eyes). Below a few percent the shells would sit almost exactly on
            // the skin and flicker against it, so treat those as bald too.
            if (mask <= 0.05)
                clip(-1);

            // Undercoat: the innermost shells are kept solid, so the skin never shows between strands.
            if (_ShellHeight <= _Undercoat)
                return;

            clip(max(StrandLayer(grid1), StrandLayer(grid2)));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex FurVertex
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                ClipToStrand(input.uv, input.mask);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                // Fake self-shadowing: hair is darker near the skin, where light can't reach.
                half occlusion = lerp(1.0 - _RootDarkness, 1.0, _ShellHeight);

                // Soft "wrapped" diffuse plus ambient light from the scene's light probe (AR light estimation feeds it).
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half wrap = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                half3 lighting = mainLight.color * wrap + SampleSH(normalWS) * _Ambient;

                return half4(albedo * occlusion * lighting, 1);
            }
            ENDHLSL
        }

        // Depth-only pass with the same cut-out, for URP features that need a depth prepass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex FurVertex
            #pragma fragment Frag

            half Frag(Varyings input) : SV_Target
            {
                ClipToStrand(input.uv, input.mask);
                return 0;
            }
            ENDHLSL
        }

        // No ShadowCaster pass on purpose: the solid body already casts the shadow.
    }
}
