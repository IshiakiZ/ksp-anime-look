#ifdef VERTEX
#version 150
// Anime Look: ink lines and flat paint (Ink.cs). One triangle over the whole picture.
in vec3 in_POSITION0;
out vec2 vs_TEXCOORD0;
void main()
{
    gl_Position = vec4(in_POSITION0.xy, 0.5, 1.0);
    vs_TEXCOORD0 = in_POSITION0.xy * 0.5 + 0.5;
}
#endif
#ifdef FRAGMENT
#version 150
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
uniform vec4 _ZBufferParams;
uniform vec4 _AnimeInk;      // x: the lines' width, pixels; y: how dark they are (0 none, 1 black); z: how much bend makes a line; w: how far off they fade out, m
uniform vec4 _AnimePaint;    // x: a step of the ladder, in stops (0: none); y: how much of the smoothing into patches is laid on; z: how strong the colours (1: as they are); w: what to show (development: 1 the lines alone, 2 the bend, 3 solid or not)
uniform vec4 _AnimeHow;      // x: 1 if the depths run backwards (nearest is 1); y: how many of the depth picture's steps a bend must be to count; z: how far out a patch of the paint reaches, pixels (0: no flattening); w: 1 if there is a depth picture from the camera before this one
uniform vec4 _AnimeMore;     // x: how much of the stepping of brightness is laid on; y: how far the steps lean to the brighter tone (0 evenly, 0.5 always the brighter); z: from what part of their reach the lines begin to fade
uniform sampler2D _AnimeScene;          // the picture so far
uniform sampler2D _CameraDepthTexture;
uniform sampler2D _AnimeBehind;         // the depth picture of the camera before this one (see deep)
in vec2 vs_TEXCOORD0;
out vec4 SV_Target0;

const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);
const float MOST = 16000.0;

// The depth picture at a pixel. In the buildings where craft are made one camera draws the building and a second the
// craft over it, and the second's depth picture has the craft in it and nothing of the building: but for an unseen floor
// that is there to catch the craft's shadow, whose far edge then got a line straight across the hangar's doorway. There
// the first camera's depth picture is handed over as well, and the nearer of the two is what is there.
float deep(ivec2 at)
{
    float d = texelFetch(_CameraDepthTexture, at, 0).r;
    if (_AnimeHow.w > 0.5)
    {
        float behind = texelFetch(_AnimeBehind, at, 0).r;
        d = _AnimeHow.x > 0.5 ? max(d, behind) : min(d, behind);
    }
    return d;
}

// One over the distance at a pixel, per metre (nothing drawn there: one over the far end of the camera's reach).
float nearness(ivec2 at, ivec2 top)
{
    return _ZBufferParams.z * deep(clamp(at, ivec2(0), top)) + _ZBufferParams.w;
}

void main()
{
    ivec2 full = textureSize(_CameraDepthTexture, 0);
    ivec2 top = full - 1;
    ivec2 at = clamp(ivec2(floor(vs_TEXCOORD0 * vec2(full))), ivec2(0), top);
    // (the picture may hold more than white, and now and then a value that is no number, or none that fits: a mean taken over
    // one of those is no number either, and what reads the picture after this pass can go black for good on a single such
    // pixel. So every value read is brought into what a picture can hold, and so is what is written.)
    vec3 colour = clamp(texture(_AnimeScene, vs_TEXCOORD0).rgb, vec3(0.0), vec3(MOST));

    float raw = deep(at);
    bool solid = _AnimeHow.x > 0.5 ? raw > 1e-7 : raw < 0.9999999;
    float here = _ZBufferParams.z * raw + _ZBufferParams.w;

    // the paint
    if (solid && _AnimeHow.z > 0.0)
    {
        vec3 even = vec3(0.0);
        float total = 0.0;
        float reach = _AnimeHow.z * 0.5;
        for (int q = 0; q < 4; q++)
        {
            vec2 way = vec2((q & 1) == 0 ? -1.0 : 1.0, (q & 2) == 0 ? -1.0 : 1.0);
            vec3 sum = vec3(0.0);
            float one = 0.0, two = 0.0;
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    ivec2 off = ivec2(floor(way * vec2(float(i), float(j)) * reach + 0.5));
                    vec3 c = clamp(texelFetch(_AnimeScene, clamp(at + off, ivec2(0), top), 0).rgb, vec3(0.0), vec3(MOST));
                    float l = dot(c, LUMA);
                    sum += c; one += l; two += l * l;
                }
            }
            float mean = one / 9.0;
            float uneven = max(two / 9.0 - mean * mean, 0.0) / (mean * mean + 1e-6);
            float weight = 1.0 / (1e-4 + uneven * uneven * 400.0);
            even += sum / 9.0 * weight;
            total += weight;
        }
        if (total > 0.0) colour = mix(colour, even / total, _AnimePaint.y);
    }
    if (solid && _AnimePaint.x > 0.0 && _AnimeMore.x > 0.0)
    {
        float light = max(dot(colour, LUMA), 1e-5);
        float rung = log2(light) / _AnimePaint.x;
        float soft = clamp(fwidth(rung), 0.02, 0.5);
        // (leaning to the brighter tone: a cel's shade is a tone of its own, and most of a thing is in its light)
        float edge = clamp(0.5 - _AnimeMore.y, soft, 1.0 - soft);
        float snapped = floor(rung) + smoothstep(edge - soft, edge + soft, fract(rung));
        colour = mix(colour, colour * (exp2(snapped * _AnimePaint.x) / light), _AnimeMore.x);
    }
    if (solid)
    {
        float grey = dot(colour, LUMA);
        colour = max(mix(vec3(grey), colour, _AnimePaint.z), vec3(0.0));
    }

    // the ink
    float drawn = 0.0, bend = 0.0;
    if (_AnimeInk.y > 0.0)
    {
        // (the least bend that counts: so much of the nearest value, and so many of the depth picture's steps)
        float grain = abs(_ZBufferParams.z) * _AnimeHow.y * 5.96e-8;
        int wide = int(max(1.0, floor(_AnimeInk.x + 0.5)));
        float left = nearness(at - ivec2(wide, 0), top), right = nearness(at + ivec2(wide, 0), top);
        float under = nearness(at - ivec2(0, wide), top), over = nearness(at + ivec2(0, wide), top);
        float nearest = max(here, max(max(left, right), max(under, over)));
        float bent = abs(left + right - 2.0 * here) + abs(under + over - 2.0 * here);
        float least = max(_AnimeInk.z * float(wide) * nearest, grain);
        drawn = smoothstep(least, 2.5 * least, bent);
        bend = bent / max(nearest, 1e-12);
        // (a second, finer look, so that a line is as wide as asked for and not two lines with a gap at a thin thing)
        if (wide > 1)
        {
            float l1 = nearness(at - ivec2(1, 0), top), r1 = nearness(at + ivec2(1, 0), top);
            float u1 = nearness(at - ivec2(0, 1), top), o1 = nearness(at + ivec2(0, 1), top);
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
        if (_AnimePaint.w < 1.5) colour = vec3(1.0 - drawn);
        else if (_AnimePaint.w < 2.5) colour = vec3(bend * 100.0, bend * 10.0, bend);
        else colour = solid ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    }
    SV_Target0 = vec4(clamp(colour, vec3(0.0), vec3(MOST)), 1.0);
}
#endif
