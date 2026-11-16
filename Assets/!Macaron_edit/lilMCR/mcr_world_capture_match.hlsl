#ifndef MCR_WORLD_CAPTURE_MATCH_INCLUDED
#define MCR_WORLD_CAPTURE_MATCH_INCLUDED
#include "mcr_local_only.hlsl"
// True while the material is drawn by its own avatar's World Shadow capture camera: an orthographic camera whose
// size / near / far match _MCR_WorldCapture.xyz (written by the World Shadow tool, capture key in .w), all three
// multiplied by the same avatar scale (the camera follows VRChat's ScaleFactor).
// Shared by the forward passes, the capture pass and the lilMCR PCSS pass.
// Local Only on another player's client: never a capture, so the shadow map holds no data of this avatar and the
// World Shadow projection casts nothing there.
bool Macaron_IsWorldCapture()
{
    if(!Macaron_LocalVisible()) return false;
    if(_MCR_WorldCapture.w < 0.5 || unity_OrthoParams.w < 0.5) return false;
    float k = unity_OrthoParams.y / max(_MCR_WorldCapture.x, 0.000001);
    return k > 0.01 && k < 1000.0
        && abs(_ProjectionParams.y / k - _MCR_WorldCapture.y) < 0.00000003
        && abs(_ProjectionParams.z / k - _MCR_WorldCapture.z) < 0.001;
}
#endif
