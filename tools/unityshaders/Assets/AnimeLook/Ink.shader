// AnimeLook/Ink: made from tools/shaderpack/anime.glsl by tools/unityshaders/port.py. Do not edit: edit the GLSL and run that again.
Shader "AnimeLook/Ink"
{
    Properties
    {

    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One Zero
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma only_renderers d3d11 glcore metal vulkan
            #include "UnityCG.cginc"

            // (the GLSL has each of the game's matrices as its four columns; HLSL has them as rows)
            #define hlslcc_mtx4x4unity_ObjectToWorld transpose(unity_ObjectToWorld)
            #define hlslcc_mtx4x4unity_WorldToObject transpose(unity_WorldToObject)
            #define hlslcc_mtx4x4unity_MatrixVP transpose(UNITY_MATRIX_VP)
            #define hlslcc_mtx4x4unity_MatrixV transpose(UNITY_MATRIX_V)
            #define SampleLod(S, uv, lod) S.SampleLevel(sampler##S, uv, lod)
            #define SampleFlat(S, uv) S.SampleLevel(sampler##S, uv, 0.0)
            #define SampleMipped(S, uv) S.Sample(sampler##S, uv)
            #define SampleGradient(S, uv, dx, dy) S.SampleGrad(sampler##S, uv, dx, dy)
            #define Mod(x, y) ((x) - (y) * floor((x) / (y)))
            #define GreaterThan(a, b) ((a) > (b))
            #define GreaterThanEqual(a, b) ((a) >= (b))
            #define LessThan(a, b) ((a) < (b))
            #define LessThanEqual(a, b) ((a) <= (b))
            float4 TexelFetch(Texture2D t, int2 p, int lod) { return t.Load(int3(p, lod)); }
            float4 TexelFetch(Texture3D t, int3 p, int lod) { return t.Load(int4(p, lod)); }
            int2 TextureSize(Texture2D t, int lod) { uint w, h; t.GetDimensions(w, h); return int2(w, h); }
            bool AllZero(float4 v) { return !any(v); }
            // (one of a vector's numbers, chosen by a variable, read and given a new value: see chosen in tools/unityshaders/port.py)
            float Pick(float2 v, int n) { return n <= 0 ? v.x : v.y; }
            float Pick(float3 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : v.z; }
            float Pick(float4 v, int n) { return n <= 0 ? v.x : n == 1 ? v.y : n == 2 ? v.z : v.w; }
            float2 Put(float2 v, int n, float x) { return float2(n == 0 ? x : v.x, n == 1 ? x : v.y); }
            float3 Put(float3 v, int n, float x) { return float3(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z); }
            float4 Put(float4 v, int n, float x) { return float4(n == 0 ? x : v.x, n == 1 ? x : v.y, n == 2 ? x : v.z, n == 3 ? x : v.w); }
            // gl_FragCoord: which pixel is being drawn, its rows counted from the bottom, as every picture's are read here. Direct3D counts
            // the rows of what it draws into from the top, and the game draws into its pictures upside down to make up for it (saying so with
            // _ProjectionParams.x at -1): so where it is drawing straight to the screen, the row is turned over, or each picture read by pixel
            // (the camera's depth, the smoke drawn small) would be read upside down.
            float4 FragCoord(float4 pos)
            {
            #if UNITY_UV_STARTS_AT_TOP
                if (_ProjectionParams.x > 0.0) pos.y = _ScreenParams.y - pos.y;
            #endif
                return pos;
            }

            float4 _AnimeInk;     // x: the lines' width, pixels; y: how dark they are (0 none, 1 black); z: how much bend makes a line; w: how far off they fade out, m
            float4 _AnimePaint;     // x: a step of the ladder, in stops (0: none); y: how much of the smoothing into patches is laid on; z: how strong the colours (1: as they are); w: what to show (development: 1 the lines alone, 2 the bend, 3 solid or not)
            float4 _AnimeHow;     // x: 1 if the depths run backwards (nearest is 1); y: how many of the depth picture's steps a bend must be to count; z: how far out a patch of the paint reaches, pixels (0: no flattening); w: 1 if there is a depth picture from the camera before this one
            float4 _AnimeMore;     // x: how much of the stepping of brightness is laid on; y: how far the steps lean to the brighter tone (0 evenly, 0.5 always the brighter); z: from what part of their reach the lines begin to fade
            Texture2D _AnimeScene; SamplerState sampler_AnimeScene;     // the picture so far
            Texture2D _CameraDepthTexture; SamplerState sampler_CameraDepthTexture;
            Texture2D _AnimeBehind; SamplerState sampler_AnimeBehind;     // the depth picture of the camera before this one (see deep)

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 vs_TEXCOORD0 : TEXCOORD0;
            };

            // Anime Look: ink lines and flat paint (Ink.cs). One triangle over the whole picture.

            v2f vert(float3 in_POSITION0 : POSITION)
            {
                v2f OUT = (v2f)0;
                OUT.pos = float4(in_POSITION0.xy, 0.5, 1.0);
                // (drawn into whatever the camera is drawing into, which may lie either way: as for the lens's second go, above; Natural
                // Light's veil and the sea's lens in the water the same; and Cinema Camera's look, whose picture came out upside down on
                // Direct3D without it, the camera drawing into a picture of its own there (the PC, 2026-10-20))
                float way = 1.0;
#if UNITY_UV_STARTS_AT_TOP
                way = _ProjectionParams.x;
#endif
                OUT.vs_TEXCOORD0 = float2(in_POSITION0.x * 0.5 + 0.5, in_POSITION0.y * way * 0.5 + 0.5);
                return OUT;
            }

            // The picture of the solid things, just drawn, made over as a drawing: before the sky's haze, the weather, smoke, flames and
            // anything else see-through is laid on it, so that those go over the lines as they go over the things.
            //
            // Ink. A line wherever the depth picture has a step (one thing in front of another: an outline) or a fold (two faces of one
            // thing meeting: an edge). What the depth picture holds is, but for a scale, one over the distance, and across a flat face
            // that runs straight over the screen however the face lies to the camera. So its bend, a pixel's value against the mean of
            // those to either side, is nothing on any flat face, however steeply seen, and something at every step and fold. Taken
            // against the nearest of the values, it is a number without a unit: about twice the line's width in pixels times a pixel's
            // angle at a right-angled edge, nearly 1 at an outline against the sky.
            //
            // The depth picture holds its values in steps (one part in sixteen million of its whole range), and far off, where a metre
            // is less than a step, flat ground comes out as a stair: a bend at every tread, and lines across a field where there is
            // no edge. So a bend also has to be several of those steps before it counts, which far off leaves only outlines.
            //
            // Paint. A cel is painted in flat tones, and what makes a game's picture not look painted is less its shading than the
            // grain of its textures. So first each solid pixel takes the mean colour of whichever of four patches round it (up-left,
            // up-right, down-left, down-right) is the most even, the patches weighed smoothly by how even they are so that nothing
            // flips from frame to frame: grain and grime go to flat patches, and an edge stays an edge, since the patch that crosses
            // it is the uneven one. Then the brightness is drawn toward the nearest of a ladder of steps, so many to a stop, the
            // colour kept, the step from one tone to the next taken over a pixel's width so that it has no stair. The sky and
            // whatever was drawn before this camera (far ground, the planet from space) are left as they are: a stepped sky is a
            // fault, not a style.

            static const float3 LUMA = float3(0.2126, 0.7152, 0.0722);
            static const float MOST = 16000.0;

            // The depth picture at a pixel. In the buildings where craft are made one camera draws the building and a second the
            // craft over it, and the second's depth picture has the craft in it and nothing of the building: but for an unseen floor
            // that is there to catch the craft's shadow, whose far edge then got a line straight across the hangar's doorway. There
            // the first camera's depth picture is handed over as well, and the nearer of the two is what is there.
            float deep(int2 at)
            {
                float d = TexelFetch(_CameraDepthTexture, at, 0).r;
                if (_AnimeHow.w > 0.5)
                {
                    float behind = TexelFetch(_AnimeBehind, at, 0).r;
                    d = _AnimeHow.x > 0.5 ? max(d, behind) : min(d, behind);
                }
                return d;
            }

            // One over the distance at a pixel, per metre (nothing drawn there: one over the far end of the camera's reach).
            float nearness(int2 at, int2 top)
            {
                return _ZBufferParams.z * deep(clamp(at, ((int2)(0)), top)) + _ZBufferParams.w;
            }

            float4 frag(v2f IN) : SV_Target
            {
                float4 result = float4(0.0, 0.0, 0.0, 0.0);
                int2 full = TextureSize(_CameraDepthTexture, 0);
                int2 top = full - 1;
                int2 at = clamp(((int2)(floor(IN.vs_TEXCOORD0 * ((float2)(full))))), ((int2)(0)), top);
                // (the picture may hold more than white, and now and then a value that is no number, or none that fits: a mean taken over
                // one of those is no number either, and what reads the picture after this pass can go black for good on a single such
                // pixel. So every value read is brought into what a picture can hold, and so is what is written.)
                float3 colour = clamp(SampleFlat(_AnimeScene, IN.vs_TEXCOORD0).rgb, ((float3)(0.0)), ((float3)(MOST)));

                float raw = deep(at);
                bool solid = _AnimeHow.x > 0.5 ? raw > 1e-7 : raw < 0.9999999;
                float here = _ZBufferParams.z * raw + _ZBufferParams.w;

                // the paint
                if (solid && _AnimeHow.z > 0.0)
                {
                    float3 even = ((float3)(0.0));
                    float total = 0.0;
                    float reach = _AnimeHow.z * 0.5;
                    { [loop] for (int q = 0; q < 4; q++)
                    {
                        float2 way = float2((q & 1) == 0 ? -1.0 : 1.0, (q & 2) == 0 ? -1.0 : 1.0);
                        float3 sum = ((float3)(0.0));
                        float one = 0.0, two = 0.0;
                        { [loop] for (int j = 0; j < 3; j++)
                        {
                            { [loop] for (int i = 0; i < 3; i++)
                            {
                                int2 off = ((int2)(floor(way * float2(float(i), float(j)) * reach + 0.5)));
                                float3 c = clamp(TexelFetch(_AnimeScene, clamp(at + off, ((int2)(0)), top), 0).rgb, ((float3)(0.0)), ((float3)(MOST)));
                                float l = dot(c, LUMA);
                                sum += c; one += l; two += l * l;
                            } }
                        } }
                        float mean = one / 9.0;
                        float uneven = max(two / 9.0 - mean * mean, 0.0) / (mean * mean + 1e-6);
                        float weight = 1.0 / (1e-4 + uneven * uneven * 400.0);
                        even += sum / 9.0 * weight;
                        total += weight;
                    } }
                    if (total > 0.0) colour = lerp(colour, even / total, _AnimePaint.y);
                }
                if (solid && _AnimePaint.x > 0.0 && _AnimeMore.x > 0.0)
                {
                    float light = max(dot(colour, LUMA), 1e-5);
                    float rung = log2(light) / _AnimePaint.x;
                    float soft = clamp(fwidth(rung), 0.02, 0.5);
                    // (leaning to the brighter tone: a cel's shade is a tone of its own, and most of a thing is in its light)
                    float edge = clamp(0.5 - _AnimeMore.y, soft, 1.0 - soft);
                    float snapped = floor(rung) + smoothstep(edge - soft, edge + soft, frac(rung));
                    colour = lerp(colour, colour * (exp2(snapped * _AnimePaint.x) / light), _AnimeMore.x);
                }
                if (solid)
                {
                    float grey = dot(colour, LUMA);
                    colour = max(lerp(((float3)(grey)), colour, _AnimePaint.z), ((float3)(0.0)));
                }

                // the ink
                float drawn = 0.0, bend = 0.0;
                if (_AnimeInk.y > 0.0)
                {
                    // (the least bend that counts: so much of the nearest value, and so many of the depth picture's steps)
                    float grain = abs(_ZBufferParams.z) * _AnimeHow.y * 5.96e-8;
                    int wide = int(max(1.0, floor(_AnimeInk.x + 0.5)));
                    float left = nearness(at - int2(wide, 0), top), right = nearness(at + int2(wide, 0), top);
                    float under = nearness(at - int2(0, wide), top), over = nearness(at + int2(0, wide), top);
                    float nearest = max(here, max(max(left, right), max(under, over)));
                    float bent = abs(left + right - 2.0 * here) + abs(under + over - 2.0 * here);
                    float least = max(_AnimeInk.z * float(wide) * nearest, grain);
                    drawn = smoothstep(least, 2.5 * least, bent);
                    bend = bent / max(nearest, 1e-12);
                    // (a second, finer look, so that a line is as wide as asked for and not two lines with a gap at a thin thing)
                    if (wide > 1)
                    {
                        float l1 = nearness(at - int2(1, 0), top), r1 = nearness(at + int2(1, 0), top);
                        float u1 = nearness(at - int2(0, 1), top), o1 = nearness(at + int2(0, 1), top);
                        float n1 = max(here, max(max(l1, r1), max(u1, o1)));
                        float b1 = abs(l1 + r1 - 2.0 * here) + abs(u1 + o1 - 2.0 * here);
                        float least1 = max(_AnimeInk.z * n1, grain);
                        drawn = max(drawn, smoothstep(least1, 2.5 * least1, b1));
                        bend = max(bend, b1 / max(n1, 1e-12));
                    }
                    float off = 1.0 / max(nearest, 1e-12);
                    drawn *= 1.0 - smoothstep(_AnimeMore.z * _AnimeInk.w, _AnimeInk.w, off);
                }
                // (ink is a darker tone of what it is drawn on, not black: a line over a pale thing stays a line and not a hole)
                colour *= 1.0 - _AnimeInk.y * drawn;

                if (_AnimePaint.w > 0.5)
                {
                    if (_AnimePaint.w < 1.5) colour = ((float3)(1.0 - drawn));
                    else if (_AnimePaint.w < 2.5) colour = float3(bend * 100.0, bend * 10.0, bend);
                    else colour = solid ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                }
                result = float4(clamp(colour, ((float3)(0.0)), ((float3)(MOST))), 1.0);
                return result;
            }
            ENDCG
        }
    }
}
