#ifndef MACARON_CUSTOM_INSERT_INCLUDED
#define MACARON_CUSTOM_INSERT_INCLUDED
// Macaron
//
// Screen-space occlusion (SSAO / Ray March) and a contact shadow toward a fixed light direction for lilToon,
// applied right before fog (Macaron_Apply). The Light Adjuster (Macaron_Adjust) changes lilToon's light color,
// the base color saturation and the main light direction from the avatar menu.
//
// VR: every screen position is converted to an eye-local pixel first, so double-wide Single Pass Stereo and
// Single Pass Instanced depth textures are both read at the right place.

#include "mcr_world_capture_match.hlsl"

// Main texture UV (tiling / offset, scroll and rotation), the space the Mask Generator and the capture pass use.
float2 Macaron_MainUV(float2 uv0)
{
#if defined(LIL_FEATURE_ANIMATE_MAIN_UV) || defined(LIL_LITE)
    return lilCalcUV(uv0, _MainTex_ST, _MainTex_ScrollRotate);
#else
    return uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
#endif
}

// Caster mask, independent of the receiver / effect masks. It clips the shadow / depth passes and the dedicated
// orthographic world capture, never the normal color pass. uv = main texture UV (Macaron_MainUV).
void Macaron_CasterMask(float2 uv)
{
    bool capture = Macaron_IsWorldCapture();
#if defined(LIL_PASS_SHADOWCASTER) || defined(LIL_PASS_DEPTHONLY) || defined(LIL_PASS_DEPTHNORMALS)
    capture = true;
#endif
    if(capture && _MCR_CasterMaskAmount > 0.0)
    {
        float mask = LIL_SAMPLE_2D(_MCR_CasterMask, lil_sampler_linear_repeat,
            uv * _MCR_CasterMask_ST.xy + _MCR_CasterMask_ST.zw).r;
        clip(lerp(1.0, mask, saturate(_MCR_CasterMaskAmount)) - 0.5);
    }
}
// Outline materials and the camera depth texture
//
// An outline material has a single ShadowCaster pass, the outline one, which pushes the whole mesh out by the outline
// width. BRP renders the camera depth texture with it, so SSAO / GTAO / Ray March and the contact shadow would see a
// shell a few millimeters in front of the real surface (dark rims along every silhouette). For the camera depth texture
// only, the mesh is written at its real position (the shell is undone); light shadow maps keep lilToon's outline.
//   The camera depth render is the only ShadowCaster render whose view sits at the camera (light shadow maps look from
//   the light). The tolerance covers the eye offset in VR.
#if defined(LIL_OUTLINE) && defined(LIL_PASS_SHADOWCASTER)
static float4 mcrMeshPositionOS = 0.0;

bool Macaron_IsCameraDepthView()
{
    float3x3 rot = (float3x3)LIL_MATRIX_V;
    float3 viewPos = -mul(transpose(rot), LIL_MATRIX_V._m03_m13_m23);
    float3 d = viewPos - _WorldSpaceCameraPos;
    return abs(LIL_MATRIX_P._m33) < 0.5 && dot(d, d) < 0.01;
}

// Before lilToon's outline offset: remember the real position. After it: put it back for the camera depth texture,
// in the input too, since the shadow caster clip position is built from it.
#define LIL_CUSTOM_VERTEX_OS mcrMeshPositionOS = positionOS;
#define LIL_CUSTOM_VERTEX_WS if(Macaron_IsCameraDepthView()) { input.positionOS = mcrMeshPositionOS; vertexInput = lilGetVertexPositionInputs(mcrMeshPositionOS); }
#endif

// Uzumore (see Macaron_UzumoreOS below). Compiled into the color passes only: not the whole Fur SubShader (the shells
// would stick out of a pushed surface), FakeShadow, Meta, nor the ShadowCaster / depth passes. The ShadowCaster vertex
// program with Uzumore crashed Unity's d3d11 shader compiler (UnityShaderCompiler.exe "disappeared"), so the shadow
// maps and the camera depth texture keep the real mesh; Macaron_UzumoreFS fades Macaron's occlusion out close to the
// camera, where that depth texture does not match the pushed surface.
#if !defined(LIL_FUR) && !defined(LIL_FAKESHADOW) && !defined(LIL_PASS_META) && !defined(LIL_PASS_MOTIONVECTORS) \
    && !defined(LIL_PASS_SHADOWCASTER) && !defined(LIL_PASS_DEPTHONLY) && !defined(LIL_PASS_DEPTHNORMALS)
    #define MCR_UZU_COMPILED 1
    #define LIL_CUSTOM_VERTEX_OS Macaron_UzumoreOS(input.normalOS, uvMain, positionOS);
#else
    #define MCR_UZU_COMPILED 0
#endif

// Black pixel light (the lilMCR PCSS light, or any light whose color is exactly 0) in lilToon's ForwardAdd pass
//
// The pass would add exactly nothing, but still rasterizes and shades every pixel.
// The mesh is collapsed to one point in the vertex shader, so no pixel is drawn. Draw call, vertex work and the light's
// shadow map stay. Only done where skipping the draw provably leaves the frame unchanged:
//   - the light color is exactly 0 (lilToon's ForwardAdd output is then black),
//   - color blending keeps the target for a black source: destination factor One, BlendOp Add / RevSub / Max
//     (lilToon's ForwardAdd alpha blending is fixed to "Zero One"),
//   - the stencil is off, or every stencil op is Keep / Zero / Replace (the base pass already wrote the same result),
//   - a plain ForwardAdd pass: outline, one-pass outline, fur, tessellation, gem, refraction and fake shadow are left as
//     they are. lilMCR PCSS's own multiply pass is a separate pass and is not affected.
#if defined(LIL_PASS_FORWARDADD) && !defined(LIL_OUTLINE) && !defined(LIL_ONEPASS_OUTLINE) && !defined(LIL_FUR) && !defined(LIL_TESSELLATION) && !defined(LIL_GEM) && !defined(LIL_REFRACTION) && !defined(LIL_FAKESHADOW)
bool Macaron_SkipBlackForwardAdd()
{
    if(any(_LightColor0.rgb != 0.0)) return false;
    int dst = (int)round(_DstBlendFA);
    int op = (int)round(_BlendOpFA);
    if(dst != 1 || !(op == 0 || op == 2 || op == 4)) return false;
    if(_StencilComp < 0.5) return true;
    return _StencilPass < 2.5 && _StencilFail < 2.5 && _StencilZFail < 2.5;
}
#define LIL_CUSTOM_VERTEX_WS if(Macaron_SkipBlackForwardAdd()) { vertexInput.positionWS = LIL_MATRIX_M._m03_m13_m23; }
#endif

#if defined(LIL_PASS_FORWARD) || defined(LIL_PASS_FORWARDADD) || defined(LIL_PASS_SHADOWCASTER) || defined(LIL_PASS_DEPTHONLY) || defined(LIL_PASS_DEPTHNORMALS)
#undef BEFORE_UNPACK_V2F
// The UV member differs per pass (uv01 packed / uv0), and LIL_FUR covers the whole Fur SubShader, including its normal
// FORWARD pass whose v2f has uv01. LIL_UNPACK_TEXCOORD0 is expanded inside each frag, after lilToon has chosen the
// right member for that pass, so one definition works everywhere.
#define BEFORE_UNPACK_V2F LIL_UNPACK_TEXCOORD0(input,fd); Macaron_CasterMask(Macaron_MainUV(fd.uv0));
#endif

// Set only by the fur shell pass (lil_pass_forward_fur.hlsl): in the forward passes BEFORE_FUR exists only there.
// The world capture records the body mesh, never the fur shells.
static bool mcrFurShell = false;
#undef BEFORE_FUR
#define BEFORE_FUR mcrFurShell = true;

// Return immediately after fog, before any standard output conversion. The RT
// is linear ARGBFloat; G identifies the owner and B records capture time.
// Preserve cutout/transparent silhouette, but do not blend data as visual color.
#if defined(LIL_PASS_FORWARD) || defined(LIL_PASS_FORWARDADD)
#undef BEFORE_OUTPUT
#if defined(LIL_PASS_FORWARDADD) || defined(LIL_OUTLINE)
    #define BEFORE_OUTPUT if(Macaron_IsWorldCapture()) { clip(-1); }
#else
    #define BEFORE_OUTPUT if(Macaron_IsWorldCapture()) { clip(mcrFurShell ? -1.0 : fd.col.a - 0.5); float mcrDepth = (-mul(LIL_MATRIX_V, float4(fd.positionWS,1)).z - _ProjectionParams.y) / (_ProjectionParams.z - _ProjectionParams.y); return float4(mcrDepth, -_MCR_WorldCapture.w, -1 - _Time.y, 1); }
#endif
#endif

#define MCR_PI 3.14159265359
#define MCR_TWO_PI 6.28318530718
#define MCR_GOLDEN_RATIO 0.61803398875
#define MCR_EPS 0.000001
#define MCR_TINY 0.0001

// SSAO "px" parameters are sized for a 1080 px tall, 60 deg view at 1.5 m.
#define MCR_REF_FOCAL_PX 935.3
#define MCR_REF_DEPTH 1.5
#define MCR_MAX_RADIUS_PX 128.0

// Per-pixel random value 0..1. The integer pixel coordinate and a per-use seed are folded into one word and
// scrambled with a PCG output permutation (one LCG step + xorshift-multiply), so every feature that needs its
// own random stream just passes a different seed instead of offsetting the coordinate.
float MCR_PixelRandom(float2 pixel, uint seed)
{
    uint2 q = (uint2)(int2)floor(pixel);
    uint v = q.x + q.y * 0x9E3779B1u + seed;
    v = v * 747796405u + 2891336453u;
    v = ((v >> ((v >> 28u) + 4u)) ^ v) * 277803737u;
    v ^= v >> 22u;
    return (float)(v >> 8u) * (1.0 / 16777216.0);
}

// Interleaved gradient noise (Jimenez 2014)
float MCR_IGN(float2 coord)
{
    return frac(52.9829189 * frac(dot(coord, float2(0.06711056, 0.00583715))));
}

// Orthonormal basis around a unit vector (branchless form of Duff et al. 2017).
void MCR_OrthoBasis(float3 n, out float3 t, out float3 b)
{
    float sgn = n.z >= 0.0 ? 1.0 : -1.0;
    float a = -1.0 / (sgn + n.z);
    float c = n.x * n.y * a;
    t = float3(1.0 + sgn * n.x * n.x * a, sgn * c, -sgn * n.x);
    b = float3(c, sgn + n.y * n.y * a, -n.y);
}

//------------------------------------------------------------------------------------------------------------------------------
// Eye-local screen space and depth texture access
//
// Every screen position below is an EYE-LOCAL pixel (0..eye width, 0..eye height) in SV_Position orientation.

struct MCRDepthInfo
{
    float2 screenSize;  // pixels of one eye (_ScreenParams)
    float2 depthSize;   // pixels of _CameraDepthTexture
    float4 eyeST;       // rectangle of this eye in a double-wide target (scale xy, offset zw)
    bool sideBySide;    // the depth texture stores both eyes next to each other
    float focalPx;      // pixels per meter at 1 m
    bool present;
};

MCRDepthInfo MCR_GetDepthInfo()
{
    MCRDepthInfo di;
    di.screenSize = max(LIL_SCREENPARAMS.xy, float2(1.0, 1.0));
    di.eyeST = float4(1.0, 1.0, 0.0, 0.0);
    di.sideBySide = false;
    #if defined(UNITY_SINGLE_PASS_STEREO)
        di.eyeST = unity_StereoScaleOffset[unity_StereoEyeIndex];
    #endif
    #if defined(LIL_HDRP)
        di.depthSize = di.screenSize;
        di.present = LIL_ENABLED_DEPTH_TEX;
    #else
        // Texture2DArray (one slice per eye) in Single Pass Instanced, Texture2D otherwise.
        di.depthSize = max(lilGetWidthAndHeight(_CameraDepthTexture), float2(1.0, 1.0));
        di.present = min(di.depthSize.x, di.depthSize.y) > 16.0;
        #if defined(UNITY_SINGLE_PASS_STEREO)
            // Double-wide target: a depth texture about twice as wide as one eye holds both eyes.
            di.sideBySide = di.depthSize.x > di.screenSize.x * 1.5;
        #endif
    #endif
    di.focalPx = 0.5 * di.screenSize.y * abs(LIL_MATRIX_P._m11);
    return di;
}

// SV_Position.xy -> eye-local pixel.
float2 MCR_EyePixel(float2 positionCS, MCRDepthInfo di)
{
    #if defined(UNITY_SINGLE_PASS_STEREO)
        // SV_Position addresses the double-wide target (two eye widths).
        float2 targetUV = positionCS / (di.screenSize * float2(2.0, 1.0));
        return (targetUV - di.eyeST.zw) / max(di.eyeST.xy, float2(MCR_TINY, MCR_TINY)) * di.screenSize;
    #else
        return positionCS;
    #endif
}

// Raw value of an empty depth texel (far plane).
float MCR_EmptyRawDepth()
{
    #if UNITY_REVERSED_Z
        return 0.0;
    #else
        return 1.0;
    #endif
}

#if !defined(LIL_HDRP)
// The BRP depth texture is stored upside down relative to SV_Position on some setups.
bool MCR_DepthFlipY()
{
#if defined(LIL_BRP)
#if UNITY_UV_STARTS_AT_TOP
    return _ProjectionParams.x > 0;
#else
    return _ProjectionParams.x < 0;
#endif
#else
    return false;
#endif
}

// Shared address calculation for cached and uncached depth reads.
float2 MCR_DepthTexel(float2 uv, MCRDepthInfo di)
{
#if defined(UNITY_SINGLE_PASS_STEREO)
    if(di.sideBySide) uv = uv * di.eyeST.xy + di.eyeST.zw;
#endif
    if(MCR_DepthFlipY()) uv.y = 1.0 - uv.y;
    return clamp(floor(uv * di.depthSize), float2(0.0,0.0), di.depthSize - 1.0);
}
#endif

// Eye-local pixel of the CENTER of the depth texel that holds eyePx (inverse of MCR_DepthTexel).
// The depth texture stores the surface depth at its texel centers. A point rebuilt from that depth must use the texel
// center too: rebuilt at an off-center position, a surface seen edge-on (the side of a face) gets a depth error of up to
// half a texel x its depth slope, which reads as small bumps above the surface in a pixel / polygon grid pattern.
#if !defined(LIL_HDRP)
// Eye-local pixel of the center of a known depth texel (the texel comes from MCR_DepthTexel).
float2 MCR_DepthTexelToEyeCenter(float2 texel, MCRDepthInfo di)
{
    float2 c = (texel + 0.5) / di.depthSize;
    if(MCR_DepthFlipY()) c.y = 1.0 - c.y;
#if defined(UNITY_SINGLE_PASS_STEREO)
    if(di.sideBySide) c = (c - di.eyeST.zw) / max(di.eyeST.xy, float2(MCR_TINY, MCR_TINY));
#endif
    return c * di.screenSize;
}
#endif

float2 MCR_DepthTexelCenter(float2 eyePx, MCRDepthInfo di)
{
#if defined(LIL_HDRP)
    return floor(eyePx) + 0.5;
#else
    return MCR_DepthTexelToEyeCenter(MCR_DepthTexel(eyePx / di.screenSize, di), di);
#endif
}

// Raw depth texture value at an eye-local pixel (empty outside the eye).
float MCR_RawDepthAt(float2 eyePx, MCRDepthInfo di)
{
    float2 uv = eyePx / di.screenSize;
    if(any(uv < 0.0) || any(uv >= 1.0)) return MCR_EmptyRawDepth();
#if defined(LIL_HDRP)
    return LIL_GET_DEPTH_TEX_CS(eyePx);
#else
    return LIL_SAMPLE_SCREEN_CS(_CameraDepthTexture, MCR_DepthTexel(uv,di)).r;
#endif
}

struct MCRDepthCache
{
    float2 texel;
    float raw;
    bool valid;
};

// One-entry cache, reset for every ray. Cache only the stored value: oblique
// projection linearization may depend on the continuous sample position.
float MCR_ReadDepthCached(float2 eyePx, MCRDepthInfo di, inout MCRDepthCache cache)
{
    float raw;
#if defined(LIL_HDRP)
    // HDRP uses its own depth addressing.
    raw = MCR_RawDepthAt(eyePx,di);
#else
    float2 uv = eyePx / di.screenSize;
    if(any(uv < 0.0) || any(uv >= 1.0)) return 1e6;
    float2 texel = MCR_DepthTexel(uv,di);
    [branch]
    if(!cache.valid || any(texel != cache.texel))
    {
        cache.raw = LIL_SAMPLE_SCREEN_CS(_CameraDepthTexture,texel).r;
        cache.texel = texel;
        cache.valid = true;
    }
    raw = cache.raw;
#endif
    if(abs(raw - MCR_EmptyRawDepth()) < 0.000001) return 1e6;
    return LIL_TO_LINEARDEPTH(raw, eyePx);
}

