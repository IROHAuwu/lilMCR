#ifndef LMP_DEPTH_INCLUDED
#define LMP_DEPTH_INCLUDED
// Affine shadow-depth packing for the dedicated light. One light, unchanged UV/w.
//
// Material variables of the add-on. The generator puts LMP_PCSS_PROPERTIES at the front of Macaron's
// LIL_CUSTOM_PROPERTIES, so lilToon declares them inside its per-material CBUFFER (official custom shader rule).
// The macro is expanded later, inside lil_common_input.hlsl, so defining it after custom.hlsl's macro is fine.
#define LMP_PCSS_PROPERTIES \
    float _lilMCRPCSS_Enable; \
    float _lilMCRPCSS_LightId; \
    float _lilMCRPCSS_SelfOnly; \
    float4 _lilMCRPCSS_Projection; \
    float4 _lilMCRPCSS_ShadowColor; \
    float _lilMCRPCSS_AlbedoTint; \
    float _lilMCRPCSS_ShadowSaturation; \
    float _lilMCRPCSS_Strength; \
    float4 _lilMCRPCSS_PenumbraColor; \
    float4 _lilMCRPCSS_PenumbraColor2; \
    float _lilMCRPCSS_PenumbraTint; \
    float _lilMCRPCSS_BackShadow; \
    float _lilMCRPCSS_LightSize; \
    float _lilMCRPCSS_BlockerRange; \
    float _lilMCRPCSS_MinBlur; \
    float _lilMCRPCSS_Quality; \
    float _lilMCRPCSS_NormalBias; \
    float _lilMCRPCSS_DepthBias; \
    float _lilMCRPCSS_ConeEdge; \
    float _lilMCRPCSS_Reach; \
    float _lilMCRPCSS_MaxLevel; \
    float _lilMCRPCSS_MaskAmount;

#endif
