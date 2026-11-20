#ifndef LILMCR_PCSS_INCLUDED
#define LILMCR_PCSS_INCLUDED
//------------------------------------------------------------------------------------------------------------------------------
// lilMCR PCSS - soft cast shadows from a dedicated spot light, as an extra multiply pass on top of an unmodified Macaron.
//
// Used only by the pass "LILMCR_PCSS_SHADOW" (Templates/lilmcr_pcss_pass.lilblock), which the add-on's generator inserts
// into copies of Macaron's shaders (lilCustomShaderDatas.lilblock: InsertPassPost / InsertUsePassPost). Macaron's own
// source files are untouched; generated ShadowCaster passes also receive the optional depth-packing hook.
//
//   - LightMode ForwardAdd, Blend DstColor Zero (multiply): it only darkens what Macaron already drew, so the shadow
//     follows any world's lighting, and when the light is missing (VRChat Safety, Shadows off) nothing changes.
//   - It draws only for this avatar's dedicated light; for every other light the vertex shader collapses the mesh
//     (no pixels, almost no cost).
//   - Independent of lilToon's "ForwardAdd shadows" setting (own multi_compile_fwdadd_fullshadows).
//
// Dedicated light check (per-draw values, done in the vertex shader):
//   1. brightest channel of _LightColor0 <= _lilMCRPCSS_MaxLevel (the light is black),
//   2. the light's cookie carries the lilMCR signature and this avatar's light ID (_lilMCRPCSS_LightId),
//   3. the light is within _lilMCRPCSS_Reach meters of this renderer's object origin.
//
// Shadow: blocker search + variable-size filter (PCSS, Fernando 2005); a dense texel-integrated filter as the smallest kernel.
//------------------------------------------------------------------------------------------------------------------------------

#include "lilmcr_pcss_depth.hlsl"
#include "lilmcr_pcss_depth_math.hlsl"
#if defined(LMP_WORLD_CAPTURE)
    #include "mcr_world_capture_match.hlsl"
#endif
#if defined(LMP_LOCAL_ONLY)
    #include "mcr_local_only.hlsl"
#endif
// _lilMCRPCSS_* are declared by lilToon (LIL_CUSTOM_PROPERTIES <- LMP_PCSS_PROPERTIES, lilmcr_pcss_depth.hlsl).

#define LMP_EPS                 0.000001
#define LMP_TINY                0.0001
#define LMP_MAX_UV              0.12

// On/off: the material is set up (_lilMCRPCSS_Enable, written by Setup), and Macaron's master switch and menu switch
// are on and Local Only allows it here (LMP_MASTER_SWITCH / LMP_MENU_SWITCH / LMP_LOCAL_ONLY, defined by the generator when Macaron has those properties).
float LMP_Weight()
{
    float on = _lilMCRPCSS_Enable;
#if defined(LMP_MASTER_SWITCH)
    on = min(on, LMP_MASTER_SWITCH);
#endif
#if defined(LMP_MENU_SWITCH)
    on = min(on, LMP_MENU_SWITCH);
#endif
#if defined(LMP_LOCAL_ONLY)
    // Macaron's Local Only: no shadow on another player's client.
    if(!Macaron_LocalVisible()) on = 0.0;
#endif
    return on > 0.5 ? saturate(_lilMCRPCSS_Strength) : 0.0;
}

#if defined(SPOT) && defined(SHADOWS_DEPTH) && defined(SHADOWS_NATIVE)
    #define LMP_ACTIVE

// Cookie of the dedicated light (must match LilMCRPcssSetup.cs): 32x32, 4x4 texel blocks in the four corners.
//   bottom-left = signature 0.25, bottom-right / top-left / top-right = ID digits d (0..7) stored as (d + 0.5) / 8.
#define LMP_UV_SIG              float2(0.0625, 0.0625)
#define LMP_UV_D2               float2(0.9375, 0.0625)
#define LMP_UV_D1               float2(0.0625, 0.9375)
#define LMP_UV_D0               float2(0.9375, 0.9375)
#define LMP_SIGNATURE           0.25
#define LMP_SIG_TOL             0.04
#define LMP_DIGIT_TOL           0.3

// One ID digit of the cookie; -100 when the texel is not on a digit level.
float LMP_Digit(float2 uv)
{
    float v = tex2Dlod(_LightTexture0, float4(uv, 0.0, 0.0)).w * 8.0 - 0.5;
    float d = round(v);
    return abs(v - d) < LMP_DIGIT_TOL ? d : -100.0;
}

