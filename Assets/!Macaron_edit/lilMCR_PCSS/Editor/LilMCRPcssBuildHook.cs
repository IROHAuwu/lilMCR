#if UNITY_EDITOR && VRC_SDK_VRCSDK3
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace lilMCR.PCSS
{
    /// <summary>
    /// "Editor preview" adds the Default layer to the dedicated light so the shadows show on the avatar in the Unity
    /// editor. Uploaded like that, the light would also cover the Default layer in VRChat: world objects inside its cone
    /// are drawn once more and cast into the avatar's shadow map.
    /// The avatar that is built (Build &amp; Test / Build &amp; Publish) therefore always gets the runtime layers only. The
    /// avatar in the scene is not touched: the preview stays as the user left it.
    /// Play mode (Gesture Manager, Av3Emulator) is left alone: there the avatar is on the Default layer and the preview is
    /// what makes the shadows visible.
    /// </summary>
    public class LilMCRPcssBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder { get { return 0; } }

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if(avatarGameObject == null || EditorApplication.isPlayingOrWillChangePlaymode) return true;
            try
            {
                if(LilMCRPcssSetup.ClearEditorPreviewForBuild(avatarGameObject))
                    Debug.Log("[lilMCR PCSS] " + avatarGameObject.name + ": editor preview was on. The uploaded avatar uses the VRChat layers only (the scene is unchanged).");
            }
            catch(System.Exception e)
            {
                // Never stop a build because of this.
                Debug.LogWarning("[lilMCR PCSS] " + e.Message);
            }
            return true;
        }
    }
}
#endif
