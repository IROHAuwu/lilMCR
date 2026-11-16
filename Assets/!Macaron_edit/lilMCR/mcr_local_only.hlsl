#ifndef MCR_LOCAL_ONLY_INCLUDED
#define MCR_LOCAL_ONLY_INCLUDED
// Local Only (僅自己可見): every lilMCR feature is drawn on the wearer's own client only. Other players see the material
// as plain lilToon (no occlusion, contact shadow, Uzumore, Light Adjuster, World Shadow or lilMCR PCSS).
//
//   - _MCR_LocalOnly: material switch (not animated).
//   - _MCR_AdjLocalOnly: the same from the avatar menu (Light Adjuster item "僅自己可見", synced Bool, optional).
//   - _MCR_UzuIsLocal: 1 only on the wearer's own client, set by the IsLocal layer of the Macaron Light Adjuster
//     (VRChat's built-in IsLocal, MacaronUzumore.AddIsLocalLayer). Its stored value stays 0, so a client whose animator
//     does not run (no Light Adjuster, animations blocked by Safety) shows nothing: the safe side.
//   - _lilMCR_EditorView: shader global set to 1 by MacaronLocalOnly.cs in Unity's edit mode only, so the effect can be
//     previewed while editing. Nothing sets it in VRChat (avatars cannot set shader globals).
// Uniform per draw call: every caller branches on it before any heavy work.
// Shared by the forward passes (custom_insert.hlsl), the world capture (mcr_world_capture_match.hlsl) and lilMCR PCSS.
float _lilMCR_EditorView;

bool Macaron_LocalVisible()
{
    return (_MCR_LocalOnly < 0.5 && _MCR_AdjLocalOnly < 0.5) || _MCR_UzuIsLocal > 0.5 || _lilMCR_EditorView > 0.5;
}
#endif
