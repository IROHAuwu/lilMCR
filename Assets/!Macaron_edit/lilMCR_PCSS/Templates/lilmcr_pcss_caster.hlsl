#ifndef LMP_CASTER_INCLUDED
#define LMP_CASTER_INCLUDED
#include "lilmcr_pcss_depth_math.hlsl"
// Included after lilToon's pipeline macros, before the shadow vertex entry point.
// Wrap the standard bias result, preserving alpha clipping, geometry and world-space data.
#if defined(LIL_BRP) && defined(UNITY_PASS_SHADOWCASTER) && !defined(SHADOWS_CUBE)
bool LMP_MatchesCasterProjection()
{
    if(_lilMCRPCSS_Enable < 0.5 || !LMP_DepthEnabled()) return false;
    float4 signature = _lilMCRPCSS_Projection;
    float focal = abs(UNITY_MATRIX_P._m11);
    // Reject orthographic, oblique and non-square camera projections.
    return abs(UNITY_MATRIX_P._m33) < 0.000001
        && abs(UNITY_MATRIX_P._m02) + abs(UNITY_MATRIX_P._m12) < 0.000001
        && abs(abs(UNITY_MATRIX_P._m00) - focal) < signature.y
        && abs(focal - signature.x) < signature.y;
}
float4 LMP_ShadowBiasAndPack(float4 clipPosition)
{
    clipPosition = UnityApplyLinearShadowBias(clipPosition);
    if(LMP_MatchesCasterProjection())
    {
        // Preserve vertices outside the original depth interval: do not pull clipped
        // geometry into the frustum. x, y and w are never modified.
        float nearClip = UNITY_NEAR_CLIP_VALUE * clipPosition.w;
#if defined(UNITY_REVERSED_Z)
        bool inside = clipPosition.z >= 0.0 && clipPosition.z <= nearClip;
#else
        bool inside = clipPosition.z >= nearClip && clipPosition.z <= clipPosition.w;
#endif
        if(inside && clipPosition.w > 0.0)
        {
            float A = UNITY_MATRIX_P._m22 / UNITY_MATRIX_P._m32;
            float B = UNITY_MATRIX_P._m23;
            float nearZ = UNITY_NEAR_CLIP_VALUE;
#if defined(UNITY_REVERSED_Z)
            float farZ = 0.0;
#else
            float farZ = 1.0;
#endif
            float2 stored=LMP_StoredProjection(A,B,nearZ,farZ);
            clipPosition.z=stored.x*clipPosition.w+stored.y;
        }
    }
    return clipPosition;
}
// Function body above resolves Unity's original function before this macro is defined.
#define UnityApplyLinearShadowBias(position) LMP_ShadowBiasAndPack(position)
#endif
#endif