#if !defined(LIL_HDRP)
// MCR_ReadDepthCached(eyePx) with the texel already known, so its address is not computed again. texel must be
// MCR_DepthTexel(eyePx / screenSize) of this eyePx, or eyePx the snapped center MCR_DepthTexelToEyeCenter(texel) (a
// texel center maps back to the same texel, half a texel from every edge). Either way the result is the same as
// MCR_ReadDepthCached(eyePx). The eye range guard is kept as is: with a double-wide depth texture whose eye boundary does
// not fall on a texel edge, the center of an edge texel can lie outside the eye, where MCR_ReadDepthCached returns
// "nothing drawn". The linearization uses eyePx. Keep the cache / linearization in sync with MCR_ReadDepthCached.
float MCR_ReadDepthTexelCached(float2 texel, float2 eyePx, MCRDepthInfo di, inout MCRDepthCache cache)
{
    float2 uv = eyePx / di.screenSize;
    if(any(uv < 0.0) || any(uv >= 1.0)) return 1e6;
    [branch]
    if(!cache.valid || any(texel != cache.texel))
    {
        cache.raw = LIL_SAMPLE_SCREEN_CS(_CameraDepthTexture,texel).r;
        cache.texel = texel;
        cache.valid = true;
    }
    float raw = cache.raw;
    if(abs(raw - MCR_EmptyRawDepth()) < 0.000001) return 1e6;
    return LIL_TO_LINEARDEPTH(raw, eyePx);
}
#endif

// Linear view depth of the depth texture at an eye-local pixel (1e6 where nothing was drawn).
// Uses lilToon's linearization (also correct for the oblique projection of VRChat mirrors).
float MCR_ReadDepth(float2 eyePx, MCRDepthInfo di)
{
    float raw = MCR_RawDepthAt(eyePx, di);
    if(abs(raw - MCR_EmptyRawDepth()) < 0.000001) return 1e6;
    return LIL_TO_LINEARDEPTH(raw, eyePx);
}

bool MCR_InsideEye(float2 eyePx, MCRDepthInfo di)
{
    return all(eyePx >= 0.0) && all(eyePx <= di.screenSize);
}

// "px" parameter -> pixels on the current eye. Keeps its world size at any distance, FOV and resolution.
float MCR_ProjectPx(float px, float viewDepth, MCRDepthInfo di)
{
    float worldSize = px * (MCR_REF_DEPTH / MCR_REF_FOCAL_PX);
    return worldSize * di.focalPx / max(viewDepth, 0.05);
}

// Eye-local pixel <-> NDC xy (y follows the platform's SV_Position orientation).
float2 MCR_PixelToNDC(float2 eyePx, MCRDepthInfo di)
{
    float2 ndc = eyePx / di.screenSize * 2.0 - 1.0;
    #if UNITY_UV_STARTS_AT_TOP
        ndc.y = -ndc.y;
    #endif
    return ndc;
}

float2 MCR_NDCToPixel(float2 ndc, MCRDepthInfo di)
{
    #if UNITY_UV_STARTS_AT_TOP
        ndc.y = -ndc.y;
    #endif
    return (ndc * 0.5 + 0.5) * di.screenSize;
}

// View-space point at z = -1 seen through an eye-local pixel (inverts the projection's x / y rows, so the
// off-center VR frustum and flipped projections are handled).
float3 MCR_ViewRay(float2 eyePx, MCRDepthInfo di)
{
    float2 ndc = MCR_PixelToNDC(eyePx, di);
    return float3((ndc.x + LIL_MATRIX_P._m02) / LIL_MATRIX_P._m00, (ndc.y + LIL_MATRIX_P._m12) / LIL_MATRIX_P._m11, -1.0);
}

// Clip position -> eye-local pixel.
float2 MCR_ClipToEyePixel(float4 clip, MCRDepthInfo di)
{
    return MCR_NDCToPixel(clip.xy / max(clip.w, MCR_TINY), di);
}

// View-space position -> eye-local pixel.
float2 MCR_ViewToEyePixel(float3 positionVS, MCRDepthInfo di)
{
    return MCR_ClipToEyePixel(mul(LIL_MATRIX_P, float4(positionVS, 1.0)), di);
}

// A point marched along a straight view-space ray is linear in clip space: project the start and the direction once,
// then each step is one multiply-add instead of a matrix multiply. clip.w = view depth of the point.
struct MCRClipRay
{
    float4 origin;
    float4 dir;
};

MCRClipRay MCR_MakeClipRay(float3 originVS, float3 dirVS)
{
    MCRClipRay r;
    r.origin = mul(LIL_MATRIX_P, float4(originVS, 1.0));
    r.dir = mul(LIL_MATRIX_P, float4(dirVS, 0.0));
    return r;
}

//------------------------------------------------------------------------------------------------------------------------------
// Smooth surface (tangent plane of the interpolated vertex normal)

struct MCRSurface
{
    float2 centerCS;    // eye-local pixel
    float depth;        // linear depth of this fragment (same scale as MCR_ReadDepth)
    float viewDepth;    // positionCS.w
    float3 positionVS;  // this fragment in view space
    float3 nView;       // smooth normal in view space, facing the camera
    float3 nFacet;      // normal of the triangle itself in view space (the surface the depth texture holds), facing the camera
    float smoothing;    // 0..1
};

MCRSurface MCR_GetSurface(lilFragData fd, MCRDepthInfo di)
{
    MCRSurface s;
    s.centerCS = MCR_EyePixel(fd.positionCS.xy, di);
    s.depth = max(LIL_TO_LINEARDEPTH(fd.positionCS.z, s.centerCS), MCR_EPS);
    s.viewDepth = max(fd.positionCS.w, MCR_TINY);   // SV_Position.w = view depth
    float3 centerRay = MCR_ViewRay(s.centerCS, di);
    s.positionVS = centerRay * s.depth;

    // Interpolated vertex normal, not the normal-mapped one: smooth over polygons. The Lite pass has no origN.
    float3 nWS = dot(fd.origN, fd.origN) > 0.25 ? fd.origN : fd.N;
    float3 n = mul((float3x3)LIL_MATRIX_V, nWS);
    n *= rsqrt(max(dot(n, n), MCR_EPS));
    // Back faces / double-sided cloth: use the side that faces the camera.
    s.nView = dot(n, centerRay) > 0.0 ? -n : n;
    s.nFacet = s.nView;     // replaced by MCR_SetFacetNormal when the triangle normal is known
    s.smoothing = saturate(_MCR_NormalSmooth);
    return s;
}

// The vertex normal can differ a lot from the triangle it is on: on strongly curved low-poly spots (the tip of the
// nose) and on faces with edited normals (flat anime shading). facetWS = cross of the screen derivatives of the
// world position, i.e. the plane of the triangle. Degenerate derivatives keep the smooth normal.
void MCR_SetFacetNormal(inout MCRSurface s, float3 facetWS)
{
    float3 n = mul((float3x3)LIL_MATRIX_V, facetWS);
    float len2 = dot(n, n);
    if(len2 < 1e-20) return;
    n *= rsqrt(len2);
    s.nFacet = dot(n, s.positionVS) > 0.0 ? -n : n;
}

// Self-occlusion filter shared by Ray March / Contact Shadow: a depth texture point only occludes when it rises
// above the smooth tangent plane by more than the polygon / curvature error, which grows with its distance.
float MCR_AboveSurface(float3 pointVS, MCRSurface s, float baseTolerance)
{
    float3 toPoint = pointVS - s.positionVS;
    float height = dot(toPoint, s.nView);
    float above = 0.0;
    [branch]
    if(height > baseTolerance)
    {
        float tolerance = baseTolerance + length(toPoint) * 0.08;
        above = smoothstep(tolerance, tolerance * 2.0, height);
    }
    return lerp(1.0, above, s.smoothing);
}

//------------------------------------------------------------------------------------------------------------------------------
// Common

// Visibility left after the Fade Distance (x = where the fade begins, y = where the effect is gone, meters).
// A range that is empty or reversed means "never fade". The falloff (1-t)^2 (1+2t) is the mirrored Hermite
// curve, written in factored form: 2 multiplies and 2 multiply-adds, exactly 0 at the far end.
float MCR_FadeOut(float depth)
{
    float2 range = max(_MCR_FadeDistance.xy, 0.0);
    float span = range.y - range.x;
    if(!(span > MCR_EPS)) return 1.0;
    float t = saturate((depth - range.x) / span);
    float u = 1.0 - t;
    return u * u * (1.0 + t + t);
}

// Weight for an effect whose on-screen size is given in pixels: below half a pixel it can only shimmer, so it is
// faded out, fully kept from 1.5 px up.
float MCR_SubPixelFade(float sizeOnScreenPx)
{
    return smoothstep(0.5, 1.5, sizeOnScreenPx);
}

//------------------------------------------------------------------------------------------------------------------------------
// Shared occlusion controls

// Stored setting: 0 Light / 1 Standard / 2 Fine / 3 Best / 4 Standard without the 2x2 quad share.
int MCR_OccQualitySetting()
{
    return (int)clamp(round(_MCR_OccQuality), 0.0, 4.0);
}

// Sample budget level 0..3: Standard without the quad share uses the Standard budget.
// Mirrors: the avatar is drawn once more per eye there, so the mirror image uses the Light budget and the Low contact
// shadow. LIL_IS_MIRROR is uniform per draw.
bool MCR_MirrorLowest()
{
    return LIL_IS_MIRROR;
}

// VR: stereo rendering shades two high-resolution eyes, so one quality level is dropped there (occlusion down to Light,
// contact shadow down to Low). Compile time: the desktop variants are unchanged, and so are non-stereo cameras in VR
// (handheld / stream camera).
#if defined(USING_STEREO_MATRICES)
    #define MCR_VR_LEVELS 1
#else
    #define MCR_VR_LEVELS 0
#endif

// VR only: very close to the camera the whole effect fades out, gone at START and full from END (m). A surface that
// close fills most of both eyes, where every pixel would pay the full cost. Desktop and non-stereo cameras: no fade.
#define MCR_NEAR_FADE_START 0.12
#define MCR_NEAR_FADE_END 0.20
float MCR_NearFade(float viewDepth)
{
#if defined(USING_STEREO_MATRICES)
    return smoothstep(MCR_NEAR_FADE_START, MCR_NEAR_FADE_END, viewDepth);
#else
    return 1.0;
#endif
}

int MCR_OccQualityLevel()
{
    if(MCR_MirrorLowest()) return 0;
    int q = MCR_OccQualitySetting();
    return max((q == 4 ? 1 : q) - MCR_VR_LEVELS, 0);
}

// Adaptive Screen Quality of the occlusion (material toggle, uniform per draw).
bool MCR_OccAdaptive()
{
    return _MCR_OccScreenLOD > 0.5;
}

// Soft cap of an on-screen size: px / (1 + (px / cap)^4)^(1/4), returned as a scale factor.
// ~1 below half the cap, 0.84 at the cap, the result never exceeds the cap.
float MCR_SoftCapScale(float px, float capPx)
{
    float r = px / max(capPx, MCR_EPS);
    float r2 = r * r;
    return rsqrt(sqrt(1.0 + r2 * r2));
}

//------------------------------------------------------------------------------------------------------------------------------
// 2x2 quad share (Ray March, by Quality: Standard = each pixel 1/2 of the rays, Light = 1/4,
// Standard (no 2x2) / Fine / Best = off)
//
// The 4 pixels of a 2x2 quad each march a quarter of the directions and add their sums together through fine
// screen derivatives. Needs fine derivatives (ddx_fine / ddy_fine, Shader Model 5): lilCustomShaderDatas.lilblock
// raises lilToon's "#pragma target 3.5" to 4.5 on Direct3D 11 only. Other APIs / targets keep the normal path.
//
// A pixel only shares when all 3 neighbors are valid partners (MCR_QuadPartner): they march, are really visible
// (depth texture agrees, which also rejects helper lanes outside the triangle), sit at a close depth, face a similar
// way (no mixing across creases) and use the same set size. Otherwise it marches the whole set itself (fallback).
//
// Both exchanges must run in uniform control flow: Macaron_Apply has no per-pixel return before them in this build,
// and they are not inside any per-pixel branch.
#if defined(SHADER_API_D3D11) && (SHADER_TARGET >= 45)
    #define MCR_QUAD_SHARE 1
#else
    #define MCR_QUAD_SHARE 0
#endif

// Partner test thresholds: depth difference (m) = base + relative x depth, minimum cosine between the normals.
#define MCR_QUAD_DEPTH_BASE 0.002
#define MCR_QUAD_DEPTH_REL 0.01
#define MCR_QUAD_NORMAL_COS 0.8
// "Really visible" test of the pixel itself (opaque / cutout): depth texture vs own depth.
#define MCR_QUAD_VISIBLE_BASE 0.002
#define MCR_QUAD_VISIBLE_REL 0.01

// Position inside the 2x2 quad from SV_Position (quads start at even pixels): 0..3 and the derivative signs.
int MCR_QuadIndex(float2 positionCS, out float2 sgn)
{
    uint2 p = (uint2)positionCS & 1u;
    sgn = float2(p.x != 0u ? -1.0 : 1.0, p.y != 0u ? -1.0 : 1.0);
    return (int)(p.x + p.y * 2u);
}

#if MCR_QUAD_SHARE
// Values of the horizontal, vertical and diagonal neighbor.
void MCR_QuadExchange(float4 v, float2 sgn, out float4 nx, out float4 ny, out float4 nd)
{
    nx = v + ddx_fine(v) * sgn.x;
    ny = v + ddy_fine(v) * sgn.y;
    nd = nx + ddy_fine(nx) * sgn.y;     // the vertical neighbor's horizontal neighbor
}

// geo = (view normal xyz, depth or -1 when the pixel is not a partner), size = ray set size.
// Symmetric for both pixels (the tolerance uses the smaller depth), so two pixels agree on being partners.
// NaN from a helper lane fails every compare.
bool MCR_QuadPartner(float4 geo, float size, float4 meGeo, float meSize)
{
    float tolerance = MCR_QUAD_DEPTH_BASE + MCR_QUAD_DEPTH_REL * min(geo.w, meGeo.w);
    return geo.w > 0.0 && abs(geo.w - meGeo.w) < tolerance
        && dot(geo.xyz, meGeo.xyz) > MCR_QUAD_NORMAL_COS
        && abs(size - meSize) < 0.5;
}
#endif

// Per-pixel noise: x = rotation, y = step offset (-0.5..0.5).
float2 MCR_OccNoiseValues(float2 centerCS)
{
    float amount = saturate(_MCR_OccNoise);
    float n = MCR_IGN(centerCS);
    return float2(n, frac(n * 1.6180339887 + 0.37) - 0.5) * amount;
}

// Occlusion Contrast (-1..1, 0 = unchanged). Below 0 a rational bias curve brings up faint occlusion; above 0
// a gain curve around 0.5 makes faint occlusion fainter and strong occlusion stronger.
float MCR_OccContrastMap(float x)
{
    x = saturate(x);
    float c = clamp(_MCR_OccContrast, -1.0, 1.0);
    if(c == 0.0 || x == 0.0 || x == 1.0) return x;
    if(c < 0.0)
    {
        float k = exp2(c * 2.0);                     // 1 .. 0.25
        return x / max(x + (1.0 - x) * k, MCR_EPS);
    }
    float a = exp2(c * 1.6);                         // 1 .. ~3
    bool lower = x < 0.5;
    float v = lower ? 2.0 * x : 2.0 - 2.0 * x;
    float shaped = 0.5 * pow(max(v, MCR_EPS), a);
    return lower ? shaped : 1.0 - shaped;
}

//------------------------------------------------------------------------------------------------------------------------------
// SSAO: points of a hemisphere around the smooth normal, each compared with the depth texture
//
// Every sample is a point in view space, inside a hemisphere of radius Reach above this surface. It is projected to
// the screen; when the depth texture there is closer to the camera than the point, something covers it. The coverage
// ramps up over one Reach of depth, so an overlapping strand of hair or a fold a few millimeters in front already
// counts fully, and it fades out past Range x Reach, so an object far in front (a hand held before the face) does not.
//   Spread : 0 = points close to the tangent plane (contours, creases), 1 = points lifted along the normal
//            (broader, cast-shadow-like darkening under overhangs).
// Convex shapes (the tip of the nose) fall away below the hemisphere and never cover their own points.
//   Facet floor: the hemisphere follows the smooth normal, but the depth texture holds the triangles. Where the two
//   disagree, points near the tangent plane (low Spread) would sink into the real surface and darken it. Every point is
//   kept at least MCR_SSAO_FACET_LIFT x its distance above the plane of the triangle, so only real occluders count.