bool LMP_IsKeyLight()
{
    if(LMP_Weight() <= 0.0) return false;

    float level = max(_LightColor0.r, max(_LightColor0.g, _LightColor0.b));
    if(level > _lilMCRPCSS_MaxLevel) return false;

    // Reject distant lights before the four vertex-stage cookie reads.
    float3 origin = float3(LIL_MATRIX_M._m03, LIL_MATRIX_M._m13, LIL_MATRIX_M._m23);
    float3 toLight = _WorldSpaceLightPos0.xyz - origin;
    if(dot(toLight, toLight) > _lilMCRPCSS_Reach * _lilMCRPCSS_Reach) return false;

    float sig = tex2Dlod(_LightTexture0, float4(LMP_UV_SIG, 0.0, 0.0)).w;
    if(abs(sig - LMP_SIGNATURE) > LMP_SIG_TOL) return false;

    float id = LMP_Digit(LMP_UV_D2) * 64.0 + LMP_Digit(LMP_UV_D1) * 8.0 + LMP_Digit(LMP_UV_D0);
    if(abs(id - _lilMCRPCSS_LightId) > 0.5) return false;

    return true;
}

// Linear form of the spot shadow projection, read from the matrix rows:
//   depth = A + B / w, w = proportional to the distance in front of the light (k = w per meter),
//   S = shadow-map uv per meter of world size at w = 1.
struct LMPProjection
{
    float A;
    float B;
    float k;
    float S;
};

LMPProjection LMP_GetProjection()
{
    float4 r0 = unity_WorldToShadow[0][0];
    float4 r2 = unity_WorldToShadow[0][2];
    float4 r3 = unity_WorldToShadow[0][3];
    LMPProjection p;
    float ww = max(dot(r3.xyz, r3.xyz), LMP_EPS);
    p.A = dot(r2.xyz, r3.xyz) / ww;
    p.B = r2.w - p.A * r3.w;
    p.k = sqrt(ww);
    p.S = length(r0.xyz - 0.5 * r3.xyz);
    return p;
}

// Unnormalized Vogel points; scale and rotation are computed once per kernel.
// Largest quality budget is 64 taps. Keep this table in sync with LMP_SampleCounts.
static const float2 LMP_VOGEL_POINTS[64] =
{
    float2(0.707106781, 0.000000000),
    float2(-0.903088752, 0.827303274),
    float2(0.138232208, -1.575084714),
    float2(1.138284879, 1.484691057),
    float2(-2.088892749, -0.369495715),
    float2(1.978781566, -1.258738858),
    float2(-0.661863708, 2.462100004),
    float2(-1.262245872, -2.430377617),
    float2(2.738568646, 1.000120877),
    float2(-2.849024345, 1.176035834),
    float2(1.373417995, -2.934914481),
    float2(1.014920964, 3.235727961),
    float2(-3.058983568, -1.772743504),
    float2(3.588535934, -0.788929559),
    float2(-2.190027619, 3.115088927),
    float2(-0.505947092, -3.904358787),
    float2(3.106018903, 2.617756019),
    float2(-4.179727820, 0.172844868),
    float2(3.048790590, -3.033953845),
    float2(-0.203975902, 4.411166947),
    float2(-2.900934035, -3.476288499),
    float2(4.595399818, 0.618304546),
    float2(-3.893672972, 2.709116238),
    float2(1.063975413, -4.729477383),
    float2(2.460919777, 4.294633145),
    float2(-4.810862704, -1.534796417),
    float2(4.673140878, -2.159109615),
    float2(-2.024521395, 4.837490374),
    float2(-1.806840736, -5.023477536),
    float2(4.807809276, 2.526849811),
    float2(-5.340266400, 1.407677085),
    float2(3.035444278, -4.720813281),
    float2(0.965592747, 5.618507867),
    float2(-4.576062462, -3.543959980),
    float2(5.853615441, -0.484960074),
    float2(-4.046082171, 4.373696270),
    float2(0.029472224, -6.041451100),
    float2(4.114439562, 4.535569103),
    float2(-6.178358927, -0.572608917),
    float2(5.006298463, -3.799602045),
    float2(-1.139044548, 6.261196173),
    float2(-3.431071528, -5.452315854),
    float2(6.287361064, 1.723105002),
    float2(-5.867884170, 3.011301275),
    float2(2.318893409, -6.254816812),
    float2(2.543291751, 6.247532878),
    float2(-6.162111385, -2.920339582),
    float2(6.586106412, -2.030566998),
    float2(-3.521252242, 6.008392684),
    float2(-1.477145921, -6.878810939),
    float2(5.793418848, 4.115373379),
    float2(-7.121258889, 0.887508781),
    float2(4.696434097, -5.517563481),
    float2(0.266559197, 7.309510667),
    float2(-5.181815656, -5.258211341),
    float2(7.440113047, 0.380417982),
    float2(-5.794584909, 4.787774612),
    float2(1.047802581, -7.510133804),
    float2(4.337639122, 6.299594181),
    float2(-7.517192175, -1.729688354),
    float2(6.767494003, -3.834191561),
    float2(-2.419935178, 7.459484817),
    float2(-3.280776754, -7.192809179),
    float2(7.335806942, 3.112223724)
};

