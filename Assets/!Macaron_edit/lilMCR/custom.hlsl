//----------------------------------------------------------------------------------------------------------------------
// Macaron
// Built on the official lilToon custom shader template (lilToon, MIT License, see LICENSE_lilToon.txt).
// Installed next to lilToon as a separate shader; no file of the lilToon package is modified.
//
// NOTE:
// This file is included from the HLSLINCLUDE block of every pass, i.e. BEFORE lilToon writes
// "#define LIL_PASS_xxx" / "#define LIL_OUTLINE". Any pass filtering written here with #if is
// evaluated too early, so pass filtering is done inside custom_insert.hlsl instead.
//----------------------------------------------------------------------------------------------------------------------

#define LIL_CUSTOM_PROPERTIES \
    float _MCR_Active; \
    float _MCR_Amount; \
    float4 _MCR_CasterMask_ST; \
    float _MCR_CasterMaskAmount; \
    float4 _MCR_WorldCapture; \
    float4 _MCR_GlobalMask_ST; \
    float _MCR_GlobalMaskAmount; \
    float _MCR_NormalSmooth; \
    float _MCR_FloorBrightness; \
    float4 _MCR_FadeDistance; \
    float _MCR_AlphaAware; \
    float _MCR_OccEnable; \
    float _MCR_OccMethod; \
    float _MCR_OccQuality; \
    float _MCR_OccScreenLOD; \
    float _MCR_OccAmount; \
    float _MCR_OccContrast; \
    float _MCR_OccNoise; \
    float4 _MCR_OccTintNear; \
    float4 _MCR_OccTintFar; \
    float _MCR_OccAlbedoTint; \
    float _MCR_OccReflection; \
    float _MCR_OccBlend; \
    float _MCR_OccMaskAmount; \
    float _MCR_OccMaskFlip; \
    float _MCR_SSAOReach; \
    float _MCR_SSAODepthReach; \
    float _MCR_SSAOBias; \
    float _MCR_SSAOSpread; \
    float _MCR_SSAOEdgeReject; \
    float _MCR_SoftRadius; \
    float _MCR_SoftFocus; \
    float _MCR_SoftLift; \
    float _MCR_SoftBias; \
    float _MCR_SoftDepthReach; \
    float _MCR_SoftClean; \
    float _MCR_OccReach; \
    float _MCR_OccFocus; \
    float _MCR_OccThickness; \
    float _MCR_OccSoftness; \
    float _MCR_OccAngle; \
    float _MCR_OccScaleMode; \
    float _MCR_GTAOReach; \
    float _MCR_GTAOWideReach; \
    float _MCR_GTAODepthReach; \
    float _MCR_GTAOWideAmount; \
    float _MCR_GTAOSoftness; \
    float _MCR_ShadowEnable; \
    float _MCR_ShadowIntensity; \
    float4 _MCR_ShadowColor; \
    float _MCR_ShadowSpace; \
    float4 _MCR_ShadowDirection; \
    float4 _MCR_AvatarBasisX; \
    float4 _MCR_AvatarBasisY; \
    float4 _MCR_AvatarBasisZ; \
    float _MCR_ShadowLength; \
    float _MCR_ShadowQuality; \
    float _MCR_ShadowScreenLOD; \
    float _MCR_ShadowSoftness; \
    float _MCR_ShadowThickness; \
    float4 _MCR_ShadowOffset; \
    float _MCR_ShadowReject; \
    float _MCR_ShadowNormalReject; \
    float _MCR_ShadowBackShade; \
    float _MCR_ShadowJitter; \
    float _MCR_ShadowMaskAmount; \
    float _MCR_AdjActive; \
    float _MCR_AdjLocalOnly; \
    float _MCR_AdjBrightness; \
    float _MCR_AdjFixedBrightness; \
    float _MCR_AdjLightColorInfluence; \
    float _MCR_AdjTemperature; \
    float _MCR_AdjSaturation; \
    float _MCR_AdjLightStrength; \
    float _MCR_AdjLightYaw; \
    float _MCR_AdjLightPitch; \
    float _DstBlendFA; \
    float _BlendOpFA; \
    float _StencilComp; \
    float _StencilPass; \
    float _StencilFail; \
    float _StencilZFail; \
    float _MCR_UzuEnable; \
    float _MCR_UzuAmount; \
    float _MCR_UzuBias; \
    float _MCR_UzuBrightness; \
    float _MCR_UzuBackReject; \
    float _MCR_UzuSelf; \
    float _MCR_UzuMaskAmount; \
    float _MCR_UzuIsLocal; \
    float _MCR_LocalOnly;

// Textures only. Sampled with lilToon's shared sampler (lil_sampler_linear_repeat), so no sampler is added.
#define LIL_CUSTOM_TEXTURES \
    TEXTURE2D(_MCR_CasterMask); \
    TEXTURE2D(_MCR_GlobalMask); \
    TEXTURE2D(_MCR_OccMask); \
    TEXTURE2D(_MCR_ShadowMask); \
    TEXTURE2D(_MCR_UzuMask);

// Uzumore (vertex stage of the color passes): main UV for its mask and the object-space normal for the facing test.
#define LIL_REQUIRE_APP_POSITION
#define LIL_REQUIRE_APP_TEXCOORD0
#define LIL_REQUIRE_APP_NORMAL

// The forward passes need the interpolated vertex normal (fd.origN, Normal Smoothing).
#define LIL_V2F_FORCE_TEXCOORD0
#define LIL_V2F_FORCE_POSITION_WS
#define LIL_V2F_FORCE_NORMAL

// Applied right before fog, so fog is not darkened.
// BEFORE_FOG only exists in the forward passes (normal / lite / fur / gem), never in ShadowCaster / Meta.
// Uzumore first: it darkens what is close to the camera and fades Macaron_Apply's occlusion out there.
#define BEFORE_FOG Macaron_UzumoreFS(fd); Macaron_Apply(fd);

// Occlusion on Reflections: remembers the color right before reflection / MatCap and right before emission, so the
// part added in between (reflection, MatCap, rim light, glitter) can be occluded separately.
#define BEFORE_REFLECTION Macaron_MarkSpecStart(fd);
#define BEFORE_MATCAP Macaron_MarkSpecStart(fd);
#define BEFORE_EMISSION_1ST Macaron_MarkSpecEnd(fd);

// Light Adjuster (animated by the Macaron Light Adjuster menu; neutral defaults = no change).
// BEFORE_SHADOW: right before lilToon's lighting in the normal / lite / fur forward passes (not Gem).
// BEFORE_OUTLINE_COLOR: the outline, which uses fd.lightColor later.
#define BEFORE_SHADOW Macaron_Adjust(fd);
#define BEFORE_OUTLINE_COLOR Macaron_AdjustLightColor(fd);
