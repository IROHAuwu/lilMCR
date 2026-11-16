#ifndef LMP_DEPTH_MATH_INCLUDED
#define LMP_DEPTH_MATH_INCLUDED
bool LMP_DepthEnabled()
{
    return _lilMCRPCSS_SelfOnly > 0.5 && _lilMCRPCSS_Projection.x > 0.0 && _lilMCRPCSS_Projection.z > 0.0;
}
float LMP_DepthNear()
{
#if defined(UNITY_REVERSED_Z)
    return 1.0;
#else
    return 0.0;
#endif
}
// Reparameterize z=A+B/w over the avatar's usable light interval. This is
// affine in clip z/w, so homogeneous w, silhouettes and alpha UVs stay intact.
float2 LMP_StoredProjection(float A, float B, float nearZ, float farZ)
{
    if(!LMP_DepthEnabled()) return float2(A,B);
    float ratio=clamp(_lilMCRPCSS_Projection.z,0.001,0.95);
    float sourceOffset=(farZ-A)/ratio;
    float gain=(farZ-nearZ)*0.0625/((farZ-A)-sourceOffset);
    return float2(nearZ-sourceOffset*gain, B*gain);
}
#endif

