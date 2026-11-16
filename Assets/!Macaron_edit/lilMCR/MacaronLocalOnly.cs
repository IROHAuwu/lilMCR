#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace liltoon_MCR
{
    /// <summary>
    /// Local Only (僅自己可見), editor preview.
    ///
    /// With the material option "Local Only" (_MCR_LocalOnly) every lilMCR feature is drawn only where _MCR_UzuIsLocal is
    /// 1, i.e. on the wearer's own client (IsLocal layer of the Macaron Light Adjuster, MacaronUzumore.AddIsLocalLayer).
    /// Outside play mode no animator runs, so the shader global _lilMCR_EditorView (mcr_local_only.hlsl) is set to 1 to
    /// keep the effect visible while editing. In play mode it is 0, so the animator decides (Gesture Manager /
    /// Av3Emulator: the main avatar is local, a remote clone is not). VRChat never sets it.
    /// </summary>
    [InitializeOnLoad]
    public static class MacaronLocalOnly
    {
        public const string Prop = "_MCR_LocalOnly";
        public const string EditorViewGlobal = "_lilMCR_EditorView";

        static MacaronLocalOnly()
        {
            Apply();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            Apply();
        }

        private static void Apply()
        {
            bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
            Shader.SetGlobalFloat(EditorViewGlobal, playing ? 0f : 1f);
        }
    }
}
#endif