#define MCR_SSAO_MAX_SAMPLES 24
// Minimum rise above the triangle plane per unit of sample distance (about 11 degrees).
#define MCR_SSAO_FACET_LIFT 0.2
// Occlusion at Occlusion Amount 1 for the average coverage.
#define MCR_SSAO_GAIN 1.5

float MCR_CalcSSAO(MCRSurface s, MCRDepthInfo di)
{
    float radiusFullPx = MCR_ProjectPx(_MCR_SSAOReach, s.viewDepth, di);
    float radiusPx = min(radiusFullPx, MCR_MAX_RADIUS_PX);
    float pixelWeight = MCR_SubPixelFade(radiusPx);
    int q = MCR_OccQualityLevel();
    int samples = q == 0 ? 6 : (q == 1 ? 12 : (q == 2 ? 18 : 24));
    // Sub-pixel radius: no samples (the loop ends at once). One exit point only: an early return here made the d3d11
    // compiler warn about a potentially uninitialized return value.
    if(pixelWeight <= 0.0) samples = 0;

    // Reach on screen -> meters at this depth (also after the on-screen cap).
    float reach = radiusPx * s.viewDepth / max(di.focalPx, MCR_EPS);
    float invReach = 1.0 / max(reach, MCR_TINY);
    // Front Distance (as GTAO): how far in front of a sample point an occluder still counts (hair slightly off the
    // face, a hand off the chest); farther ones (a hand held before the face) do not. Reach sets how wide the
    // darkening spreads sideways, this only the depth. It shrinks with Reach's on-screen cap and is never shorter
    // than Reach.
    float range = max(_MCR_SSAODepthReach * (radiusPx / max(radiusFullPx, MCR_EPS)), reach);
    // Depth texture precision drops with distance: the self-occlusion tolerance follows it.
    float bias = max(_MCR_SSAOBias, 0.0) + s.depth * 0.0003;
    float spread = saturate(_MCR_SSAOSpread);
    float rotate = MCR_PixelRandom(s.centerCS, 0xA341u) * saturate(_MCR_OccNoise) * MCR_TWO_PI;

    // Hemisphere frame in clip space: a sample is origin + T*a + B*b + N*c (3 multiply-adds, no matrix per sample).
    float3 axisT;
    float3 axisB;
    MCR_OrthoBasis(s.nView, axisT, axisB);
    float4 clipO = mul(LIL_MATRIX_P, float4(s.positionVS, 1.0));
    float4 clipT = mul(LIL_MATRIX_P, float4(axisT * reach, 0.0));
    float4 clipB = mul(LIL_MATRIX_P, float4(axisB * reach, 0.0));
    float4 clipN = mul(LIL_MATRIX_P, float4(s.nView * reach, 0.0));
    // Facet floor: height of the frame axes above the triangle plane, and the clip-space step along its normal.
    float3 facetH = float3(dot(axisT, s.nFacet), dot(axisB, s.nFacet), dot(s.nView, s.nFacet));
    float4 clipF = mul(LIL_MATRIX_P, float4(s.nFacet * reach, 0.0));
    // Projection of this surface can differ from SV_Position by a fraction of a pixel.
    float2 pxFix = s.centerCS - MCR_ClipToEyePixel(clipO, di);

    float occ = 0.0;
    [loop]
    for(int i = 0; i < MCR_SSAO_MAX_SAMPLES; i++)
    {
        if(i >= samples) break;
        // Even coverage of the disc: radius sqrt((i + 0.5) / n), golden-angle turns, rotated per pixel.
        float r = sqrt(((float)i + 0.5) / (float)samples);
        float sn;
        float cs;
        sincos((float)i * 2.39996323 + rotate, sn, cs);
        // Lift along the normal with Spread (direction normalized so every point stays inside the hemisphere).
        float3 d = float3(cs * (1.0 - spread), sn * (1.0 - spread), spread);
        d *= r * rsqrt(max(dot(d, d), MCR_EPS));
        float lift = max(r * MCR_SSAO_FACET_LIFT - dot(d, facetH), 0.0);
        float4 clip = clipO + clipT * d.x + clipB * d.y + clipN * d.z + clipF * lift;
        [branch]
        if(clip.w <= 0.01) continue;
        float2 px = MCR_ClipToEyePixel(clip, di) + pxFix;
        float sceneDepth = MCR_ReadDepth(px, di);
        // > 0: the depth texture is in front of the sample point (clip.w = its view depth).
        float delta = clip.w - sceneDepth - bias;
        [branch]
        if(delta <= 0.0) continue;
        float cover = saturate(delta * invReach);
        cover *= 1.0 - smoothstep(range - reach * 0.5, range + reach * 0.5, delta);
        occ += cover;
    }
    occ /= (float)max(samples, 1);

    // Edge Rejection: surfaces seen nearly edge-on (the silhouette) have unreliable neighbors in the depth texture.
    float3 viewDir = -s.positionVS * rsqrt(max(dot(s.positionVS, s.positionVS), MCR_EPS));
    float facing = saturate(dot(s.nView, viewDir));
    occ *= lerp(1.0, smoothstep(0.0, 0.35, facing), saturate(_MCR_SSAOEdgeReject));

    float ao = saturate(occ * MCR_SSAO_GAIN * max(_MCR_OccAmount, 0.0));
    return MCR_OccContrastMap(ao) * pixelWeight;
}

//------------------------------------------------------------------------------------------------------------------------------
// Ray March: rays marched in 3D view space over the hemisphere of the smooth normal, hits tested against the depth texture.
// It weighs an occluder by its distance (Reach, Focus) and returns partial sums (occlusion, occluder distance, rays) so
// that the 2x2 quad share can add the pixels of a quad together before MCR_RayFinalize turns them into
// (occlusion, mean distance 0..1); the tint goes from the near color to the far color with the mean distance.

#define MCR_OCC_MAX_STEPS 18
#define MCR_MARCH_GAIN 1.4
// Occluders more than Thickness behind the visible surface count this much less (may be free space).
#define MCR_OCC_BEHIND_REJECT 0.5

// Ray March rays and march steps for quality 0..3.
int MCR_DistanceLOD(float viewDepth)
{
    return (viewDepth >= 2.0 ? 1 : 0) + (viewDepth >= 4.0 ? 1 : 0) + (viewDepth >= 8.0 ? 1 : 0);
}
// qualityDrop: extra levels removed by Adaptive Screen Quality (near cap without the quad share).
void MCR_OccBudget(float viewDepth, float reachPx, int qualityDrop, out int rays, out int steps)
{
    int q = max(0, MCR_OccQualityLevel() - MCR_DistanceLOD(viewDepth) - qualityDrop);
    rays = q == 0 ? 3 : (q == 1 ? 4 : (q == 2 ? 6 : 8));
    steps = q == 0 ? 6 : (q == 1 ? 8 : (q == 2 ? 12 : 18));
    // Caps by effect radius in eye-local screen pixels; never above the quality setting or the distance LOD.
    if(MCR_OccAdaptive())
    {
        int capRays = reachPx < 3.0 ? 2 : (reachPx < 6.0 ? 3 : (reachPx < 12.0 ? 4 : (reachPx < 24.0 ? 6 : 8)));
        int capSteps = reachPx < 3.0 ? 4 : (reachPx < 6.0 ? 6 : (reachPx < 12.0 ? 8 : (reachPx < 24.0 ? 12 : 18)));
        rays = min(rays,capRays);
        steps = min(steps,capSteps);
    }
}

#if MCR_VR_LEVELS
// VR step compensation (stereo variants only)
//
// The VR level drop marches fewer steps along each direction, and fewer steps find fewer occluders (the thin ones
// fall between two steps): the occlusion gets fainter than on desktop. It is multiplied by
//     (steps without the VR drop / steps actually used) ^ MCR_VR_STEP_COMP
// which costs no depth read. Where the screen size of Reach limits the steps anyway, both counts are equal and
// nothing changes. Higher = darker in VR. Fewer directions only add noise and are not compensated.
#define MCR_VR_STEP_COMP 1.0
// Quality level 0..3 as on desktop: without the VR drop. qualityDrop as in MCR_OccBudget.
int MCR_OccLevelNoVR(float viewDepth, int qualityDrop)
{
    int q = MCR_OccQualitySetting();
    q = q == 4 ? 1 : q;
    return max(0, q - MCR_DistanceLOD(viewDepth) - qualityDrop);
}
float MCR_VRStepComp(int refSteps, int steps)
{
    return pow(clamp((float)max(refSteps, 1) / (float)max(steps, 1), 1.0, 4.0), MCR_VR_STEP_COMP);
}
// Ray March: for the steps MCR_OccBudget returned.
float MCR_OccVRComp(float viewDepth, float reachPx, int qualityDrop, int steps)
{
    int q = MCR_OccLevelNoVR(viewDepth, qualityDrop);
    int refSteps = q == 0 ? 6 : (q == 1 ? 8 : (q == 2 ? 12 : 18));
    if(MCR_OccAdaptive())
    {
        int capSteps = reachPx < 3.0 ? 4 : (reachPx < 6.0 ? 6 : (reachPx < 12.0 ? 8 : (reachPx < 24.0 ? 12 : 18)));
        refSteps = min(refSteps, capSteps);
    }
    return MCR_VRStepComp(refSteps, steps);
}
#endif

// Reach in meters. Screen mode: the value is set for a 60 deg view and follows the zoom (same size on screen).
float MCR_ScaledReach(float meters)
{
    float reach = max(meters, MCR_TINY * 0.1);
    if(_MCR_OccScaleMode > 0.5) reach *= 1.7320508 / max(abs(LIL_MATRIX_P._m11), MCR_TINY);
    return reach;
}

// Ray March Reach.
float MCR_OccReachMeters()
{
    return MCR_ScaledReach(_MCR_OccReach);
}

// GTAO has its own Reach (_MCR_GTAOReach): its horizon search shades wider than Ray March's distance-weighted hits at
// the same value, so it uses a smaller range of values.
float MCR_GTAOReachMeters()
{
    return MCR_ScaledReach(_MCR_GTAOReach);
}

// Near-camera reach cap (Adaptive Screen Quality). Close up, a Reach of a few centimeters spans hundreds of pixels:
// every step then lands on a different, distant depth texel, so the texture cache misses on almost every read and
// the cost per pixel climbs while the avatar also fills more of the screen. The reach on screen is limited to a
// fraction of the eye height (clamped in pixels) with a soft knee, so the world-space reach is unchanged at normal
// distances and only shrinks smoothly once the camera gets very close.
#define MCR_OCC_REACH_CAP_FRAC 0.08
#define MCR_OCC_REACH_CAP_MIN_PX 64.0
#define MCR_OCC_REACH_CAP_MAX_PX 160.0
float MCR_OccCapReach(float reach, float viewDepth, MCRDepthInfo di)
{
    float scale = 1.0;
    [branch]
    if(MCR_OccAdaptive())
    {
        float reachPx = reach * di.focalPx / max(viewDepth, 0.05);
        float capPx = clamp(di.screenSize.y * MCR_OCC_REACH_CAP_FRAC, MCR_OCC_REACH_CAP_MIN_PX, MCR_OCC_REACH_CAP_MAX_PX);
        scale = MCR_SoftCapScale(reachPx, capPx);
    }
    return reach * scale;
}

//------------------------------------------------------------------------------------------------------------------------------
// Soft SSAO: a fixed spiral of points in meters, lifted off the surface (wide, smooth gradients without grain)
//
// The points lie on a golden-angle spiral that runs from this pixel out to Radius (meters), each lifted off the surface
// plane along the smooth normal by Lift (the same lift as Spread of the SSAO above). A point is covered when the depth
// texture is in front of it. The coverage rises smoothly over one Radius of depth, which is what makes the gradient
// wide and soft, and fades out again for occluders farther in front than Front Distance.
//   The kernel is the same for every pixel and only turned by Noise: at Noise 0 neighboring pixels test the same
//   points and the result is smooth without any grain; a little Noise hides the kernel pattern.
//   Focus  : how much the points gather near this pixel (as Focus of the other methods: 0 = the whole Radius counts,
//            1 = mostly the nearest crevices). It is the exponent of the spiral's radius: 0.5 (even over the disc) .. 2.
//   Lift   : 0 = points in the surface plane (curved surfaces shade themselves), 1 = only along the normal.
//   Clean  : how much of lilMCR's self-occlusion removal is applied (slope tolerance and depth calibration of the
//            contact shadow, facet floor of the SSAO above). 0 = none: surfaces turning away from the camera get a
//            light, even shade of their own; 1 = only real occluders count.

#define MCR_SOFT_MAX_SAMPLES 32
// Occlusion at Occlusion Amount 1 for the average coverage.
#define MCR_SOFT_GAIN 2.5

float MCR_CalcSoftAO(MCRSurface s, MCRDepthInfo di)
{
    float radius = MCR_OccCapReach(max(_MCR_SoftRadius, 0.0005), s.viewDepth, di);
    float pixelWeight = MCR_SubPixelFade(radius * di.focalPx / s.viewDepth);
    int q = MCR_OccQualityLevel();
    int samples = q == 0 ? 8 : (q == 1 ? 12 : (q == 2 ? 20 : 32));
    // One exit point only (see MCR_CalcSSAO).
    if(pixelWeight <= 0.0) samples = 0;

    float clean = saturate(_MCR_SoftClean);
    float bias = max(_MCR_SoftBias, 0.0);
    float depthBias = 0.0;
    [branch]
    if(clean > 0.0)
    {
        // Depth texture precision drops with distance, and a depth texel read at its center differs from the sample
        // point by up to ~0.7 px of the surface's depth slope (see MCR_CalcContactShadow).
        float3 viewRay = MCR_ViewRay(s.centerCS, di);
        float facingRay = abs(dot(s.nView, viewRay)) * rsqrt(max(dot(viewRay, viewRay), MCR_EPS));
        float slopePx = s.depth * length(s.nView.xy) / (di.focalPx * max(facingRay, 0.05));
        bias += (s.depth * 0.0003 + min(slopePx, 0.02)) * clean;
        // The depth texture can hold this surface slightly closer to the camera than this pass draws it.
        float offset = s.depth - MCR_ReadDepth(s.centerCS, di);
        depthBias = offset > 0.0 && offset < 0.01 ? offset * clean : 0.0;
    }
    float invRadius = 1.0 / radius;
    // Front Distance (as the SSAO above): occluders farther in front do not count; they fade out over its outer 3/4.
    // Never shorter than the Radius, so the coverage can reach its full value.
    float rangeEnd = max(_MCR_SoftDepthReach, radius * 1.25);
    float rangeStart = max(rangeEnd * 0.25, radius);
    float spiralPower = lerp(0.5, 2.0, saturate(_MCR_SoftFocus));
    float liftAmount = saturate(_MCR_SoftLift);
    float rotate = MCR_IGN(s.centerCS) * saturate(_MCR_OccNoise) * MCR_TWO_PI;

    // Sample frame in clip space: a sample is origin + T*a + B*b + N*c (3 multiply-adds, no matrix per sample).
    float3 axisT;
    float3 axisB;
    MCR_OrthoBasis(s.nView, axisT, axisB);
    float4 clipO = mul(LIL_MATRIX_P, float4(s.positionVS, 1.0));
    float4 clipT = mul(LIL_MATRIX_P, float4(axisT * radius, 0.0));
    float4 clipB = mul(LIL_MATRIX_P, float4(axisB * radius, 0.0));
    float4 clipN = mul(LIL_MATRIX_P, float4(s.nView * radius, 0.0));
    float2 pxFix = s.centerCS - MCR_ClipToEyePixel(clipO, di);
    // Facet floor (as MCR_CalcSSAO): the points follow the smooth normal, the depth texture holds the triangles.
    float3 facetH = float3(dot(axisT, s.nFacet), dot(axisB, s.nFacet), dot(s.nView, s.nFacet));
    float4 clipF = mul(LIL_MATRIX_P, float4(s.nFacet * radius, 0.0));

    float occ = 0.0;
    [loop]
    for(int i = 0; i < MCR_SOFT_MAX_SAMPLES; i++)
    {
        if(i >= samples) break;
        float u = ((float)i + 0.5) / (float)samples;
        // Golden-angle spiral (as MCR_CalcSSAO). Radius exponent 0.5 spreads the points evenly over the disc; a larger
        // one gathers them near this pixel.
        float len = pow(u, spiralPower);
        float sn;
        float cs;
        sincos((float)i * 2.39996323 + rotate, sn, cs);
        // Lifted along the normal (as Spread in MCR_CalcSSAO); normalized, so every point is exactly len x Radius away.
        float3 d = float3(cs * (1.0 - liftAmount), sn * (1.0 - liftAmount), liftAmount);
        d *= len * rsqrt(max(dot(d, d), MCR_EPS));
        float lift = max(len * MCR_SSAO_FACET_LIFT - dot(d, facetH), 0.0) * clean;
        float4 clip = clipO + clipT * d.x + clipB * d.y + clipN * d.z + clipF * lift;
        [branch]
        if(clip.w <= 0.01) continue;
        float2 px = MCR_ClipToEyePixel(clip, di) + pxFix;
        float sceneDepth = MCR_ReadDepth(px, di);
        // > 0: the depth texture is in front of the sample point (clip.w = its view depth).
        float delta = clip.w - sceneDepth - depthBias - bias;
        [branch]
        if(delta <= 0.0) continue;
        occ += smoothstep(0.0, 1.0, delta * invRadius) * (1.0 - smoothstep(rangeStart, rangeEnd, delta));
    }
    occ /= (float)max(samples, 1);

    // Edge Rejection: surfaces seen nearly edge-on (the silhouette) have unreliable neighbors in the depth texture.
    float3 viewDir = -s.positionVS * rsqrt(max(dot(s.positionVS, s.positionVS), MCR_EPS));
    float facing = saturate(dot(s.nView, viewDir));
    occ *= lerp(1.0, smoothstep(0.0, 0.35, facing), saturate(_MCR_SSAOEdgeReject));

    float ao = saturate(occ * MCR_SOFT_GAIN * max(_MCR_OccAmount, 0.0));
    return MCR_OccContrastMap(ao) * pixelWeight;
}