float2 LMP_Vogel(int i, float scale, float2 rotation)
{
    float2 p = LMP_VOGEL_POINTS[i] * scale;
    return float2(p.x * rotation.x - p.y * rotation.y,
                  p.x * rotation.y + p.y * rotation.x);
}

void LMP_SampleCounts(out int blockers, out int filters)
{
    int q = (int)clamp(round(_lilMCRPCSS_Quality), 0.0, 3.0);
    blockers = q == 0 ? 9 : (q == 1 ? 13 : (q == 2 ? 21 : 33));
    filters  = q == 0 ? 16 : (q == 1 ? 32 : (q == 2 ? 48 : 64));
}

// Cone falloff of the spot (0 outside, smooth edge), without Unity's distance falloff.
float LMP_Cone(float3 positionWS)
{
    float4 lc = mul(unity_WorldToLight, float4(positionWS, 1.0));
    if(lc.w <= 0.0) return 0.0;
    float r = length(lc.xy / lc.w) * 2.0;      // 1 = edge of the spot circle
    return 1.0 - smoothstep(1.0 - saturate(_lilMCRPCSS_ConeEdge), 1.0, r);
}

// Project two tangent vectors of the receiver plane into shadow UV/depth.
// The caller supplies the smooth vertex-normal plane to avoid triangle-edge jumps.
float2 LMP_ReceiverGradient(float4 sc, float3 dxWS, float3 dyWS, float storedB)
{
    float4 sx = mul(unity_WorldToShadow[0], float4(dxWS, 0.0));
    float4 sy = mul(unity_WorldToShadow[0], float4(dyWS, 0.0));
    float invW = rcp(sc.w);
    float2 uv = sc.xy * invW;
    float2 dx = (sx.xy - uv * sx.w) * invW;
    float2 dy = (sy.xy - uv * sy.w) * invW;
    float zx = -storedB * sx.w * invW * invW;
    float zy = -storedB * sy.w * invW * invW;
    float det = dx.x * dy.y - dx.y * dy.x;
    float scale = max(length(dx) * length(dy), 1e-20);
    if(abs(det) <= scale * 1e-5) return 0.0;
    return float2(dy.y * zx - dx.y * zy, dx.x * zy - dy.x * zx) / det;
}

// Only compensate a local texel footprint. A smooth tangent plane cannot be
// extrapolated across an entire blur disc on disconnected hair cards.
float LMP_LocalPlaneDelta(float2 offsetUV, float2 gradient, float2 size, float zDir)
{
    // Radial, differentiable limit: no axis-aligned kink at +/-1.5 texels.
    float2 offsetPx = offsetUV * size;
    float2 localOffset = offsetPx * rsqrt(1.0 + dot(offsetPx, offsetPx) / 2.25) / size;
    return zDir * max(0.0, zDir * dot(gradient, localOffset));
}

// Compare each stored texel at its own receiver depth, then interpolate the
// visibility. Hardware PCF cannot do this: its four texels share one threshold,
// and choosing the nearest corner makes that threshold jump at texel boundaries.
float LMP_CompareDepth(float depth, int2 px, float2 centerUV, float zCompare, float2 gradient, float2 size, float zDir, float depthWidth)
{
    if(any(px < 0) || any(float2(px) >= size)) return 1.0;
    float2 offsetUV = (float2(px) + 0.5) / size - centerUV;
    float correction = LMP_LocalPlaneDelta(offsetUV, gradient, size, zDir);

    float gap = (depth - zCompare - correction) * zDir;
    // Approximate coverage only near the depth threshold; fully lit/occluded
    // texels remain 1/0. The caller bounds the width by the existing depth bias.
    if(depthWidth <= 1e-12) return gap > 0.0 ? 0.0 : 1.0;
    return 1.0 - smoothstep(-depthWidth, depthWidth, gap);
}

// Ordering everywhere below: (0,0), (1,0), (0,1), (1,1).
// GatherRed returns (0,1),(1,1),(1,0),(0,0) on D3D11: reorder .wzxy.
#if defined(SHADER_API_D3D11) && SHADER_TARGET >= 45 && !defined(LMP_DISABLE_GATHER)
    #define LMP_USE_GATHER 1
    SamplerState lmp_sampler_point_clamp;
