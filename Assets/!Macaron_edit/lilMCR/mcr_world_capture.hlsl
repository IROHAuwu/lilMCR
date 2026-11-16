#ifndef MCR_WORLD_CAPTURE_PASS_INCLUDED
#define MCR_WORLD_CAPTURE_PASS_INCLUDED
// Reserve the closest hardware-depth band for this camera's owner, while
// storing actual linear depth in the color target. Not absolute isolation:
// Always-depth effects, near-plane intruders and duplicate keys can interfere.
#define MCR_CAPTURE_DEPTH_BAND (1.0 / 1024.0)
#include "mcr_world_capture_match.hlsl"
struct mcrCaptureAppdata
{
    float4 positionOS : POSITION;
    float2 uv0 : TEXCOORD0;
    LIL_VERTEX_INPUT_INSTANCE_ID
};
struct mcrCaptureV2F
{
    float4 positionCS : SV_POSITION;
    float2 uv0 : TEXCOORD0;
    float linearDepth : TEXCOORD1;
    LIL_VERTEX_OUTPUT_STEREO
};
mcrCaptureV2F mcrCaptureVert(mcrCaptureAppdata input)
{
    mcrCaptureV2F o = (mcrCaptureV2F)0;
    LIL_SETUP_INSTANCE_ID(input);
    LIL_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    // Not a world capture, or hidden by lilToon's Invisible switch: collapsed.
    if(_Invisible || !Macaron_IsWorldCapture()) { o.positionCS = float4(0,0,0,1); return o; }
    lilVertexPositionInputs p = lilGetVertexPositionInputs(input.positionOS);
    o.positionCS = p.positionCS;
    o.uv0 = input.uv0;
    o.linearDepth = (-mul(LIL_MATRIX_V, float4(p.positionWS,1)).z - _ProjectionParams.y)
        / max(_ProjectionParams.z - _ProjectionParams.y, 0.0001);
    float priorityDepth = o.linearDepth * MCR_CAPTURE_DEPTH_BAND;
#if defined(UNITY_REVERSED_Z)
    o.positionCS.z = (1.0 - priorityDepth) * o.positionCS.w;
#else
    o.positionCS.z = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, priorityDepth) * o.positionCS.w;
#endif
    return o;
}
float4 mcrCaptureFrag(mcrCaptureV2F input) : SV_Target
{
    LIL_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    if(!Macaron_IsWorldCapture()) discard;
    clip(input.linearDepth);
    clip(1.0 - input.linearDepth);
    // Unity supplies skinning/blendshapes. Specialized fur, tessellation,
    // dissolve, parallax and custom displacement silhouettes are not reproduced.
    float2 uv = input.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
#if defined(LIL_FEATURE_ANIMATE_MAIN_UV) || defined(LIL_LITE)
    uv = lilCalcUV(input.uv0, _MainTex_ST, _MainTex_ScrollRotate);
#endif
#if LIL_RENDER != 0
    float alpha = LIL_SAMPLE_2D(_MainTex, sampler_MainTex, uv).a * _Color.a;
// _AlphaMaskMode / _AlphaMaskScale / _AlphaMaskValue / _AlphaMask_ST exist only with LIL_FEATURE_ALPHAMASK. lilToon's
// upload optimization turns that feature off when no material of the avatar uses an alpha mask; using them without
// this guard broke the whole transparent / cutout shader after upload (Unity fell back to Unlit/Texture).
#if !defined(LIL_LITE) && defined(LIL_FEATURE_ALPHAMASK)
    if(_AlphaMaskMode > 0)
    {
        float mask = 1.0;
#if defined(LIL_FEATURE_AlphaMask)
        mask = LIL_SAMPLE_2D(_AlphaMask, sampler_MainTex, uv * _AlphaMask_ST.xy + _AlphaMask_ST.zw).r;
#endif
        mask = saturate(mask * _AlphaMaskScale + _AlphaMaskValue);
        if(_AlphaMaskMode == 1) alpha = mask;
        else if(_AlphaMaskMode == 2) alpha *= mask;
        else if(_AlphaMaskMode == 3) alpha = saturate(alpha + mask);
        else if(_AlphaMaskMode == 4) alpha = saturate(alpha - mask);
    }
#endif
#if LIL_RENDER == 1
    clip(alpha - _Cutoff);
#else
    clip(alpha - 0.5);
#endif
#endif
    if(_MCR_CasterMaskAmount > 0.0)
    {
        float mask = LIL_SAMPLE_2D(_MCR_CasterMask, lil_sampler_linear_repeat,
            uv * _MCR_CasterMask_ST.xy + _MCR_CasterMask_ST.zw).r;
        clip(lerp(1.0, mask, saturate(_MCR_CasterMaskAmount)) - 0.5);
    }
    return float4(input.linearDepth, -_MCR_WorldCapture.w, -1 - _Time.y, 1);
}
#endif