// Focus 0 = far occluders count almost as much as near ones, 1 = only the nearest crevices.
float MCR_OccFocusPower()
{
    return lerp(0.5, 6.0, saturate(_MCR_OccFocus));
}

float MCR_OccFalloff(float x, float k)
{
    return pow(saturate(1.0 - x), k);
}

// Per-pixel constants of the hit test.
struct MCRHitParams
{
    float2 pxFix;       // projection of this surface vs SV_Position (sub-pixel)
    float bias;         // gap that still counts as "on the surface"
    float edge;         // width of the soft hit edge (Softness)
    float thickness;    // occluders deeper than this behind the visible surface count less
    float farLimit;     // occluders this far in front are something else (another player, a hand)
    float tolerance;    // self-occlusion tolerance of the tangent plane
};

// How much the depth texture hides the ray point at this clip position (0..1).
float MCR_RTHitAt(float4 clip, MCRSurface s, MCRDepthInfo di, MCRHitParams hp, inout MCRDepthCache cache, out bool onScreen)
{
    float2 px = MCR_ClipToEyePixel(clip, di) + hp.pxFix;
    onScreen = clip.w > 0.01 && MCR_InsideEye(px, di);
    if(!onScreen) return 0.0;

#if defined(LIL_HDRP)
    float sceneDepth = MCR_ReadDepthCached(px, di, cache);
#else
    // The texel is computed once: this read (at px, as MCR_ReadDepthCached) and the center rebuild below share it.
    float2 texel = MCR_DepthTexel(px / di.screenSize, di);
    float sceneDepth = MCR_ReadDepthTexelCached(texel, px, di, cache);
#endif
    float gap = clip.w - sceneDepth - hp.bias;               // > 0: the point is hidden
    [branch]
    if(gap <= 0.0 || gap >= hp.farLimit * 2.0) return 0.0;   // visible, or far in front: no occluder
    float hit = smoothstep(0.0, hp.edge, gap);
    hit *= 1.0 - smoothstep(hp.thickness, hp.thickness * 2.0, gap) * MCR_OCC_BEHIND_REJECT;
    hit *= 1.0 - smoothstep(hp.farLimit, hp.farLimit * 2.0, gap);
    [branch]
    if(s.smoothing <= 0.0) return hit;
    // Rebuilt at the depth texel center, where the depth was stored (see MCR_DepthTexelCenter).
#if defined(LIL_HDRP)
    return hit * MCR_AboveSurface(MCR_ViewRay(MCR_DepthTexelCenter(px, di), di) * sceneDepth, s, hp.tolerance);
#else
    return hit * MCR_AboveSurface(MCR_ViewRay(MCR_DepthTexelToEyeCenter(texel, di), di) * sceneDepth, s, hp.tolerance);
#endif
}

// Which rays this pixel marches.
//  Normal    : all rays of the budget, rotated per pixel.
//  Quad set  : (2x2 quad share on) a set of 4 x m directions, rotated once per quad; direction j belongs to the pixel
//              with quadIndex == j % 4. m = rays / 2 (Balanced: the quad holds twice the budget) or rays / 4 (Max).
//              share    = this pixel marches only its own m directions; the quad exchange adds the other three.
//              fallback = (share off: silhouette, depth step, crease, a neighbor that does not march) this pixel
//                         still marches its own m directions for the neighbors (so a sharing neighbor never gets a
//                         different direction distribution), and for itself the whole set when that is not more
//                         than budget + m rays, otherwise its own m plus an independent set of the normal budget.
#define MCR_OCC_MAX_SET 16
struct MCRRaySet
{
    bool quadSet;
    bool share;
    int quadDiv;            // 2 Balanced / 4 Max: share of the budget each pixel marches
    int quadIndex;          // 0..3 position inside the 2x2 quad
    float quadRotation;     // 0..1 rotation shared by the quad (already scaled by Noise)
    int rays;               // budget (MCR_OccBudget)
    int steps;
};

// Own directions per pixel in the quad set.
int MCR_RayOwnCount(MCRRaySet set)
{
    // Unsigned division (both are positive): cheaper than a signed integer divide.
    return (int)(((uint)set.rays + (uint)set.quadDiv - 1u) / (uint)max(set.quadDiv, 1));
}

// Directions in the whole set (also exchanged so that partners agree on it).
int MCR_RaySetSize(MCRRaySet set)
{
    return set.quadSet ? MCR_RayOwnCount(set) * 4 : set.rays;
}

// Returns the sums of every ray this pixel marched for itself: (occlusion sum, occluder distance sum, rays). The
// distance sum is weighted by occlusion. ownSums: the same, only for this pixel's own directions of the quad set.
float3 MCR_RayMarchSums(MCRSurface s, MCRDepthInfo di, float reach, MCRRaySet set, out float3 ownSums)
{
    int steps = set.steps;
    float k = MCR_OccFocusPower();
    float2 noise = MCR_OccNoiseValues(s.centerCS);

    // Phase A: directions of the quad set (j = firstA + n * strideA, azimuths of setSize).
    // Phase B: an independent normal set (j = 0..rays-1, azimuths of rays), only without the quad set or in the
    //          large fallback.
    int setSize = MCR_RaySetSize(set);
    int countA = 0;
    int firstA = 0;
    int strideA = 1;
    int countB = set.rays;
    if(set.quadSet)
    {
        noise.x = set.quadRotation;
        int own = MCR_RayOwnCount(set);
        countB = 0;
        if(set.share)
        {
            countA = own;
            firstA = set.quadIndex;
            strideA = 4;
        }
        else if(setSize <= set.rays + own)
        {
            countA = setSize;
        }
        else
        {
            countA = own;
            firstA = set.quadIndex;
            strideA = 4;
            countB = set.rays;
        }
    }
    int total = countA + countB;

    // Soft: a hit lets part of the ray through (averages several steps, less noise). Softness 0: the first hit decides.
    // The partial opacity is per step, scaled so that the quality level (step count) does not change the level.
    float opacity = lerp(1.0, 1.0 - pow(0.45, 7.0 / (float)steps), saturate(_MCR_OccSoftness * 4.0));
    float sinMax = sin(radians(clamp(_MCR_OccAngle, 10.0, 90.0)));

    float3 axisA;
    float3 axisB;
    MCR_OrthoBasis(s.nView, axisA, axisB);
    float3 origin = s.positionVS + s.nView * (s.depth * 0.0004);
    // All rays share the projected origin and step denominator.
    float4 clipOrigin = mul(LIL_MATRIX_P, float4(origin, 1.0));
    float invSteps = 1.0 / (float)steps;
    float azimuthA = MCR_TWO_PI / (float)max(setSize, 1);
    float azimuthB = MCR_TWO_PI / (float)max(set.rays, 1);
    MCRHitParams hp;
    hp.pxFix = s.centerCS - MCR_ViewToEyePixel(s.positionVS, di);
    hp.bias = 0.005 * reach;
    hp.thickness = max(_MCR_OccThickness, MCR_TINY);
    hp.edge = max(s.depth * 0.0003, hp.thickness * lerp(0.05, 0.35, saturate(_MCR_OccSoftness)));
    hp.farLimit = max(hp.thickness * 3.0, reach * 2.0);
    hp.tolerance = s.depth * 0.0004;

    float3 sums = float3(0.0, 0.0, 0.0);
    ownSums = float3(0.0, 0.0, 0.0);
    [loop]
    for(int n = 0; n < MCR_OCC_MAX_SET; n++)
    {
        if(n >= total) break;
        bool phaseA = n < countA;
        int j = phaseA ? firstA + n * strideA : n - countA;
        float ray = (float)j;
        // Cosine-weighted directions inside the Angle cone: evenly spaced azimuths (rotated per pixel / quad),
        // elevations from a golden-ratio sequence so that the few rays do not all lean the same way.
        // Phase B is offset by half a step so that it does not repeat phase A's directions.
        float u = frac(ray * MCR_GOLDEN_RATIO + (phaseA ? 0.5 : 0.25) + noise.y);
        float sinTheta = sqrt(u) * sinMax;
        float cosTheta = sqrt(1.0 - sinTheta * sinTheta);
        float phi = phaseA ? (ray + noise.x) * azimuthA : (ray + noise.x + 0.5) * azimuthB;
        float sp;
        float cp;
        sincos(phi, sp, cp);
        float3 dir = (axisA * cp + axisB * sp) * sinTheta + s.nView * cosTheta;
        // Facet floor (as MCR_CalcSSAO): the directions follow the smooth normal, but the depth texture holds the
        // triangles. Where the two disagree (edited normals, a vertex normal that points away from the camera), part of
        // the rays would start out below the triangle itself and hit it a few millimeters on: a shade over the whole
        // surface, in one band per triangle. Every direction is kept at least MCR_SSAO_FACET_LIFT above the triangle.
        float below = MCR_SSAO_FACET_LIFT - dot(dir, s.nFacet);
        if(below > 0.0) dir = normalize(dir + s.nFacet * below);
        MCRClipRay cr;
        cr.origin = clipOrigin;
        cr.dir = mul(LIL_MATRIX_P, float4(dir, 0.0));

        MCRDepthCache cache = (MCRDepthCache)0;
        float visible = 1.0;
        float rayOcc = 0.0;
        float rayDist = 0.0;
        [loop]
        for(int i = 0; i < MCR_OCC_MAX_STEPS; i++)
        {
            if(i >= steps) break;
            float f = saturate(((float)i + 0.75 + noise.y) * invSteps);
            // f=1 has zero falloff for every Focus value; no depth read needed.
            if(f >= 1.0) break;
            float x = f * f;
            float dist = reach * x;             // denser close to the surface
            bool onScreen;
            float hit = MCR_RTHitAt(cr.origin + cr.dir * dist, s, di, hp, cache, onScreen);
            if(!onScreen) break;
            // A miss contributes nothing; avoid falloff pow and accumulation for it.
            [branch]
            if(hit <= 0.0) continue;
            // x is already the normalized squared step distance.
            float w = hit * opacity * MCR_OccFalloff(x, k);
            rayOcc += visible * w;
            rayDist += visible * w * x;
            visible *= 1.0 - w;
            if(visible < 0.02) break;
        }
        float3 r = float3(rayOcc, rayDist, 1.0);
        sums += r;
        if(phaseA && set.quadSet && (j & 3) == set.quadIndex) ownSums += r;
    }
    return sums;
}

// Partial sums -> (occlusion 0..1 after Amount / Contrast, mean occluder distance 0..1 for the tint).
// weight = mask x sub-pixel fade of this pixel.
float2 MCR_RayFinalize(float3 sums, float weight)
{
    float occ = saturate(sums.x / max(sums.z, MCR_EPS)) * MCR_MARCH_GAIN;
    float meanDistance = sums.y / max(sums.x, MCR_EPS);
    // Remove the faint noise floor; a reach smaller than about a pixel only flickers.
    occ = max(occ - 0.01, 0.0) * weight;
    return float2(MCR_OccContrastMap(occ * max(_MCR_OccAmount, 0.0)), saturate(meanDistance));
}

//------------------------------------------------------------------------------------------------------------------------------
// GTAO: ground-truth based ambient occlusion
//
// The hemisphere is cut into slices that contain the view direction. In each slice the depth texture is walked in both
// screen directions to find the highest occluder angle (horizon) inside Reach, and the visible arc between the two
// horizons is integrated in closed form, weighted by the cosine to the normal projected into the slice. Unlike counting
// hits, the result is the real occluded fraction of the cosine-weighted hemisphere: a narrow gap darkens strongly and a
// gentle slope only a little, at any distance and Reach.
//   Front Distance: the walk covers Reach along the surface (how far shading spreads sideways), but an occluder found
//               there may sit up to Front Distance away from the point (in front of / above the surface): hair, a chin
//               or a hand a few centimeters off the skin still shades it without the shading growing wider. It follows
//               Reach's scaling (screen mode, near-camera cap) and is never shorter than Reach.
//   Focus     : where the distance falloff starts. An occluder keeps its full angle up to that point and fades out
//               linearly to Front Distance (0 = almost to the end, 1 = from about a fifth of it on). The falloff blends the
//               horizon angle itself, so it is kept flat near the surface: a steep curve would erase most of the result.
//   Thickness : occluders are assumed to be about this thick. Only when the depth texture jumps more than Thickness
//               behind the occluder that set the horizon (the walk has passed a strand or a finger and sees what lies
//               behind it) does the horizon relax back toward the surface. A continuous overhang (hair over the face,
//               the chin over the neck) keeps its horizon even though its angle naturally drops with distance.
//   Tint      : the near / far tint follows where the horizon occluder sits in the falloff: inside the full-weight zone
//               it is "near", toward Reach it moves to "far" (where its occlusion is also fading).
//   Wide Reach: optional second, wider band of steps whose occluders count with Wide Amount (large soft shading).
//   Triangle floor: the slice uses the smooth normal for its cosine weight, but the depth texture holds the triangles.
//   Where they disagree (curved low-poly spots, faces with edited normals) the neighboring triangles rise above the
//   smooth tangent plane and a flat-shaded face would darken all over. In each slice the lowest horizon is therefore
//   the higher of the smooth and the triangle tangent, and the visibility is measured against what that floor alone
//   leaves open, so an open surface is fully visible whatever its normals are.
//   The cosine-weighted visibility is raised to a power before it becomes occlusion: the raw value of a mostly open
//   surface stays close to 1 and would read as a faint veil, the power gives creases and overlaps their usual depth.

#define MCR_GTAO_MAX_SLICES 6
#define MCR_GTAO_MAX_STEPS 12
#define MCR_HALF_PI 1.57079632679
// Visibility -> occlusion curve (occlusion = 1 - visibility^power).
#define MCR_GTAO_POWER 2.2
// Horizon relax per step once the walk is behind a thin occluder.
#define MCR_GTAO_THIN_RELAX 0.25
// Rise above the triangle plane per unit of distance that is still treated as the surface itself (about 4.6 degrees,
// the same slope as the Ray March / Contact Shadow self-occlusion tolerance).
#define MCR_GTAO_POLY_TOLERANCE 0.08

// Visible arc between the view direction and horizon angle h, in a slice with projected normal angle n.
float MCR_GTAOArc(float h, float n, float cosN, float sinN)
{
    return (cosN + 2.0 * h * sinN - cos(2.0 * h - n)) * 0.25;
}

// Distance falloff of an occluder (x = distance / Reach): 1 up to the Focus start, then linear to 0 at Reach.
float MCR_GTAOFalloff(float x, float focus)
{
    float start = lerp(0.9, 0.2, focus);
    return saturate((1.0 - x) / max(1.0 - start, 0.05));
}

// Edge Gradient: sideways fade of an occluder by where it sits in its band (f = 0 at the start of the band, 1 at its
// end: Reach for the inner band, Wide Reach for the wide one). The distance falloff above is measured against Front
// Distance, which is usually several times Reach, so without this every occluder inside Reach counts fully and the
// shading stops with a hard edge where the walk ends. soft = 0 keeps that hard edge.
// Two parts, both growing with soft:
//   edge  : smooth fade over the outer soft x 100 % of the band (no visible end of the walk),
//   curve : (1 - f)^(2.5 soft), a weight that already drops from the center on, so the shading is darkest right at the
//           occluder and thins out over the whole band instead of staying flat and fading only at the rim.
// A farther occluder only lowers its own weight: a nearer one still sets the horizon, so the shading darkens toward
// the occluder and fades out smoothly away from it.
#define MCR_GTAO_GRADIENT_POWER 2.5
float MCR_GTAOEdgeFade(float f, float soft)
{
    float s = saturate((1.0 - f) / max(soft, 0.001));
    float edge = s * s * (3.0 - 2.0 * s);
    // max(): pow(0, 0) is NaN on some GPUs (soft = 0 at the band end); the edge term is 0 there anyway.
    float curve = pow(max(1.0 - f, 0.00001), MCR_GTAO_GRADIENT_POWER * soft);
    return edge * curve;
}