#endif
float LMP_LoadDepth(int2 px, float2 size)
{
    if(any(px < 0) || any(float2(px) >= size)) return 0.0;
    return _ShadowMapTexture.Load(int3(px,0)).r;
}
float4 LMP_LoadDepth4(int2 base, float2 size)
{
#if defined(LMP_USE_GATHER)
    // Never let clamping change border behavior. Border tiles use bounded Load.
    if(all(base >= 0) && all(float2(base + 1) < size))
        return _ShadowMapTexture.GatherRed(lmp_sampler_point_clamp, (float2(base)+1.0)/size).wzxy;
#endif
    return float4(LMP_LoadDepth(base,size), LMP_LoadDepth(base+int2(1,0),size),
        LMP_LoadDepth(base+int2(0,1),size), LMP_LoadDepth(base+int2(1,1),size));
}
float4 LMP_Compare4(int2 base, float2 uv, float z, float2 gradient,
    float2 size, float zDir, float depthWidth)
{
    float4 d = LMP_LoadDepth4(base,size);
    return float4(
        LMP_CompareDepth(d.x,base,uv,z,gradient,size,zDir,depthWidth),
        LMP_CompareDepth(d.y,base+int2(1,0),uv,z,gradient,size,zDir,depthWidth),
        LMP_CompareDepth(d.z,base+int2(0,1),uv,z,gradient,size,zDir,depthWidth),
        LMP_CompareDepth(d.w,base+int2(1,1),uv,z,gradient,size,zDir,depthWidth));
}
float LMP_PlaneFilter(float2 tapUV, float2 centerUV, float zCompare, float2 gradient, float2 size, float zDir, float depthWidth)
{
    float2 coord = tapUV * size - 0.5;
    int2 base = (int2)floor(coord);
    float2 f = frac(coord);
    float4 lit = LMP_Compare4(base,centerUV,zCompare,gradient,size,zDir,depthWidth);
    return lerp(lerp(lit.x,lit.y,f.x),lerp(lit.z,lit.w,f.x),f.y);
}