// Slices and steps per side for quality 0..3, lowered by the distance LOD like Ray March.
void MCR_GTAOBudget(float viewDepth, float reachPx, out int slices, out int steps)
{
    int q = max(0, MCR_OccQualityLevel() - MCR_DistanceLOD(viewDepth));
    slices = q == 0 ? 2 : (q == 1 ? 3 : (q == 2 ? 4 : 6));
    steps = q == 0 ? 4 : (q == 1 ? 6 : (q == 2 ? 8 : 12));
    if(MCR_OccAdaptive())
    {
        // A few pixels of Reach cannot hold more distinct steps than pixels.
        steps = min(steps, (int)clamp(ceil(reachPx * 0.75), 2.0, (float)MCR_GTAO_MAX_STEPS));
    }
}

#if MCR_VR_LEVELS
// VR step compensation of GTAO for the steps MCR_GTAOBudget returned (see MCR_VR_STEP_COMP).
float MCR_GTAOVRComp(float viewDepth, float reachPx, int steps)
{
    int q = MCR_OccLevelNoVR(viewDepth, 0);
    int refSteps = q == 0 ? 4 : (q == 1 ? 6 : (q == 2 ? 8 : 12));
    if(MCR_OccAdaptive())
    {
        refSteps = min(refSteps, (int)clamp(ceil(reachPx * 0.75), 2.0, (float)MCR_GTAO_MAX_STEPS));
    }
    return MCR_VRStepComp(refSteps, steps);
}
#endif

// cos of the highest occluder seen from this pixel along one screen direction.
//   lowCos : cos of the tangent plane in this slice (no occluder)
//   distOut: distance of the occluder that set the horizon, relative to Reach (for the near / far tint)
//   origin : this surface lifted by the depth precision, so the surface itself stays below the tangent plane
float MCR_GTAOHorizon(MCRSurface s, MCRDepthInfo di, float3 origin, float2 stepPx, float3 viewDir, float reach, float lowCos,
    int steps, float wideSteps, float wideReach, float jitter, float focus, float decay, float edgeSoft, out float distOut)
{
    float horizonCos = lowCos;
    distOut = 0.0;
    float occluderDepth = -1.0;     // depth of the occluder that set the horizon (-1 = none yet)
    float thickness = max(_MCR_OccThickness, MCR_TINY);
    float farDist = max(_MCR_GTAODepthReach * reach / max(_MCR_GTAOReach, MCR_TINY), reach);
    MCRDepthCache cache = (MCRDepthCache)0;
    // Reach of this slice on screen (pixels). Every inner step moves at least one new pixel, so the inner band holds at
    // most that many steps: otherwise step i would be pushed out to i + 1 pixels, past Reach, and a small Reach could
    // never shade smaller than the step count in pixels.
    float reachLenPx = length(stepPx);
    int inner = min(steps, max(1, (int)floor(reachLenPx)));
    int total = inner + (int)wideSteps;
    float wideAmount = saturate(_MCR_GTAOWideAmount);
    [loop]
    for(int i = 0; i < MCR_GTAO_MAX_STEPS + MCR_GTAO_MAX_STEPS / 2; i++)
    {
        if(i >= total) break;
        bool wide = i >= inner;
        // Inner band: squared spacing (denser near the surface). Wide band: linear from Reach to Wide Reach.
        float t = wide ? ((float)(i - inner) + 0.5 + jitter) / max(wideSteps, 1.0)
                       : ((float)i + 0.5 + jitter) / (float)inner;
        t = saturate(t);
        float distPx = wide ? lerp(1.0, wideReach / reach, t) : t * t;
        // Inner band: at least one new pixel per step, so the first steps do not read this pixel again (never past
        // Reach, see inner above).
        // (The caller skips slices whose Reach is under half a pixel, so reachLenPx >= 0.5 here.)
        if(!wide) distPx = min(max(distPx, ((float)i + 1.0) / max(reachLenPx, 0.5)), max(1.0, 1.0 / max(reachLenPx, 0.5)));
        float2 px = s.centerCS + stepPx * distPx;
        if(!MCR_InsideEye(px, di)) break;
        // Rebuild the point where its depth was actually stored (see MCR_DepthTexelCenter). The step is at least one
        // pixel long, so the snapped point never falls back onto this pixel.
#if defined(LIL_HDRP)
        px = MCR_DepthTexelCenter(px, di);
        float sceneDepth = MCR_ReadDepthCached(px, di, cache);
#else
        // The texel is computed once and read directly (MCR_ReadDepthTexelCached), not recomputed from the center.
        float2 texel = MCR_DepthTexel(px / di.screenSize, di);
        px = MCR_DepthTexelToEyeCenter(texel, di);
        float sceneDepth = MCR_ReadDepthTexelCached(texel, px, di, cache);
#endif
        float3 v = MCR_ViewRay(px, di) * sceneDepth - origin;
        float dist = length(v);
        // Polygon tolerance (same idea as MCR_AboveSurface of Ray March / Contact Shadow): the depth texture holds flat
        // triangles, and a neighboring triangle of a low-poly concave spot rises a few degrees above this triangle's
        // plane. That rise is not an occluder; only what rises more than MCR_GTAO_POLY_TOLERANCE x distance counts.
        float rise = dot(v, s.nFacet);
        v -= s.nFacet * clamp(rise, 0.0, dist * MCR_GTAO_POLY_TOLERANCE * s.smoothing);
        float sampleCos = dot(v, viewDir) / max(length(v), MCR_EPS);
        // Influence of this occluder by its distance (Focus), by band, and by its sideways place in the band (Edge
        // Gradient; distPx is the fraction of Reach in the inner band, t the fraction of the wide band).
        float x = dist / (wide ? max(wideReach, farDist) : farDist);
        float w = MCR_GTAOFalloff(x, focus) * (wide ? wideAmount * MCR_GTAOEdgeFade(t, edgeSoft)
                                                    : MCR_GTAOEdgeFade(distPx, edgeSoft));
        float candidate = lerp(lowCos, sampleCos, w);
        [flatten]
        if(candidate > horizonCos)
        {
            horizonCos = candidate;
            distOut = wide ? 1.0 : 1.0 - w;
            occluderDepth = sceneDepth;
        }
        else if(occluderDepth > 0.0 && sceneDepth > occluderDepth + thickness)
        {
            // Behind a thin occluder: let the horizon sink back a little toward what this sample sees.
            horizonCos = lerp(horizonCos, max(candidate, lowCos), decay);
        }
    }
    return horizonCos;
}

// Returns (occlusion 0..1 after Amount / Contrast, mean occluder distance 0..1).
float2 MCR_CalcGTAO(MCRSurface s, MCRDepthInfo di, float reach, float weight)
{
    float reachPx = reach * di.focalPx / max(s.viewDepth, 0.05);
    int slices;
    int steps;
    MCR_GTAOBudget(s.viewDepth, reachPx, slices, steps);
    #if MCR_VR_LEVELS
        weight *= MCR_GTAOVRComp(s.viewDepth, reachPx, steps);
    #endif

    // Same scale as reach (Screen scale mode, near-camera cap) so the wide band keeps its ratio to the main band.
    float reachScale = reach / max(_MCR_GTAOReach, MCR_TINY);
    float wideReach = max(_MCR_GTAOWideReach * reachScale, reach);
    float wideSteps = _MCR_GTAOWideAmount > 0.0 && wideReach > reach * 1.05 ? (float)max(steps >> 1, 2) : 0.0;
    float2 noise = MCR_OccNoiseValues(s.centerCS);
    float focus = saturate(_MCR_OccFocus);
    float decay = MCR_GTAO_THIN_RELAX;
    float edgeSoft = saturate(_MCR_GTAOSoftness);

    float3 viewDir = -s.positionVS * rsqrt(max(dot(s.positionVS, s.positionVS), MCR_EPS));
    float3 origin = s.positionVS + s.nFacet * (0.0005 + s.depth * 0.0004);
    float visibility = 0.0;
    float openSum = 0.0;        // the same integral with only the floor horizons (fully open surface)
    float distSum = 0.0;
    float distWeight = 0.0;

    [loop]
    for(int slice = 0; slice < MCR_GTAO_MAX_SLICES; slice++)
    {
        if(slice >= slices) break;
        float phi = ((float)slice + noise.x) * (MCR_PI / (float)slices);
        float3 dir;
        sincos(phi, dir.y, dir.x);
        dir.z = 0.0;

        // Slice plane: view direction and dir. The normal is projected into it.
        float3 orthoDir = dir - dot(dir, viewDir) * viewDir;
        float3 axis = normalize(cross(dir, viewDir));
        float3 projN = s.nView - axis * dot(s.nView, axis);
        float projLen = length(projN);
        [branch]
        if(projLen < 0.001) continue;
        float cosN = saturate(dot(projN, viewDir) / projLen);
        float n = (dot(orthoDir, projN) >= 0.0 ? 1.0 : -1.0) * acos(cosN);
        float sinN = sin(n);

        // Triangle tangent in this slice (falls back to the smooth one when the triangle is edge-on to the slice).
        float3 projF = s.nFacet - axis * dot(s.nFacet, axis);
        float projFLen = length(projF);
        float nf = projFLen > 0.001
            ? (dot(orthoDir, projF) >= 0.0 ? 1.0 : -1.0) * acos(saturate(dot(projF, viewDir) / projFLen))
            : n;
        // Floor horizons: the higher of the two tangents on each side.
        float lowCos0 = max(cos(n + MCR_HALF_PI), cos(nf + MCR_HALF_PI));
        float lowCos1 = max(cos(n - MCR_HALF_PI), cos(nf - MCR_HALF_PI));
        float h0Open = n + clamp(acos(lowCos0) - n, -MCR_HALF_PI, MCR_HALF_PI);
        float h1Open = n + clamp(-acos(lowCos1) - n, -MCR_HALF_PI, MCR_HALF_PI);
        float openArc = MCR_GTAOArc(h0Open, n, cosN, sinN) + MCR_GTAOArc(h1Open, n, cosN, sinN);
        openSum += projLen * openArc;

        // Screen extent of Reach along this slice (projected, so flipped / off-center projections stay correct).
        float2 stepPx = MCR_ViewToEyePixel(s.positionVS + orthoDir * reach, di) - MCR_ViewToEyePixel(s.positionVS, di);
        [branch]
        if(dot(stepPx, stepPx) < 0.25)
        {
            // Reach under half a pixel in this slice: nothing to find, the slice is open.
            visibility += projLen * openArc;
            continue;
        }

        float d0;
        float d1;
        float cos0 = MCR_GTAOHorizon(s, di, origin, stepPx, viewDir, reach, lowCos0, steps, wideSteps, wideReach,
            noise.y, focus, decay, edgeSoft, d0);
        float cos1 = MCR_GTAOHorizon(s, di, origin, -stepPx, viewDir, reach, lowCos1, steps, wideSteps, wideReach,
            -noise.y, focus, decay, edgeSoft, d1);

        // Horizon angles measured from the view direction, kept inside the hemisphere of the projected normal.
        float h0 = n + clamp(acos(clamp(cos0, -1.0, 1.0)) - n, -MCR_HALF_PI, MCR_HALF_PI);
        float h1 = n + clamp(-acos(clamp(cos1, -1.0, 1.0)) - n, -MCR_HALF_PI, MCR_HALF_PI);
        float arc = MCR_GTAOArc(h0, n, cosN, sinN) + MCR_GTAOArc(h1, n, cosN, sinN);
        // Weighted by the projected normal length (the cosine weight of the slice).
        visibility += projLen * arc;

        float sliceOcc = saturate(1.0 - arc / max(openArc, MCR_EPS));
        distSum += (d0 + d1) * 0.5 * sliceOcc;
        distWeight += sliceOcc;
    }

    float openVisibility = openSum > MCR_EPS ? saturate(visibility / openSum) : 1.0;
    float occ = 1.0 - pow(openVisibility, MCR_GTAO_POWER);
    // Remove the faint floor left by polygon edges (scaled with the power curve above).
    occ = max(occ - 0.025, 0.0) * (1.0 / 0.975) * weight;
    return float2(MCR_OccContrastMap(occ * max(_MCR_OccAmount, 0.0)), saturate(distSum / max(distWeight, MCR_EPS)));
}

//------------------------------------------------------------------------------------------------------------------------------
// Contact Shadow toward a fixed light direction (3D screen-space ray march)

// Direction TOWARD the light in view space.
//  Space 0 Avatar : x = right as seen from the front, y = up, z = toward the front of the avatar.
//                   _MCR_AvatarBasisX/Y/Z are those axes in object space (written by the Macaron tools).
//  Space 1 Camera : x = screen right, y = screen up, z = toward the viewer.
//  Space 2 World  : world axes.
float3 MCR_LightDirView(float3 d, float spaceValue)
{
    if(dot(d, d) < MCR_EPS) d = float3(0.0, 1.0, 0.0);
    int space = (int)round(spaceValue);
    float3 dirVS;
    if(space == 1)
    {
        dirVS = d;
    }
    else
    {
        float3 dirWS = d;
        if(space == 0)
        {
            float3 dirOS = _MCR_AvatarBasisX.xyz * d.x + _MCR_AvatarBasisY.xyz * d.y + _MCR_AvatarBasisZ.xyz * d.z;
            if(dot(dirOS, dirOS) < MCR_EPS) dirOS = d;
            dirWS = mul((float3x3)LIL_MATRIX_M, dirOS);
        }
        dirVS = mul((float3x3)LIL_MATRIX_V, dirWS);
    }
    return dirVS / max(length(dirVS), MCR_EPS);
}

float3 MCR_ShadowLightDirView()
{
    return MCR_LightDirView(_MCR_ShadowDirection.xyz, _MCR_ShadowSpace);
}

// -1 Lowest / 0 Low / 1 Medium / 2 High / 3 Ultra
int MCR_ShadowQuality()
{
    return (int)clamp(round(_MCR_ShadowQuality), -1.0, 3.0);
}

// Adaptive Screen Quality of the contact shadow (material toggle, uniform per draw).
bool MCR_ShadowAdaptive()
{
    return _MCR_ShadowScreenLOD > 0.5;
}

// Near-camera length cap (Adaptive): like the occlusion reach, a long shadow seen up close spans hundreds of pixels,
// each step reads a distant depth texel and the step count doubles. The on-screen length is limited with the same
// soft knee; the cap is wider than the occlusion's because the shadow length is easy to see.
#define MCR_SHADOW_LENGTH_CAP_FRAC 0.2
#define MCR_SHADOW_LENGTH_CAP_MIN_PX 128.0
#define MCR_SHADOW_LENGTH_CAP_MAX_PX 320.0

// One point of the shadow ray: how strongly the depth texture blocks it (0..1, no length fade).
struct MCRShadowRay
{
    float3 originVS;     // ray start (surface pushed off along the normal)
    float3 surfaceVS;    // this surface
    float3 dirVS;        // toward the light
    MCRClipRay clip;     // origin / (dir * maxDist) in clip space
    float2 pxCorrection;
    float maxDist;
    float thickness;     // base thickness + slope tolerance (see MCR_CalcContactShadow)
    float edge;          // depth behind the occluder over which the hit rises to full
    float rejectDist;
    float depthBias;     // how far the depth texture sits in front of this surface at this pixel (>= 0)
};

// Gradual hit: the hit rises to full over this fraction of the shadow length behind the occluder (plus the thickness).
#define MCR_SHADOW_EDGE_FRAC 0.15
// tan of the bend between neighboring triangles that still counts as one surface (triangle plane test in MCR_ShadowBlock).
#define MCR_FACET_BEND 0.18

float MCR_ShadowBlock(MCRShadowRay r, MCRSurface s, MCRDepthInfo di, float t, inout MCRDepthCache cache, out bool valid)
{
    valid = true;
    float4 clip = r.clip.origin + r.clip.dir * t;
    if(clip.w < 0.01) { valid = false; return 0.0; }               // behind the camera
    float2 samplePx = MCR_ClipToEyePixel(clip, di) + r.pxCorrection;
    if(!MCR_InsideEye(samplePx, di)) { valid = false; return 0.0; }

    // Moved back by this pixel's own depth texture offset, so the receiver's own surface reads where it really is.
#if defined(LIL_HDRP)
    float sceneDepth = MCR_ReadDepthCached(samplePx, di, cache) + r.depthBias;
#else
    // The texel is computed once: this read (at samplePx, as MCR_ReadDepthCached) and the center rebuild below share it.
    float2 sampleTexel = MCR_DepthTexel(samplePx / di.screenSize, di);
    float sceneDepth = MCR_ReadDepthTexelCached(sampleTexel, samplePx, di, cache) + r.depthBias;
#endif
    // > 0: the depth texture has something in front of the ray point.
    float diff = clip.w - sceneDepth - _MCR_ShadowOffset.x;
    [branch]
    if(diff <= r.thickness * 0.5 || diff >= r.rejectDist) return 0.0;
    float h = smoothstep(r.thickness * 0.5, r.thickness * 0.5 + r.edge, diff);
    // Far in front = other player / object between camera and avatar -> not an occluder.
    h *= 1.0 - smoothstep(r.rejectDist * 0.7, r.rejectDist, diff);

    // Self-shadow tests in 3D, on the point the depth texture holds (rebuilt at the depth texel center, where the
    // depth was stored, see MCR_DepthTexelCenter).
#if defined(LIL_HDRP)
    float3 occluderVS = MCR_ViewRay(MCR_DepthTexelCenter(samplePx, di), di) * sceneDepth;
#else
    float3 occluderVS = MCR_ViewRay(MCR_DepthTexelToEyeCenter(sampleTexel, di), di) * sceneDepth;
#endif
    float3 toOccluder = occluderVS - s.positionVS;
    // Triangle plane: a point of this triangle, or of a neighbor on a convex shape, lies on or below the plane of the
    // triangle at any view angle; a real occluder (nose, hair, an arm) rises well above it. Seen at a grazing angle,
    // the depth comparison alone cannot tell them apart (layered bands).
    // The plane differs per triangle, so a fixed tolerance switched a real shadow on for one triangle and off for the
    // next (sharp triangle-shaped shards). The tolerance grows with the distance by the bend allowed between
    // neighboring triangles (~10 deg), with a wide soft ramp: a real occluder clears it on every triangle alike.
    float facetTolerance = r.thickness + length(toOccluder) * MCR_FACET_BEND;
    h *= smoothstep(facetTolerance, facetTolerance * 2.5, dot(toOccluder, s.nFacet));
    // Smooth tangent plane: the occluder must also rise above it by more than the polygon / curvature error.
    [branch]
    if(s.smoothing <= 0.0 || h <= 0.0) return h;
    return h * MCR_AboveSurface(occluderVS, s, r.thickness);
}

// Returns the cast shadow (0..1). backShade: side facing away from the fixed light (0..1).
float MCR_CalcContactShadow(MCRSurface s, MCRDepthInfo di, out float backShade)
{
    float3 lightVS = MCR_ShadowLightDirView();
    float ndl = dot(s.nView, lightVS);
    backShade = 1.0 - smoothstep(-0.12, 0.28, ndl);

    float normalWeight = lerp(1.0, smoothstep(-0.05, 0.25, ndl), saturate(_MCR_ShadowNormalReject));

    bool adaptive = MCR_ShadowAdaptive();
    // Mirror / VR / distance LOD lower the level down to Low; only the Lowest setting itself goes below.
    int setting = MCR_ShadowQuality();
    if(MCR_MirrorLowest()) setting = min(setting, 0);
    setting = max(min(setting, 0), setting - MCR_VR_LEVELS);
    int quality = max(min(setting, 0), setting - MCR_DistanceLOD(s.viewDepth));
    int rayCount = max(quality, 0) + 1;                  // 1 / 1 / 2 / 3 / 4
    // A zero cone makes every ray identical, including the step jitter.
    if(_MCR_ShadowSoftness <= 0.0) rayCount = 1;
    // Lowest: 5 steps that never grow with the length, and one halving instead of three for the shadow end.
    int baseSteps = quality >= 3 ? 24 : (quality == 2 ? 16 : (quality == 1 ? 12 : (quality == 0 ? 8 : 5)));
    int maxSteps = quality < 0 ? baseSteps : baseSteps * 2;
    int refineSteps = quality < 0 ? 1 : 3;

    MCRShadowRay r;
    r.maxDist = max(_MCR_ShadowLength, 0.001);
    r.thickness = max(_MCR_ShadowThickness, 0.0001) + s.depth * 0.0008;  // depth precision grows with distance
    // Slope tolerance: how much the depth of the surface changes over one pixel on screen. Seen at a grazing angle, a
    // depth texel read at its center differs from the ray by up to ~0.7 px of that slope. From the smooth normal, not
    // the triangle's: a per-triangle value made the leftover bands stop at triangle edges (sharp-edged patches).
    // Capped, so a nearly edge-on surface does not swallow every occluder.
    float3 viewRay = MCR_ViewRay(s.centerCS, di);
    float facing = abs(dot(s.nView, viewRay)) * rsqrt(max(dot(viewRay, viewRay), MCR_EPS));
    {
        float slopePx = s.depth * length(s.nView.xy) / (di.focalPx * max(facing, 0.05));
        r.thickness += min(slopePx * 0.75, 0.04);
    }
    // Nearly edge-on (extreme angles): the depth texture cannot resolve the surface at all there, so the shadow fades
    // out instead of showing leftover bands. Such a surface covers only a thin strip of the screen.
    float grazingWeight = smoothstep(0.05, 0.18, facing);
    r.rejectDist = max(_MCR_ShadowReject, r.thickness * 2.0);
    // Self calibration: the depth texture does not always hold this surface exactly where this pass draws it (shadow
    // caster bias of the depth render, depth precision, a slightly different vertex result). An offset of a few
    // millimeters toward the camera made the receiver shadow itself in patches with layered bands at any thickness
    // below that offset. It is measured at this pixel and taken off every read of the ray. Only small offsets in
    // front count: a larger difference is another surface (transparent materials, something in front).
    {
        float centerScene = MCR_ReadDepth(s.centerCS, di);
        float bias = s.depth - centerScene;
        r.depthBias = bias > 0.0 && bias < 0.01 ? bias : 0.0;
    }

    // Long shadows: more steps (up to x2) so one step stays a few pixels on screen.
    float lengthPx = r.maxDist * di.focalPx / max(s.viewDepth, 0.05);
    if(adaptive)
    {
        float capPx = clamp(di.screenSize.y * MCR_SHADOW_LENGTH_CAP_FRAC, MCR_SHADOW_LENGTH_CAP_MIN_PX, MCR_SHADOW_LENGTH_CAP_MAX_PX);
        float lengthScale = MCR_SoftCapScale(lengthPx, capPx);
        r.maxDist *= lengthScale;
        lengthPx *= lengthScale;
    }
    float pixelWeight = MCR_SubPixelFade(lengthPx);
    // Facing away from the light or sub-pixel: no rays (the loop ends at once; backShade was already evaluated). One
    // exit point only: early returns made the d3d11 compiler warn about a potentially uninitialized return value.
    if(normalWeight <= 0.0 || pixelWeight <= 0.0 || grazingWeight <= 0.0) rayCount = 0;
    // Gradual hit: a ray that only just passes behind an occluder (the rim of the shadow) is shaded a little, one that
    // goes deeper behind it fully. The depth over which it rises grows with the shadow length, so a long shadow gets
    // a soft rim instead of a hard, stepped one; never narrower than the thickness.
    r.edge = r.thickness + r.maxDist * MCR_SHADOW_EDGE_FRAC;
    int stepCount = (int)clamp(ceil(lengthPx / 4.0), (float)baseSteps, (float)maxSteps);
    // Steps still longer than ~3 px: offset them per pixel so thin occluders do not repeat as layers.
    float stepPx = lengthPx / (float)stepCount;
    float jitterAmount = max(saturate(_MCR_ShadowJitter), saturate((stepPx - 3.0) / 6.0) * 0.6);
    float noise = MCR_IGN(s.centerCS);
    float stepShift = mad(noise, jitterAmount, -0.5 * jitterAmount);   // -0.5..0.5 of the jitter amount

    // Ray origin: this surface, pushed off along the smooth normal (removes self shadowing on polygon edges).
    r.surfaceVS = s.positionVS;
    r.originVS = r.surfaceVS + s.nView * (_MCR_ShadowOffset.y + s.depth * 0.0005);
    // Projection of the origin can differ by a fraction of a pixel from SV_Position; correct it.
    r.pxCorrection = s.centerCS - MCR_ViewToEyePixel(r.surfaceVS, di);

    float3 tangent;
    float3 bitangent;
    MCR_OrthoBasis(lightVS, tangent, bitangent);
    float coneTan = tan(radians(clamp(_MCR_ShadowSoftness, 0.0, 30.0)));

    float total = 0.0;
    [loop]
    for(int ray = 0; ray < 4; ray++)
    {
        if(ray >= rayCount) break;
        // Soft shadow: with more than one ray the directions sit on a ring inside the light cone, rotated per pixel.
        float2 ring = 0.0;
        if(rayCount > 1)
        {
            sincos(((float)ray + noise) * (MCR_TWO_PI / (float)rayCount), ring.y, ring.x);
            ring *= coneTan * (0.55 + 0.35 * frac((float)ray * MCR_GOLDEN_RATIO + stepShift));
        }
        r.dirVS = normalize(lightVS + tangent * ring.x + bitangent * ring.y);
        r.clip = MCR_MakeClipRay(r.originVS, r.dirVS * r.maxDist);

        MCRDepthCache cache = (MCRDepthCache)0;
        float hit = 0.0;
        float prevT = 0.0;
        float entry = -1.0;     // where the ray enters the nearest occluder (< 0: not found yet)
        [loop]
        for(int i = 0; i < 48; i++)
        {
            if(i >= stepCount) break;
            float t = saturate(((float)i + 0.5 + stepShift) / (float)stepCount);
            bool valid;
            float h = MCR_ShadowBlock(r, s, di, t, cache, valid);
            if(!valid) break;

            [branch]
            if(h > 0.5 && entry < 0.0)
            {
                // First solid hit = nearest occluder. Find where the ray enters it (3 halvings) so the length fade
                // is continuous instead of jumping from step to step (the layered look of long shadows).
                float lo = prevT;
                float hi = t;
                [unroll]
                for(int k = 0; k < 3; k++)
                {
                    if(k >= refineSteps) break;
                    float mid = (lo + hi) * 0.5;
                    bool midValid;
                    if(MCR_ShadowBlock(r, s, di, mid, cache, midValid) > 0.5 && midValid) hi = mid;
                    else lo = mid;
                }
                entry = hi;
            }
            // Fade at the end of the ray, measured where the occluder begins once that is known.
            hit = max(hit, h * (1.0 - smoothstep(0.65, 1.0, entry >= 0.0 ? entry : t)));
            // Fully behind the occluder: nothing darker can follow. A partial hit marches on, the ray may go deeper.
            if(h > 0.98) break;
            prevT = t;
        }
        total += hit;
    }

    float shadow = total / (float)max(rayCount, 1);
    // Surfaces facing away from the light are already in the back shade.
    shadow *= normalWeight * grazingWeight;
    // Shadow shorter than ~1px only flickers.
    return saturate(shadow) * pixelWeight;
}

//------------------------------------------------------------------------------------------------------------------------------
// Helpers

// Mask texture value -> multiplier. amount 0 ignores the mask (always 1), amount 1 uses the texel as is.
// One multiply-add: value * amount + (1 - amount).
float MCR_MaskFactor(float value, float amount)
{
    float a = saturate(amount);
    return mad(saturate(value), a, 1.0 - a);
}

// Darkening layer. mode 0 scales the color by shade (keeps the hue of the base, default); any other mode takes
// away what shade is missing from white (also darkens very bright / saturated colors evenly). amount moves the
// base toward the fully applied color. The mode is a material constant, so the select costs no divergence.
float3 MCR_Composite(float3 base, float3 shade, float amount, float mode)
{
    float3 full = mode < 0.5 ? base * shade : base + shade - 1.0;
    return max(lerp(base, full, saturate(amount)), 0.0);
}

// Darkness (1 - luminance) of the default occlusion tint (0.37 gray): Albedo Tint is fully applied from here on.
#define MCR_TINT_REF_DARK 0.63

// Hue of the base color at luminance 1 (saturation raised), used to color the occlusion like light bounced
// inside a crease of the same material.
float3 MCR_AlbedoChroma(float3 albedo)
{
    float3 lumW = float3(0.2126, 0.7152, 0.0722);
    float lum = dot(albedo, lumW);
    float3 c = max(lerp(lum.xxx, albedo, 1.6), 0.0);
    c /= max(dot(c, lumW), 0.02);
    return clamp(c, 0.0, 2.0);
}

//------------------------------------------------------------------------------------------------------------------------------
// Uzumore: when a camera goes inside the mesh (a face buried in hair or a chest, a hand through the head), the surface it
// went through is pushed back in front of that camera, so the viewer sees the surface instead of the inside of the mesh.
// Same idea as UzumoreShader by sigmal00 (MIT); this implementation is Macaron's own:
//   - Per camera, in the vertex stage: a vertex that faces the camera and lies closer than the landing plane is moved
//     along the camera's forward axis. Within Reach it lands on a thin band right past the near plane; the farther the
//     camera went past it, the closer it lands, so nested layers (clothes over skin) keep their order. Layer Offset adds a
//     per-material gap on top (outer layers small, inner layers larger). Beyond Reach a vertex only moves by Reach and
//     stays behind the camera, so the far side of the body never covers the view.
//   - The band starts past lilToon's Clipping Canceller range (near x 1.01), which would otherwise squash the depths of
//     every layer together (Z-fighting).
//   - Back Side Reject uses the avatar axis written by the Macaron tools (_MCR_AvatarBasisZ = front in object space): the
//     back of the body is not pushed into the view of a player standing right behind the avatar.
//   - Local Only (mcr_local_only.hlsl): other players' clients never push anything back.
//   - Own view: _MCR_UzuIsLocal is 1 only on the wearer's client, set by an IsLocal layer of the Macaron Light Adjuster.
//     Unless "In Own View" (_MCR_UzuSelf) is on, nothing is pushed for the wearer's own camera (no neck in the bottom of
//     the view when looking down). Without the Light Adjuster the property stays 0 and every camera is handled alike.
//   - Only the color passes are changed (see MCR_UZU_COMPILED): shadow maps, the camera depth texture, the lilMCR PCSS
//     pass, orthographic cameras (World Shadow capture, UI) and mirrors keep the real mesh.
//   - Outline passes drop the triangles of pushed vertices (NaN position): a pushed outline shell would stick out toward
//     the camera. Fragments close to the camera are darkened by Brightness and Macaron's occlusion / contact shadow fades
//     out there (per-pixel view depth, see Macaron_UzumoreFS).

static float mcrUzuFade = 0.0;      // fragment stage: 0..1, how much Macaron's occlusion is faded out here

// Uniform per draw and camera.
bool MCR_UzuActive()
{
    if(_MCR_UzuEnable < 0.5 || _MCR_UzuAmount <= 0.0) return false;
    if(!Macaron_LocalVisible()) return false;                       // Local Only: another player's client
    if(_MCR_UzuIsLocal > 0.5 && _MCR_UzuSelf < 0.5) return false;   // the wearer's own view (Light Adjuster IsLocal layer)
    if(abs(LIL_MATRIX_P._m33) > 0.5) return false;     // orthographic
    if(LIL_IS_MIRROR) return false;
    return true;
}

// Start of the landing band: past the Clipping Canceller range of lilToon (near x 1.01).
float MCR_UzuLandNear()
{
    return _ProjectionParams.y * 1.02;
}

void Macaron_UzumoreOS(float3 normalOS, float2 uvMain, inout float4 positionOS)
{
#if MCR_UZU_COMPILED
    [branch]
    if(!MCR_UzuActive()) return;

    float3 positionWS = lilTransformOStoWS(positionOS.xyz);
    // View depth (= SV_Position.w) and the camera's forward axis in world space (per eye in VR).
    float depth = -mul(LIL_MATRIX_V, float4(positionWS, 1.0)).z;
    float3 forward = -LIL_MATRIX_V._m20_m21_m22;
    forward *= rsqrt(max(dot(forward, forward), MCR_EPS));

    float landNear = MCR_UzuLandNear();
    float bias = max(_MCR_UzuBias, 0.0);
    float need = landNear + bias - depth;       // > 0: closer than the landing band (or behind the camera)
    [branch]
    if(need <= 0.0) return;

    // Only the side that faces the camera: the surface the camera went through, not the inside of the far wall.
    float3 normalWS = lilTransformDirOStoWS(normalOS, true);
    float weight = smoothstep(0.1, 0.3, -dot(normalWS, forward));
    // Back Side Reject: normals pointing to the back of the avatar.
    float3 front = _MCR_AvatarBasisZ.xyz;
    float frontLen2 = dot(front, front);
    float normalLen2 = dot(normalOS, normalOS);
    if(frontLen2 > MCR_EPS && normalLen2 > MCR_EPS)
    {
        float back = dot(normalOS, front) * rsqrt(frontLen2 * normalLen2);
        weight *= lerp(1.0, smoothstep(-0.7, -0.2, back), saturate(_MCR_UzuBackReject));
    }
    // Mask (main UV, like every Macaron mask). Not read at all when its amount is 0 (uniform).
    if(_MCR_UzuMaskAmount > 0.0)
    {
        weight *= MCR_MaskFactor(LIL_SAMPLE_2D_LOD(_MCR_UzuMask, lil_sampler_linear_repeat, uvMain, 0).r, _MCR_UzuMaskAmount);
    }

    float reach = max(_MCR_UzuAmount, 0.0) * weight;
    [branch]
    if(reach <= MCR_TINY) return;

    // Within Reach: land between landNear (went deepest) and landNear + bias (barely touched).
    // Beyond Reach: move by Reach only (continuous at need == reach), the vertex stays behind the camera.
    float landed = need <= reach ? landNear + bias * (1.0 - need / reach) : depth + reach - bias;
    float move = max(landed - depth, 0.0);
    [branch]
    if(move <= 0.0) return;
    positionOS.xyz = lilTransformWStoOS(positionWS + forward * move);

    #if defined(LIL_OUTLINE) && !defined(LIL_PASS_SHADOWCASTER)
        // NaN removes every triangle that uses this vertex.
        positionOS = asfloat(0x7fc00000u).xxxx;
    #endif
#endif
}