// Integrate the bilinear reconstruction basis over a box footprint.
// This visits every contributing texel at small radii: no gaps between disc taps.
float LMP_TentIntegral(float x)
{
    float a = saturate(x + 1.0);
    float b = saturate(x);
    return 0.5 * a * a + b - 0.5 * b * b;
}
float LMP_DenseFilter(float2 uv, float radius, float zCompare, float2 gradient,
    float2 size, float zDir, float depthWidth)
{
    float2 coord = uv * size - 0.5;
    int2 base = (int2)floor(coord);
    float2 f = frac(coord);
    int reach = (int)ceil(radius);
    float sum = 0.0, weightSum = 0.0;
    // The footprint has 2*reach+2 texels per axis, so every tile is a full 2x2.
    [loop] for(int y = -reach; y <= reach + 1; y += 2)
    {
        float wy0 = LMP_TentIntegral(y-f.y+radius)-LMP_TentIntegral(y-f.y-radius);
        float wy1 = LMP_TentIntegral(y+1-f.y+radius)-LMP_TentIntegral(y+1-f.y-radius);
        [loop] for(int x = -reach; x <= reach + 1; x += 2)
        {
            float wx0 = LMP_TentIntegral(x-f.x+radius)-LMP_TentIntegral(x-f.x-radius);
            float wx1 = LMP_TentIntegral(x+1-f.x+radius)-LMP_TentIntegral(x+1-f.x-radius);
            float4 weight = float4(wx0*wy0,wx1*wy0,wx0*wy1,wx1*wy1);
            if(all(weight <= 0.0)) continue;
            float4 lit = LMP_Compare4(base+int2(x,y),uv,zCompare,gradient,size,zDir,depthWidth);
            sum += dot(weight,lit);
            weightSum += dot(weight,float4(1,1,1,1));
        }
    }
    return sum / max(weightSum, LMP_EPS);
}
// Soft shadow: 1 = lit, 0 = shadowed.
float LMP_Shadow(float3 positionWS, float3 N, float ndl)
{
    uint wi = 1, hi = 1;
    _ShadowMapTexture.GetDimensions(wi, hi);
    float2 size = max(float2((float)wi, (float)hi), 1.0);
    float texel = 1.0 / max(size.x, size.y);

    LMPProjection p = LMP_GetProjection();

    // Normal offset of about one shadow-map texel at this distance, larger at grazing angles (where acne appears).
    // The projection is linear in the position, so the offset is added in shadow space.
    float4 sc = mul(unity_WorldToShadow[0], float4(positionWS, 1.0));
    float texelMeters = texel * abs(sc.w) / max(p.S, LMP_EPS);
    sc += mul(unity_WorldToShadow[0], float4(N * (texelMeters * _lilMCRPCSS_NormalBias * (0.5 + saturate(1.0 - ndl))), 0.0));

    // w grows with the distance in front of the light; its sign depends on the platform's matrix convention.
    float wSign = sc.w >= 0.0 ? 1.0 : -1.0;
    float wR = max(abs(sc.w), LMP_TINY);
    float2 uv = sc.xy / sc.w;
    if(any(uv < 0.0) || any(uv > 1.0)) return 1.0;

    // Receiver depth with the depth bias, in the shadow map's own depth encoding.
    float2 stored = LMP_StoredProjection(p.A, p.B, LMP_DepthNear(), 1.0 - LMP_DepthNear());
    // The control adds to the 2 mm baseline: UI 0 means 0.002 m.
    float depthBiasMeters = 0.002 + max(_lilMCRPCSS_DepthBias, 0.0);
    float wBiased = max(wR - depthBiasMeters * p.k, LMP_TINY);

    float zCompare = stored.x + stored.y / (wBiased * wSign);

    int nBlocker = 9, nFilter = 16;
    LMP_SampleCounts(nBlocker, nFilter);
    float lightSize = max(_lilMCRPCSS_LightSize, 0.0);

    // Comparison direction follows the stored depth mapping, independently of
    // search range. Light size zero uses fixed-width PCF and skips blocker search.
    float zDir = stored.y * wSign >= 0.0 ? 1.0 : -1.0;

    // A geometric (ddx/ddy) plane changes at every triangle edge. With wide
    // kernels those changes become visible as a mesh. Use the interpolated
    // vertex-normal tangent plane, continuous across smooth-shaded edges.
    float3 referenceAxis = abs(N.z) < 0.9 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    float3 tangent = normalize(cross(referenceAxis, N));
    float3 bitangent = cross(N, tangent);
    float3 toLight = normalize(_WorldSpaceLightPos0.xyz - positionWS);
    float planeFacing = abs(dot(N, toLight));
    float2 gradient = 0.0;
    [branch]
    if(planeFacing > 0.05)
        gradient = LMP_ReceiverGradient(sc, tangent, bitangent, stored.y)
            * smoothstep(0.05, 0.2, planeFacing);

    // Analytic shadow-texel depth footprint. Limit to half the current depth
    // bias to avoid turning thin foreground blockers into broad translucent bands.
    float unbiasedDepth = stored.x + stored.y / (wR * wSign);
    float depthWidth = min(0.25 * dot(abs(gradient), 1.0 / size),
        0.5 * abs(zCompare - unbiasedDepth));
    // Shadow type. Both share the shadow map, the depth comparison and every slider:
    //   0 PCSS : blocker search, then a filter as wide as the penumbra (softer with distance).
    //   1 PCF  : no blocker search; one filter width everywhere, the width PCSS gives an occluder a fifth of the
    //            Blocker Search Range away (0.1 m by default), so switching keeps a similar softness. The cheaper
    //            one: about half the reads of PCSS.
    bool fixedWidth = _lilMCRPCSS_Mode > 0.5;
    float minFilterUV = min(texel * max(_lilMCRPCSS_MinBlur, 0.0), LMP_MAX_UV);
    float filterUV = minFilterUV;
    [branch]
    if(fixedWidth && lightSize > 0.0)
    {
        float wRef = max(wR - max(_lilMCRPCSS_BlockerRange, 0.0) * 0.2 * p.k, wR * 0.1);
        filterUV = clamp(lightSize * (wR - wRef) / wRef * p.S / wR, minFilterUV, LMP_MAX_UV);
    }
    // 1 = every tap of the blocker search is a blocker: the point is deep inside the shadow (see below).
    float umbra = 0.0;
    [branch]
    if(lightSize > 0.0 && !fixedWidth)
    {
        float wMin = max(wR - max(_lilMCRPCSS_BlockerRange, 0.0) * p.k, wR * 0.1);
        float searchUV = clamp(lightSize * p.S * (wR - wMin) / max(wR * wMin, LMP_EPS), texel * 1.5, LMP_MAX_UV);
        // Keep a center tap and four local taps as the search expands. Otherwise
        // every Vogel tap moves away and a thin central hair blocker disappears.
        float outerScale = rsqrt((float)((nBlocker - 5) >> 1));
        float localRadius = min(searchUV, texel * 1.5);
        float distanceSum = 0.0;
        float blockerCount = 0.0;
        float searched = 0.0, localSearched = 0.0, localBlocked = 0.0;
        [loop]
        for(int i = 0; i < nBlocker; i++)
        {
            float2 offset = 0.0;
            if(i > 0 && i < 5)
            {
                float2 axis = i <= 2 ? float2(1.0,0.0) : float2(0.0,1.0);
                offset = axis * ((i & 1) != 0 ? 1.0 : -1.0) * localRadius;
            }
            else if(i >= 5)
            {
                int pair = (i - 5) >> 1;
                offset = LMP_Vogel(pair, outerScale, float2(1.0,0.0)) * searchUV;
                if(((i - 5) & 1) != 0) offset = -offset;
            }
            float2 suv = uv + offset;
            // Do not repeat border occluders across the light frustum.
            if(any(suv < 0.0) || any(suv >= 1.0)) continue;
            float2 sampleCoord = suv * size - 0.5;
            int2 sampleBase = (int2)floor(sampleCoord);
            float2 sf = frac(sampleCoord);
            float4 depths = LMP_LoadDepth4(sampleBase,size);
            // Bilinear blocker coverage makes the search continuous across texel
            // boundaries instead of changing the entire penumbra in one step.
            [unroll] for(int by = 0; by < 2; by++)
            [unroll] for(int bx = 0; bx < 2; bx++)
            {
                int2 px = sampleBase + int2(bx,by);
                if(any(px < 0) || any(float2(px) >= size)) continue;
                float weight = (bx == 0 ? 1.0-sf.x : sf.x) * (by == 0 ? 1.0-sf.y : sf.y);
                searched += weight;
                if(i < 5) localSearched += weight;
                float d = depths[by*2+bx];
                float delta = LMP_LocalPlaneDelta((float2(px)+0.5)/size-uv, gradient, size, zDir);
                float gap = (d-zCompare-delta)*zDir;
                float hit = depthWidth > 1e-12 ? smoothstep(-depthWidth,depthWidth,gap) : (gap > 0.0 ? 1.0 : 0.0);
                float denominator = d-stored.x;
                if(hit > 0.0 && abs(denominator) > 1e-10)
                {
                    float distance = stored.y / denominator * wSign;
                    if(distance > 0.0 && distance < wR)
                    {
                        float mass = weight * hit;
                        distanceSum += distance * mass;
                        blockerCount += mass;
                        if(i < 5) localBlocked += mass;
                    }
                }
            }
        }
        if(blockerCount > LMP_EPS)
        {
            float wB = max(distanceSum / blockerCount, LMP_TINY);
            float penumbra = clamp(lightSize * max(wR-wB,0.0)/wB*p.S/wR, minFilterUV, LMP_MAX_UV);
            filterUV = lerp(minFilterUV, penumbra, smoothstep(0.0,1.0,blockerCount));
        }
        // No blockers: still evaluate the local/minimum filter. A sparse search
        // is not proof that the receiving pixel is lit, especially on hair cards.

        // Every tap blocked: the filter below, which reads inside the searched disc (the penumbra is never wider
        // than the search), would return about 0 after its 16 to 64 reads. It is skipped: the inside of a shadow,
        // usually a large part of the shadowed pixels and the part with the widest filter, costs the search only.
        //   Smooth on both sides: it fades in over the last tenth of blocked taps, so nothing pops where one tap
        //   leaves the blocker, and the center tap with its four neighbors must be blocked too, so a small lit gap
        //   at the point itself is never darkened.
        umbra = smoothstep(0.9, 1.0, blockerCount / max(searched, LMP_EPS))
              * smoothstep(0.8, 1.0, localBlocked / max(localSearched, LMP_EPS));
        if(umbra >= 1.0) return 0.0;
    }

    // Filter.
    //    Small radii integrate all contributing texels. Close to the occluder the shadow follows
    //    the occluder's shape with smooth texel edges instead of blocky texels, without noise.
    //    Wider penumbra: bilinearly reconstructed taps over the penumbra disc, center weighted, blended in smoothly.
    float radiusPx = filterUV / texel;
    float dense = 0.0;
    float denseBlend = smoothstep(4.0, 5.0, radiusPx);
    if(denseBlend < 1.0)
    {
        dense = LMP_DenseFilter(uv, max(radiusPx, 1.0), zCompare, gradient, size, zDir, depthWidth);
        if(radiusPx <= 4.0) return dense * (1.0 - umbra);
    }
    // Only radius > 4 reaches here.
    // Equal-weight opposite pairs have exactly zero centroid at every quality.
    int pairCount = nFilter >> 1;
    float filterScale = rsqrt((float)pairCount);
    float2 filterRotation = float2(1.0, 0.0);
    sincos(1.3, filterRotation.y, filterRotation.x);   // fixed rotation: coherent kernel, no screen-pixel noise
    float disc = 0.0;
    float weightSum = 0.0;
    [loop]
    for(int j = 0; j < nFilter; j++)
    {
        int pair = j >> 1;
        float2 o = LMP_Vogel(pair, filterScale, filterRotation);
        if((j & 1) != 0) o = -o;
        // Smoothly taper the disc instead of giving its outer ring half weight.
        float radialWeight = 1.0 - 0.75 * ((float)pair + 0.5) / (float)pairCount;
        float wgt = radialWeight * radialWeight;
        weightSum += wgt;
        disc += LMP_PlaneFilter(uv + o * filterUV, uv, zCompare, gradient, size, zDir, depthWidth) * wgt;
    }
    float lit = disc / max(weightSum, LMP_EPS);
    return lerp(dense, lit, denseBlend) * (1.0 - umbra);
}

// Shadow amount 0..1 (1 = fully in the cast shadow) at a surface point.
float LMP_ShadowAmount(float3 positionWS, float3 normalWS)
{
    float cone = LMP_Cone(positionWS);
    [branch]
    if(cone <= 0.0) return 0.0;

    float3 N = normalize(normalWS);
    float ndl = dot(N, normalize(_WorldSpaceLightPos0.xyz - positionWS));
    float facing = lerp(saturate(ndl * 6.0 + 0.3), 1.0, saturate(_lilMCRPCSS_BackShadow));
    [branch]
    if(facing <= 0.0) return 0.0;

    float lit = LMP_Shadow(positionWS, N, max(ndl, 0.0));
    return (1.0 - lit) * cone * facing;
}

#endif

//------------------------------------------------------------------------------------------------------------------------------
// Pass "LILMCR_PCSS_SHADOW"
#if defined(LILMCR_PCSS_PASS)

// PCSS-only mask. Only this pass samples it, so it is declared here (a texture is not part of the per-material CBUFFER;
// the amount is, in LMP_PCSS_PROPERTIES). Sampled with lilToon's shared sampler, so no sampler is added.
TEXTURE2D(_lilMCRPCSS_Mask);

struct lmpAppdata
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float2 uv0          : TEXCOORD0;
    LIL_VERTEX_INPUT_INSTANCE_ID
};