// Per-pixel, by the view depth only (smooth on screen: anything per vertex or per triangle showed the mesh as steps):
//   Darkening: full on the band right past the near plane, fading out over 2 cm behind it (Brightness).
//   Occlusion: the camera depth texture holds the real mesh, not the pushed one, so Macaron's occlusion / contact shadow
//   is faded out from the band to 5 cm behind it.
// A surface that close to the camera without being pushed is darkened the same way, like being buried in it.
#define MCR_UZU_DARK_RANGE 0.02
#define MCR_UZU_OCC_RANGE 0.05
void Macaron_UzumoreFS(inout lilFragData fd)
{
#if MCR_UZU_COMPILED && !defined(LIL_OUTLINE)
    mcrUzuFade = 0.0;
    [branch]
    if(!MCR_UzuActive()) return;
    float bandEnd = MCR_UzuLandNear() + max(_MCR_UzuBias, 0.0);
    float depth = fd.positionCS.w;
    float dark = 1.0 - smoothstep(bandEnd, bandEnd + MCR_UZU_DARK_RANGE, depth);
    mcrUzuFade = 1.0 - smoothstep(bandEnd, bandEnd + MCR_UZU_OCC_RANGE, depth);
    fd.col.rgb *= lerp(1.0, saturate(_MCR_UzuBrightness), dark);
#endif
}

//------------------------------------------------------------------------------------------------------------------------------
// Reflection range: lilToon adds reflection, MatCap, rim light and glitter between BEFORE_REFLECTION / BEFORE_MATCAP
// and BEFORE_EMISSION_1ST. The color before and after that range gives the added light, which Occlusion on
// Reflections removes inside occluded areas (a crease does not see the environment either).

// Lightweight switch of the avatar menu (Light Adjuster, 1 = on). Off turns off every lilMCR rendering feature
// (occlusion, contact shadow and any later feature). The light adjustments (brightness, fixed brightness, light color,
// temperature, saturation, light direction) are cheap and stay active.
bool MCR_MenuActive()
{
    return _MCR_AdjActive > 0.5;
}

static float3 mcrSpecStart = 0.0;
static float3 mcrSpecEnd = 0.0;
static int mcrSpecState = 0;     // 0 not reached, 1 inside, 2 done

void Macaron_MarkSpecStart(inout lilFragData fd)
{
#if !defined(LIL_LITE) && !defined(LIL_OUTLINE) && !defined(LIL_PASS_SHADOWCASTER) && !defined(LIL_PASS_META)
    if(mcrSpecState == 0)
    {
        mcrSpecStart = fd.col.rgb;
        mcrSpecState = 1;
    }
#endif
}

void Macaron_MarkSpecEnd(inout lilFragData fd)
{
#if !defined(LIL_LITE) && !defined(LIL_OUTLINE) && !defined(LIL_PASS_SHADOWCASTER) && !defined(LIL_PASS_META)
    if(mcrSpecState == 1)
    {
        mcrSpecEnd = fd.col.rgb;
        mcrSpecState = 2;
    }
#endif
}

//------------------------------------------------------------------------------------------------------------------------------
// Skin: fine surface detail, a soft highlight and a warm glow along the shadow edge, drawn by lilMCR on top of lilToon's
// result. No lilToon feature or texture slot is used, so the material's own settings are left as they are.
// Called from BEFORE_EMISSION_1ST (see custom.hlsl): after lilToon's lighting, reflection, MatCap and rim light, and
// inside the reflection range, so Occlusion on Reflections also removes the highlight inside creases.
//
// _MCR_SkinTex: RG = tangent-space normal (x, y), B = cavity (1 = skin line / pore), A = color variation around 0.5
// (below: mottling, darker; above: capillaries, redder).
// It is read twice: once at Tiling for the fine detail and once wider and turned, which hides the repetition of the
// fine read and carries the mottling.

// Wide read: scale and a fixed turn (cos, sin) relative to the fine read.
#define MCR_SKIN_WIDE_SCALE 0.31
#define MCR_SKIN_WIDE_COS 0.8
#define MCR_SKIN_WIDE_SIN 0.6
// Reflectance of skin at normal incidence.
#define MCR_SKIN_F0 0.028
// Capillaries: what is left of the base color where one runs (a red tint).
#define MCR_SKIN_VESSEL_TINT float3(1.0, 0.55, 0.55)
// Width of the glow along the shadow edge, as a range of N.L around 0.
#define MCR_SKIN_EDGE_WIDTH 0.38

void Macaron_Skin(inout lilFragData fd)
{
#if !defined(LIL_LITE) && !defined(LIL_GEM) && !defined(LIL_FUR) && !defined(LIL_OUTLINE) && !defined(LIL_PASS_FORWARDADD) && !defined(LIL_PASS_SHADOWCASTER) && !defined(LIL_PASS_META) && !defined(LIL_PASS_DEPTHONLY) && !defined(LIL_PASS_DEPTHNORMALS) && !defined(LIL_PASS_MOTIONVECTORS)
    // Uniform per draw (material values, pass): returning here keeps the implicit-gradient texture reads below valid.
    if(!(_MCR_SkinEnable > 0.5 && _MCR_Active > 0.5 && MCR_MenuActive() && Macaron_LocalVisible())) return;
    if(Macaron_IsWorldCapture()) return;

    float2 uvFine = fd.uvMain * max(_MCR_SkinTiling, 1.0);
    float2 uvWide = float2(uvFine.x * MCR_SKIN_WIDE_COS - uvFine.y * MCR_SKIN_WIDE_SIN, uvFine.x * MCR_SKIN_WIDE_SIN + uvFine.y * MCR_SKIN_WIDE_COS) * MCR_SKIN_WIDE_SCALE + 0.37;
    float4 fine = LIL_SAMPLE_2D(_MCR_SkinTex, lil_sampler_linear_repeat, uvFine);
    float4 wide = LIL_SAMPLE_2D(_MCR_SkinTex, lil_sampler_linear_repeat, uvWide);
    float mask = 1.0;
    if(_MCR_SkinMaskAmount > 0.0)
    {
        mask = MCR_MaskFactor(LIL_SAMPLE_2D(_MCR_SkinMask, lil_sampler_linear_repeat, fd.uvMain).r, _MCR_SkinMaskAmount);
    }

    // Surface frame of the main UV from screen derivatives (no vertex tangent needed, mirrored UVs included).
    float3 N = fd.N;
    float3 dpx = ddx(fd.positionWS);
    float3 dpy = ddy(fd.positionWS);
    float2 dux = ddx(fd.uvMain);
    float2 duy = ddy(fd.uvMain);
    float3 px = cross(dpy, N);
    float3 py = cross(N, dpx);
    float3 T = px * dux.x + py * duy.x;
    float3 B = px * dux.y + py * duy.y;
    float frameScale = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-20));
    T *= frameScale;
    B *= frameScale;

    #if defined(LIL_RENDER) && LIL_RENDER == 2
        mask *= saturate(fd.col.a);
    #endif
    // Fades with the rest of lilMCR (Fade Distance, total Amount).
    mask *= MCR_FadeOut(fd.positionCS.w) * saturate(_MCR_Amount);
    [branch]
    if(mask <= 0.0) return;

    // Detail normal: the fine read plus a part of the wide one (turned back into the fine read's frame).
    float2 w = wide.xy * 2.0 - 1.0;
    float2 slope = (fine.xy * 2.0 - 1.0) + float2(w.x * MCR_SKIN_WIDE_COS + w.y * MCR_SKIN_WIDE_SIN, w.y * MCR_SKIN_WIDE_COS - w.x * MCR_SKIN_WIDE_SIN) * 0.3;
    float detail = max(_MCR_SkinDetail, 0.0) * mask;
    float3 Nd = normalize(N + (T * slope.x + B * slope.y) * detail);
    float cavity = fine.b * saturate(detail);
    float variation = wide.a * 2.0 - 1.0;
    float mottle = saturate(-variation);
    float vessel = saturate(variation);

    float3 L = fd.L;
    float3 V = fd.V;
    float nl = dot(N, L);
    float3 light = fd.lightColor;
    float3 colIn = fd.col.rgb;
    float3 col = colIn;

    // 1. Surface color: mottling, capillaries, darker skin lines, and the relief of the detail normal against the light.
    float variationAmount = saturate(_MCR_SkinMottle) * mask;
    col *= lerp(1.0.xxx, _MCR_SkinMottleColor.rgb, saturate(mottle * _MCR_SkinMottleColor.a) * variationAmount);
    col *= lerp(1.0.xxx, MCR_SKIN_VESSEL_TINT, vessel * variationAmount);
    col *= 1.0 - cavity * 0.10;
    col *= clamp(1.0 + (dot(Nd, L) - nl) * 0.9, 0.6, 1.4);

    // 2. Light passing through the skin: a warm band along the shadow edge, and a glow on thin edges seen against
    //    the light. Both use the base color, so they stay inside the material's own palette.
    float scatter = saturate(_MCR_SkinScatter) * saturate(_MCR_SkinScatterColor.a) * mask;
    float edge = nl + 0.08;
    float band = exp(-(edge * edge) / (MCR_SKIN_EDGE_WIDTH * MCR_SKIN_EDGE_WIDTH));
    float through = pow(saturate(dot(V, -L)), 3.0) * pow(1.0 - saturate(dot(N, V)), 2.0);
    col += fd.albedo * _MCR_SkinScatterColor.rgb * light * (band * 0.35 + through * 0.6) * scatter;

    // Reflection range: what was changed so far is surface color, not reflected light.
    if(mcrSpecState == 1) mcrSpecStart += col - colIn;

    // 3. Highlight: one GGX lobe on the detail normal, rougher inside the grooves, plus a soft sheen at grazing angles.
    float3 H = normalize(L + V);
    float nh = saturate(dot(Nd, H));
    float lh = saturate(dot(L, H));
    float nlD = saturate(dot(Nd, L));
    float nvD = saturate(dot(Nd, V));
    float rough = saturate(lerp(0.8, 0.42, saturate(_MCR_SkinSmoothness)) + cavity * 0.3);
    float a = rough * rough;
    float a2 = a * a;
    float d = nh * nh * (a2 - 1.0) + 1.0;
    float lobe = a2 / (d * d * max(lh * lh, 0.1) * (a * 4.0 + 2.0));
    // In lilToon's shadow the main light does not reach the surface (shadowmix: 1 lit, 0 shadow).
    float lit = lerp(0.15, 1.0, saturate(fd.shadowmix));
    float3 highlight = light * (min(lobe, 16.0) * MCR_SKIN_F0 * nlD * lit * max(_MCR_SkinSpecular, 0.0));
    float fresnel = pow(1.0 - nvD, 4.0);
    highlight += light * (fresnel * saturate(_MCR_SkinSheen) * 0.2 * lerp(0.4, 1.0, saturate(nl * 0.5 + 0.5)));
    col += highlight * (1.0 - cavity * 0.5) * mask;

    fd.col.rgb = col;
#endif
}

//------------------------------------------------------------------------------------------------------------------------------
// Entry point (called from BEFORE_FOG, see custom.hlsl)

void Macaron_Apply(inout lilFragData fd)
{
#if defined(LIL_OUTLINE) || defined(LIL_FUR) || defined(LIL_PASS_SHADOWCASTER) || defined(LIL_PASS_META) || defined(LIL_PASS_DEPTHONLY) || defined(LIL_PASS_DEPTHNORMALS) || defined(LIL_PASS_MOTIONVECTORS) || defined(LIL_PASS_FORWARDADD)
    // Outline: lilToon does not compute the normal data in the outline pass, and the outline shell is
    // not in the depth texture. Skipped.
    // Fur (whole Fur SubShader: body pass and shells): occlusion and contact shadow are not compiled at all, to keep
    // fur materials cheap. The Light Adjuster still applies.
    // Pixel light pass (BRP ForwardAdd): BEFORE_FOG runs again for every pixel light, which would compute the whole
    // occlusion / contact shadow once more per light, only to darken the light that light adds. Skipped: the base pass
    // carries the effect.
#else
    // Capture writes packed data, so AO/contact shading is unnecessary.
    if(Macaron_IsWorldCapture()) return;
    // Orthographic cameras (UI / photo tools): the screen-space depth math assumes a perspective projection.
    if(abs(LIL_MATRIX_P._m33) > 0.5) return;
    // Early outs on material values and on the pass are uniform per draw call: they do not break the
    // implicit-gradient texture sampling below. Per-pixel early outs come only after the sampling.
    // Material switches and the avatar menu's lightweight switch (Light Adjuster, 1 = on). Off skips the whole effect.
    // The switches are also folded into useOcc / useShadow, which guard the depth reads and the sampling loops with
    // real branches, so nothing heavy runs even if the early return were flattened.
    // Local Only (mcr_local_only.hlsl): nothing on another player's client.
    float amount = MCR_MenuActive() && _MCR_Active > 0.5 && Macaron_LocalVisible() ? saturate(_MCR_Amount) : 0.0;
    bool useOcc = amount > 0.0 && _MCR_OccEnable > 0.5 && _MCR_OccAmount > 0.0;
    bool useShadow = amount > 0.0 && _MCR_ShadowEnable > 0.5 && _MCR_ShadowIntensity > 0.0;
    if(!(useOcc || useShadow)) return;
    int method = (int)round(_MCR_OccMethod);   // 0 SSAO, 1 Ray March, 2 GTAO, 3 Soft SSAO

    // Masks are sampled before any per-pixel early return (implicit gradients), and only for the features in use
    // (a mask with amount 0 is not read at all; the condition is per material, so it is uniform).
    float globalMask = 1.0;
    float occMask = 1.0;
    float shadowMask = 1.0;
    if(_MCR_GlobalMaskAmount > 0.0)
    {
        float2 globalUV = fd.uvMain * _MCR_GlobalMask_ST.xy + _MCR_GlobalMask_ST.zw;
        globalMask = MCR_MaskFactor(LIL_SAMPLE_2D(_MCR_GlobalMask, lil_sampler_linear_repeat, globalUV).r, _MCR_GlobalMaskAmount);
    }
    if(useOcc && _MCR_OccMaskAmount > 0.0)
    {
        float m = LIL_SAMPLE_2D(_MCR_OccMask, lil_sampler_linear_repeat, fd.uvMain).r;
        occMask = MCR_MaskFactor(_MCR_OccMaskFlip > 0.5 ? 1.0 - m : m, _MCR_OccMaskAmount);
    }
    if(useShadow && _MCR_ShadowMaskAmount > 0.0)
    {
        shadowMask = MCR_MaskFactor(LIL_SAMPLE_2D(_MCR_ShadowMask, lil_sampler_linear_repeat, fd.uvMain).r, _MCR_ShadowMaskAmount);
    }

    // Triangle plane for the SSAO facet floor: screen derivatives, taken here while the control flow is still uniform.
    float3 facetWS = cross(ddy(fd.positionWS), ddx(fd.positionWS));

    // Everything past the far end of Fade Distance or fully masked out is skipped (MCR_FadeOut is exactly 0
    // there). The view depth of a visible fragment is SV_Position.w.
    // Uzumore (Macaron_UzumoreFS): faded out close to the camera, the depth texture there is not the pushed surface.
    float visibility = MCR_FadeOut(fd.positionCS.w) * MCR_NearFade(fd.positionCS.w) * globalMask * amount * (1.0 - mcrUzuFade);
    bool alive = visibility > 0.0;
#if defined(LIL_RENDER) && LIL_RENDER == 2
    // Masks have already been sampled, preserving their derivatives.
    alive = alive && fd.col.a > 0.0;
#endif
    // All implicit-gradient mask reads are complete before these per-pixel branches.
    useOcc = useOcc && occMask > 0.0 && alive;
    useShadow = useShadow && shadowMask > 0.0 && alive;
#if !MCR_QUAD_SHARE
    [branch]
    if(!(useOcc || useShadow)) return;
#endif
    // Quad share build: no per-pixel return until the quad exchange below. Pixels with nothing to do skip the heavy
    // work through the branches and only take part in the exchange.

    MCRDepthInfo di = MCR_GetDepthInfo();

    float occ = 0.0;
    float occDistance = 0.0;     // 0 = near tint, 1 = far tint
    float castShadow = 0.0;
    float backShade = 0.0;
    float visibleFactor = 1.0;

    bool rayMethod = method == 1;
    bool gtaoMethod = method == 2;
    float2 quadSign;
    MCRRaySet raySet;
    raySet.quadIndex = MCR_QuadIndex(fd.positionCS.xy, quadSign);
    // Uniform per draw: the quad share build, Ray March and the material's Quality setting (not the per-pixel
    // distance LOD, so the 4 pixels of a quad always agree). Best / Fine / Standard (no 2x2): off, Standard: 1/2, Light: 1/4.
    int quadQuality = MCR_OccQualitySetting();
    raySet.quadSet = MCR_QUAD_SHARE && rayMethod && quadQuality <= 1;
    raySet.quadDiv = quadQuality == 0 ? 4 : 2;
    raySet.share = false;
    raySet.quadRotation = MCR_IGN(floor(fd.positionCS.xy * 0.5)) * saturate(_MCR_OccNoise);
    raySet.rays = 0;
    raySet.steps = 0;

    //--------------------------------------------------------------------------------------------------------------------------
    // 1. Per-pixel setup (cheap): surface, Alpha Aware, Ray March reach / weight / budget.
    // No depth texture -> effect off (no fake darkening that could turn the avatar black).
    bool active = di.present && (useOcc || useShadow);
    MCRSurface s = (MCRSurface)0;
    bool covered = true;
    float reach = 0.0;
    float rayWeight = 0.0;       // mask x sub-pixel fade; > 0 = this pixel marches
    float rayVRComp = 1.0;       // VR step compensation of this pixel's Ray March budget (1 on desktop)
    bool partner = false;        // quad set: this pixel is really visible (may share with its neighbors)
    [branch]
    if(active)
    {
        s = MCR_GetSurface(fd, di);
        MCR_SetFacetNormal(s, facetWS);

        // Alpha Aware (transparent only): a transparent surface is not written to the depth texture, which instead
        // holds whatever opaque geometry is there. Where that geometry is closer to the camera than this surface,
        // the effect is faded out over a few millimeters instead of switching off at one depth.
        #if defined(LIL_RENDER) && LIL_RENDER == 2
            if(_MCR_AlphaAware > 0.5)
            {
                float tolerance = 0.001 + s.depth * 0.001;
                visibleFactor = 1.0 - smoothstep(tolerance, tolerance * 3.0, s.depth - MCR_ReadDepth(s.centerCS, di));
            }
        #endif
        covered = visibleFactor <= 0.0;
        // Hidden fragment (opaque / cutout): something closer is already in the depth texture at this pixel, so this
        // fragment will be drawn over (or was only shaded because it came before its occluder in the draw order).
        // One depth read instead of the whole occlusion / contact shadow.
        // Tolerance: the depth texture can hold this very surface a few millimeters closer than this pass draws it
        // (see the self calibration of the contact shadow, which accepts up to 1 cm). Anything within 1 cm + 1% of the
        // depth counts as this surface, so a visible surface is never skipped; layers closer together than that
        // are simply not skipped.
        // A material that is not in the depth texture reads what is behind it and is never skipped.
        #if !(defined(LIL_RENDER) && LIL_RENDER == 2)
            float centerScene = MCR_ReadDepth(s.centerCS, di);
            bool selfVisible = s.depth - centerScene < 0.01 + 0.01 * s.depth;
            covered = covered || !selfVisible;
        #endif

        [branch]
        if(useOcc && !covered && rayMethod)
        {
            float reachFull = MCR_OccReachMeters();
            reach = MCR_OccCapReach(reachFull, s.viewDepth, di);
            rayWeight = MCR_SubPixelFade(reach * di.focalPx / s.viewDepth) * occMask;
            int qualityDrop = 0;
            // Without the quad share, a reach shrunk by the near cap also drops one quality level: the effect is
            // large and soft on screen there, so fewer rays are hard to see.
            if(!raySet.quadSet && reach < reachFull * 0.9) qualityDrop = max(qualityDrop, 1);
            MCR_OccBudget(s.viewDepth, reach * di.focalPx / max(s.viewDepth, 0.05), qualityDrop, raySet.rays, raySet.steps);
            #if MCR_VR_LEVELS
                rayVRComp = MCR_OccVRComp(s.viewDepth, reach * di.focalPx / max(s.viewDepth, 0.05), qualityDrop, raySet.steps);
            #endif
            partner = raySet.quadSet && rayWeight > 0.0;
            #if !(defined(LIL_RENDER) && LIL_RENDER == 2)
                // Opaque / cutout write their own depth: a pixel whose depth texture disagrees is not what the
                // neighbors see (helper lane outside the triangle, another surface in front).
                [branch]
                if(partner)
                {
                    partner = abs(centerScene - s.depth) < MCR_QUAD_VISIBLE_BASE + MCR_QUAD_VISIBLE_REL * s.depth;
                }
            #endif
        }
    }

#if MCR_QUAD_SHARE
    //--------------------------------------------------------------------------------------------------------------------------
    // 2. Partner exchange (uniform control flow): share only when all 3 neighbors are partners.
    // raySet.quadSet depends only on material constants (uniform per draw); skip both exchanges when no quad set is used.
    [branch] if(raySet.quadSet)
    {
        float4 meGeo = float4(s.nView, partner ? s.depth : -1.0);
        float meSize = (float)MCR_RaySetSize(raySet);
        float4 gx, gy, gd;
        MCR_QuadExchange(meGeo, quadSign, gx, gy, gd);
        float4 sx, sy, sd;
        MCR_QuadExchange(float4(meSize, 0.0, 0.0, 0.0), quadSign, sx, sy, sd);
        raySet.share = partner
            && MCR_QuadPartner(gx, sx.x, meGeo, meSize)
            && MCR_QuadPartner(gy, sy.x, meGeo, meSize)
            && MCR_QuadPartner(gd, sd.x, meGeo, meSize);
    }
#endif

    //--------------------------------------------------------------------------------------------------------------------------
    // 3. Heavy work, per pixel.
    float3 raySums = float3(0.0, 0.0, 0.0);      // everything this pixel marched
    float3 ownSums = float3(0.0, 0.0, 0.0);      // only its own directions of the quad set
    [branch]
    if(rayWeight > 0.0)
    {
        raySums = MCR_RayMarchSums(s, di, reach, raySet, ownSums);
    }

    [branch]
    if(active && useOcc && !covered && method == 0)
    {
        occ = MCR_CalcSSAO(s, di) * occMask;
    }

    [branch]
    if(active && useOcc && !covered && method == 3)
    {
        occ = MCR_CalcSoftAO(s, di) * occMask;
    }

    [branch]
    if(active && useOcc && !covered && gtaoMethod)
    {
        float gtaoReach = MCR_OccCapReach(MCR_GTAOReachMeters(), s.viewDepth, di);
        float gtaoWeight = MCR_SubPixelFade(gtaoReach * di.focalPx / s.viewDepth) * occMask;
        [branch]
        if(gtaoWeight > 0.0)
        {
            float2 g = MCR_CalcGTAO(s, di, gtaoReach, gtaoWeight);
            occ = g.x;
            occDistance = g.y;
        }
    }

    [branch]
    if(active && useShadow && !covered)
    {
        castShadow = MCR_CalcContactShadow(s, di, backShade);
    }

#if MCR_QUAD_SHARE
    //--------------------------------------------------------------------------------------------------------------------------
    // 4. Sum exchange (uniform control flow). A sharing pixel adds the own directions of its 3 partners with tent
    //    weights (itself 1, side neighbors 1/2, diagonal 1/4): every subset is an unbiased estimate on its own, and
    //    weighting itself higher keeps the 4 pixels of a quad different instead of one flat 2x2 block. A fallback
    //    pixel keeps its own result. Neighbors only ever hand over their own directions.
    [branch] if(raySet.quadSet)
    {
        float4 ox, oy, od;
        MCR_QuadExchange(float4(ownSums, 0.0), quadSign, ox, oy, od);
        if(raySet.share) raySums = ownSums + (ox.xyz + oy.xyz) * 0.5 + od.xyz * 0.25;
    }
    [branch]
    if(!(useOcc || useShadow)) return;
#endif
    if(rayWeight > 0.0)
    {
        float2 r = MCR_RayFinalize(raySums, rayWeight * rayVRComp);
        occ = r.x;
        occDistance = r.y;
    }

    float shadow = saturate(max(castShadow, backShade * saturate(_MCR_ShadowBackShade)) * _MCR_ShadowIntensity) * shadowMask;
    if(!useShadow) shadow = 0.0;

    // visibility already holds Fade Distance x Global Mask x Amount; add the Alpha Aware cover term and, on
    // transparent materials, the surface alpha (a half transparent surface receives half the darkening).
    float weight = visibility * visibleFactor;
    #if defined(LIL_RENDER) && LIL_RENDER == 2
        weight *= saturate(fd.col.a);
    #endif

    // Near / far tint and its alpha interpolated together as one float4.
    float4 tint = lerp(_MCR_OccTintNear, _MCR_OccTintFar, occDistance);
    // Albedo Tint follows how dark the tint is: full at the default gray (0.37) and darker, none at white, so a
    // white tint really means "no darkening" instead of the saturated base hue. Never brighter than the base.
    float tintDark = saturate((1.0 - dot(tint.rgb, float3(0.2126, 0.7152, 0.0722))) / MCR_TINT_REF_DARK);
    float chromaWeight = saturate(_MCR_OccAlbedoTint) * tintDark;
    tint.rgb = min(tint.rgb * lerp(1.0.xxx, MCR_AlbedoChroma(fd.albedo), chromaWeight), 1.0);
    occ *= weight;
    shadow *= weight;

    float3 baseCol = fd.col.rgb;
    float3 col = MCR_Composite(baseCol, tint.rgb, occ * saturate(tint.a), _MCR_OccBlend);
    col = MCR_Composite(col, _MCR_ShadowColor.rgb, shadow * saturate(_MCR_ShadowColor.a), 0.0);

    // Occlusion on Reflections: the darkening above already scales the reflected light like everything else;
    // this removes more of it in occluded areas, up to all of it at half occlusion.
    float reflectionAmount = saturate(_MCR_OccReflection) * saturate(occ * 2.0);
    if(mcrSpecState == 1) mcrSpecEnd = baseCol;
    if(mcrSpecState != 0 && reflectionAmount > 0.0)
    {
        float3 spec = max(mcrSpecEnd - mcrSpecStart, 0.0);
        float3 darken = col / max(baseCol, 0.0001.xxx);
        col = max(col - spec * darken * reflectionAmount, 0.0);
    }
    // Floor Brightness: never darker than this fraction of the original color (after every darkening step).
    col = max(col, baseCol * saturate(_MCR_FloorBrightness));
    fd.col.rgb = col;
#endif
}

//------------------------------------------------------------------------------------------------------------------------------
// Light Adjuster

// Light tint for a color temperature control: -1 = cool (about 10000K), 0 = neutral, +1 = warm (about 3000K).
// Normalized to luminance 1 so the control changes the hue of the light, not its brightness.
float3 MCR_TemperatureTint(float t)
{
    t = clamp(t, -1.0, 1.0);
    float3 warm = float3(1.0, 0.71, 0.42);
    float3 cool = float3(0.79, 0.86, 1.0);
    float3 tint = lerp(1.0.xxx, warm, saturate(t)) * lerp(1.0.xxx, cool, saturate(-t));
    return tint / max(dot(tint, float3(0.2126, 0.7152, 0.0722)), MCR_EPS);
}

// Light color influence, fixed brightness, brightness and color temperature: every light color lilToon uses for
// this fragment. Light color influence below 1 removes the hue of the world's lights (luminance is kept).
// Fixed brightness blends the world's lighting toward plain white light (1.0), so the avatar keeps the same
// brightness in dark and bright worlds; point / spot lights fade out with it. Brightness and color temperature
// are applied on top.
void Macaron_AdjustLightColor(inout lilFragData fd)
{
    // Local Only: another player's client keeps lilToon's own light colors.
    if(!Macaron_LocalVisible()) return;
    float colorAmount = saturate(_MCR_AdjLightColorInfluence);
    if(colorAmount < 1.0)
    {
        const float3 lumW = float3(0.2126, 0.7152, 0.0722);
        fd.lightColor = lerp(dot(fd.lightColor, lumW).xxx, fd.lightColor, colorAmount);
    #if !defined(LIL_PASS_FORWARDADD)
        fd.indLightColor = lerp(dot(fd.indLightColor, lumW).xxx, fd.indLightColor, colorAmount);
        fd.addLightColor = lerp(dot(fd.addLightColor, lumW).xxx, fd.addLightColor, colorAmount);
    #endif
    }

    float fixedAmount = saturate(_MCR_AdjFixedBrightness);
    if(fixedAmount > 0.0)
    {
    #if defined(LIL_PASS_FORWARDADD)
        fd.lightColor *= 1.0 - fixedAmount;
    #else
        fd.lightColor = lerp(fd.lightColor, 1.0.xxx, fixedAmount);
        fd.indLightColor = lerp(fd.indLightColor, 1.0.xxx, fixedAmount);
        fd.addLightColor *= 1.0 - fixedAmount;
    #endif
    }

    float3 k = MCR_TemperatureTint(_MCR_AdjTemperature) * max(_MCR_AdjBrightness, 0.0);
    fd.lightColor *= k;
    fd.indLightColor *= k;
    fd.addLightColor *= k;
}

void Macaron_Adjust(inout lilFragData fd)
{
#if !defined(LIL_OUTLINE) && !defined(LIL_PASS_SHADOWCASTER) && !defined(LIL_PASS_META)
    // Local Only: another player's client keeps lilToon's own color, lighting and light direction.
    if(!Macaron_LocalVisible()) return;
    // Saturation of the base color (before lighting, emission is not affected). fd.albedo == fd.col.rgb here.
    float sat = max(_MCR_AdjSaturation, 0.0);
    float lumA = dot(fd.albedo, float3(0.2126, 0.7152, 0.0722));
    float lumC = dot(fd.col.rgb, float3(0.2126, 0.7152, 0.0722));
    fd.albedo = max(lerp(lumA.xxx, fd.albedo, sat), 0.0);
    fd.col.rgb = max(lerp(lumC.xxx, fd.col.rgb, sat), 0.0);

    Macaron_AdjustLightColor(fd);

    #if !defined(LIL_PASS_FORWARDADD)
        // Fixed light direction for lilToon's shading (main light only; point / spot lights keep their own direction).
        // Avatar space as in MCR_LightDirView: x = right seen from the front, y = up, z = toward the front.
        float strength = saturate(_MCR_AdjLightStrength);
        if(strength > 0.0001)
        {
            float yaw = radians(_MCR_AdjLightYaw);
            float pitch = radians(clamp(_MCR_AdjLightPitch, -89.0, 89.0));
            float3 d = float3(sin(yaw) * cos(pitch), sin(pitch), cos(yaw) * cos(pitch));
            float3 dirOS = _MCR_AvatarBasisX.xyz * d.x + _MCR_AvatarBasisY.xyz * d.y + _MCR_AvatarBasisZ.xyz * d.z;
            if(dot(dirOS, dirOS) < MCR_EPS) dirOS = d;
            float3 dirWS = mul((float3x3)LIL_MATRIX_M, dirOS);
            dirWS = dirWS / max(length(dirWS), MCR_EPS);
            float3 L = lerp(fd.L, dirWS, strength);
            fd.L = dot(L, L) > MCR_EPS ? normalize(L) : dirWS;
            fd.ln = dot(fd.L, fd.N);
            // Terms lilToon derived from the light direction before this point (backlight uses hl).
            #if defined(LIL_V2F_POSITION_WS)
                fd.vl = dot(fd.V, fd.L);
                fd.hl = dot(fd.headV, fd.L);
            #endif
            // lilToon only lets the realtime shadow map through where fd.L is close to the real light (fd.origL).
            // With a fixed direction that closeness changes with the menu, so low-resolution shadow map texels
            // would appear and vanish in patches. The shadow map is faded out with the fixed direction instead.
            fd.attenuation = lerp(fd.attenuation, 1.0, saturate(strength * 4.0));
        }
    #endif
#endif
}

#endif