struct lmpV2F
{
    float4 positionCS   : SV_POSITION;
    float3 positionWS   : TEXCOORD0;
    float3 normalWS     : TEXCOORD1;
    float2 uv0          : TEXCOORD2;
    LIL_VERTEX_OUTPUT_STEREO
};

lmpV2F lmpVert(lmpAppdata input)
{
    lmpV2F o = (lmpV2F)0;
    LIL_SETUP_INSTANCE_ID(input);
    LIL_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
#if defined(LMP_ACTIVE)
    // Hidden by lilToon's Invisible switch: no shadow either.
    if(!_Invisible && LMP_IsKeyLight())
    {
        lilVertexPositionInputs p = lilGetVertexPositionInputs(input.positionOS);   // same transform as lilToon's passes
        o.positionCS = p.positionCS;
        o.positionWS = p.positionWS;
        o.normalWS = lilTransformNormalOStoWS(input.normalOS, true);
        o.uv0 = input.uv0;
    }
#endif
    return o;   // not this avatar's light: collapsed, nothing is drawn
}

// Shadow factor (multiplied into what Macaron drew).
//   Shadow Color  : brightness and tone of the shadow.
//   Use Base Color: the shadow takes the surface's own hue: the tint becomes the base color normalized to the Shadow
//                   Color's brightness, so a skin turns into a deeper skin tone instead of gray.
//   Saturation    : saturation of that tint at the same brightness. 0 = neutral (plain darkening, the surface keeps its
//                   own color), 1 = the tint as is, above 1 = more vivid.
// The tint is multiplied as is. An earlier version divided a target color by the base color, which overshot into the
// complementary hue (pink / red surfaces turned green) wherever the drawn color was paler than the base texture.
// Every channel stays at or below 1: a shadow never brightens a channel.
float3 LMP_ShadowMultiplier(float3 albedo)
{
    const float3 lumW = float3(0.2126, 0.7152, 0.0722);
    float3 sc = saturate(_lilMCRPCSS_ShadowColor.rgb);
    float scL = dot(sc, lumW);
    float aL = dot(albedo, lumW);
    float3 fromBase = aL > LMP_TINY ? albedo * (scL / aL) : sc;
    float3 tint = lerp(sc, fromBase, saturate(_lilMCRPCSS_AlbedoTint));
    float tL = dot(tint, lumW);
    tint = lerp(tL.xxx, tint, max(_lilMCRPCSS_ShadowSaturation, 0.0));
    return saturate(tint);
}

// Limits: only the main texture alpha, the alpha mask, Macaron's global mask and the PCSS mask are evaluated. lilToon's Dissolve,
// Tessellation, Parallax and custom vertex displacement are not reproduced, so the shadow follows the plain mesh there.
float4 lmpFrag(lmpV2F input) : SV_Target
{
#if defined(LMP_WORLD_CAPTURE)
    // Do not multiply signed owner/depth data in the dedicated capture camera.
    if(Macaron_IsWorldCapture()) discard;
#endif
    LIL_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    // Main texture UV with lilToon's scroll / rotation, like Macaron's own passes.
#if defined(LIL_FEATURE_ANIMATE_MAIN_UV) || defined(LIL_LITE)
    float2 uvMain = lilCalcUV(input.uv0, _MainTex_ST, _MainTex_ScrollRotate);
#else
    float2 uvMain = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
#endif
    float4 baseColor = LIL_SAMPLE_2D(_MainTex, sampler_MainTex, uvMain) * _Color;
    // Where the shadow appears: Macaron's global mask (red channel, its own tiling and amount), when present.
    float mask = 1.0;
#if defined(LMP_GLOBAL_MASK)
    float maskAmount = saturate(LMP_GLOBAL_MASK_AMOUNT);
    // Material-uniform branch keeps implicit texture derivatives valid.
    [branch]
    if(maskAmount > 0.0)
    {
        float maskValue = LIL_SAMPLE_2D(LMP_GLOBAL_MASK, lil_sampler_linear_repeat, uvMain * LMP_GLOBAL_MASK_ST.xy + LMP_GLOBAL_MASK_ST.zw).r;
        mask = lerp(1.0, saturate(maskValue), maskAmount);
    }
#endif
    // PCSS-only mask (red channel, main UV like Macaron's other masks): where this add-on's shadow appears.
    float pcssMaskAmount = saturate(_lilMCRPCSS_MaskAmount);
    [branch]
    if(pcssMaskAmount > 0.0)
    {
        float pcssMask = LIL_SAMPLE_2D(_lilMCRPCSS_Mask, lil_sampler_linear_repeat, uvMain).r;
        mask *= lerp(1.0, saturate(pcssMask), pcssMaskAmount);
    }
    float alpha = 1.0;
#if LIL_RENDER != 0
    float baseAlpha = baseColor.a;
// _AlphaMaskMode / _AlphaMaskScale / _AlphaMaskValue / _AlphaMask_ST exist only with LIL_FEATURE_ALPHAMASK. lilToon's
// upload optimization turns that feature off when no material of the avatar uses an alpha mask; using them without
// this guard broke the whole transparent / cutout shader after upload (Unity fell back to Unlit/Texture).
#if !defined(LIL_LITE) && defined(LIL_FEATURE_ALPHAMASK)
    // lilToon's alpha mask, handled like the World Shadow capture pass.
    if(_AlphaMaskMode > 0)
    {
        float alphaMask = 1.0;
#if defined(LIL_FEATURE_AlphaMask)
        alphaMask = LIL_SAMPLE_2D(_AlphaMask, sampler_MainTex, uvMain * _AlphaMask_ST.xy + _AlphaMask_ST.zw).r;
#endif
        alphaMask = saturate(alphaMask * _AlphaMaskScale + _AlphaMaskValue);
        if(_AlphaMaskMode == 1) baseAlpha = alphaMask;
        else if(_AlphaMaskMode == 2) baseAlpha *= alphaMask;
        else if(_AlphaMaskMode == 3) baseAlpha = saturate(baseAlpha + alphaMask);
        else if(_AlphaMaskMode == 4) baseAlpha = saturate(baseAlpha - alphaMask);
    }
#endif
#if LIL_RENDER == 1
    clip(baseAlpha - _Cutoff);
#else
    alpha = saturate(baseAlpha);
#endif
#endif
    // No implicit-gradient texture reads occur after this per-pixel early out.
    float weight = LMP_Weight() * alpha * mask;
    [branch]
    if(weight <= 0.0) return float4(1.0, 1.0, 1.0, 1.0);
    float shadow = 0.0;
#if defined(LMP_ACTIVE)
    shadow = LMP_ShadowAmount(input.positionWS, input.normalWS);
#endif
    float amount = shadow * weight;
    [branch]
    if(amount <= 0.0) return float4(1.0, 1.0, 1.0, 1.0);
    float3 shadowMul = LMP_ShadowMultiplier(saturate(baseColor.rgb));
    float3 result = lerp(1.0.xxx, shadowMul, amount);
    // Penumbra tint: the edge of a shadow is not a plain fade but a band of two colors, as in hand-drawn animation:
    // Penumbra Color on the side of the light, Penumbra Color 2 on the side of the shadow.
    //   The band is strongest where the shadow is half way and gone in full light and full shadow, so it is exactly
    //   as wide as the penumbra: thin next to the occluder, wide far from it. It reuses the shadow amount: no extra
    //   shadow map read, a few multiplications, skipped entirely at 0.
    //   Where the color comes from is a blend, every step of it a slider (no switch):
    //     Use Base Color : 0 = the two chosen colors, 1 = the surface's own base color, in between = a mix of both.
    //     Saturation     : saturation of that result. 0 = neutral (no tint), 1 = as is, above 1 = more vivid: a pale
    //                      skin gives a clearly warm edge from its own color alone.
    //   The result is used as a hue (brightest channel = 1) and multiplied: no channel gets brighter.
    float penumbraTint = saturate(_lilMCRPCSS_PenumbraTint);
    [branch]
    if(penumbraTint > 0.0)
    {
        float3 albedo = saturate(baseColor.rgb);
        float3 chosen = lerp(saturate(_lilMCRPCSS_PenumbraColor.rgb), saturate(_lilMCRPCSS_PenumbraColor2.rgb), smoothstep(0.25, 0.75, shadow));
        float3 hue = lerp(chosen, albedo, saturate(_lilMCRPCSS_PenumbraAlbedo));
        float hueL = dot(hue, float3(0.2126, 0.7152, 0.0722));
        hue = max(lerp(hueL.xxx, hue, max(_lilMCRPCSS_PenumbraSaturation, 0.0)), 0.0);
        float hueMax = max(max(hue.r, hue.g), hue.b);
        // A black surface has no hue to give: no tint there.
        hue = hueMax > 1e-4 ? hue / hueMax : 1.0.xxx;
        float band = 4.0 * shadow * (1.0 - shadow);
        result *= lerp(1.0.xxx, hue, band * penumbraTint * weight);
    }
    return float4(result, 1.0);
}

#endif

#endif
