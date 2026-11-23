#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace liltoon_MCR
{
    /// <summary>
    /// Macaron Uzumore (per-avatar setup)
    /// Per-avatar setup of the Uzumore feature of the Macaron shader (custom_insert.hlsl, Macaron_UzumoreOS). It is on by
    /// default (shader default and every quality tier) and switched in Batch Settings; this sets the per-avatar values:
    ///  - every Macaron material: Reach from the avatar's eye height, Layer Offset by mesh (inner meshes larger),
    ///    avatar axis written (Back Side Reject uses it).
    /// Own view: the Macaron Light Adjuster (MacaronLightAdjuster.cs, added by the Converter by default) carries one more
    /// FX layer, built by AddIsLocalLayer below: it sets _MCR_UzuIsLocal to 1 on the wearer's own client only (VRChat's
    /// built-in IsLocal, no synced parameter, no menu item). Unless the material option "In Own View" (_MCR_UzuSelf) is
    /// on, the wearer's own camera is then not pushed back. Without the Light Adjuster the own view is pushed back too.
    /// The original article's issues and how they are handled here:
    ///  - render mode reset by a shader switch: no shader switch, Uzumore is part of Macaron;
    ///  - neck in the bottom of the own view: own view off by default (Light Adjuster), or the Uzumore mask;
    ///  - skin through clothes: every material of the avatar, layer offsets by mesh;
    ///  - no darkening when buried: Brightness (only the pushed band, not lilToon's distance fade);
    ///  - blocking the view of a player behind the avatar: Reach from the eye height, Back Side Reject.
    /// </summary>
    public static class MacaronUzumore
    {
        // Object of an earlier test build (separate IsLocal object); removed by Setup, the Light Adjuster does it now.
        private const string LegacyObjectName = "Macaron Uzumore";
        public const string PropEnable = "_MCR_UzuEnable";
        public const string PropAmount = "_MCR_UzuAmount";
        public const string PropBias = "_MCR_UzuBias";
        public const string PropIsLocal = "_MCR_UzuIsLocal";
        // Material override tag written by Setup: the per-avatar values have been set (Batch Settings checks it).
        public const string SetupTag = "MCR_UzuSetup";

        // Layer offsets (m): outer meshes small, inner meshes larger, so an outer layer always lands in front.
        public const float BiasOuter = 0.001f;    // hair, accessories
        public const float BiasMiddle = 0.0015f;  // clothes and everything else
        public const float BiasInner = 0.002f;    // body / skin / face

        // Reach = eye height x this, clamped. 0.15 m for an eye height of 1.3 m (the original recommends 0.1 - 0.2).
        private const float ReachPerEyeHeight = 0.115f;
        private const float ReachMin = 0.06f, ReachMax = 0.25f;

        /// <summary>Per-avatar setup of Uzumore: Reach and Layer Offset of every Macaron material and the avatar axis.
        /// The on / off switch itself is left alone (quality tiers turn it on, the user switches it in Batch Settings).
        /// Called by the Converter, and by Batch Settings when it is turned on for materials that were never set up.
        /// Returns a message for the user.</summary>
        public static string Setup(GameObject avatar) { return Setup(avatar, null); }

        /// <summary>Setup that leaves the given materials alone (the Converter's excluded materials).</summary>
        public static string Setup(GameObject avatar, ICollection<Material> exclude)
        {
            if(avatar == null) return "";
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Macaron Uzumore");
            int group = Undo.GetCurrentGroup();

            float reach = ReachFor(avatar);
            Dictionary<Material, Renderer> materials = MaterialsOf(avatar);
            if(exclude != null) foreach(Material x in exclude) if(x != null) materials.Remove(x);
            int count = 0;
            foreach(KeyValuePair<Material, Renderer> kv in materials)
            {
                Material m = kv.Key;
                if(!m.HasProperty(PropEnable)) continue;
                // The shader does not compile Uzumore into Fur (the shells would stick out of a pushed surface).
                if(m.shader.name.IndexOf("Fur", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Undo.RecordObject(m, "Macaron Uzumore");
                m.SetFloat(PropAmount, reach);
                m.SetFloat(PropBias, BiasFor(kv.Value));
                m.SetOverrideTag(SetupTag, "1");
                EditorUtility.SetDirty(m);
                count++;
            }
            // Back Side Reject reads the avatar axis.
            MacaronCore.WriteAvatarBasisForAvatar(avatar, new List<Material>(materials.Keys));

            // The separate IsLocal object of an earlier test build: the Light Adjuster has that layer now.
            for(int i = avatar.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = avatar.transform.GetChild(i);
                if(c.name == LegacyObjectName) Undo.DestroyObjectImmediate(c.gameObject);
            }
            Undo.CollapseUndoOperations(group);

            if(count == 0) return ML.T("這個角色沒有 Macaron 材質，沒有設定埋入。");
            return ML.F("已設定 {0} 個材質的埋入（推回範圍 {1:0.00} m，可在批次設定中開關）。", count, reach);
        }

        /// <summary>True when every given Macaron material has been through Setup.</summary>
        public static bool IsSetUp(IEnumerable<Material> materials)
        {
            foreach(Material m in materials)
                if(m != null && m.HasProperty(PropEnable) && m.GetTag(SetupTag, false, "") != "1") return false;
            return true;
        }

        /// <summary>True when one of the renderers has a Macaron material with the IsLocal property (the Light Adjuster then
        /// adds the IsLocal layer, used by Uzumore and by Local Only, mcr_local_only.hlsl).</summary>
        public static bool Uses(IEnumerable<Renderer> renderers)
        {
            foreach(Renderer r in renderers)
                foreach(Material m in r.sharedMaterials)
                    if(MacaronCore.IsMacaronMaterial(m) && m.HasProperty(PropIsLocal)) return true;
            return false;
        }

        // Macaron materials of the avatar with the first renderer that uses each (SkinnedMeshRenderers first).
        private static Dictionary<Material, Renderer> MaterialsOf(GameObject avatar)
        {
            Dictionary<Material, Renderer> result = new Dictionary<Material, Renderer>();
            foreach(Renderer r in MacaronCore.AvatarRenderers(avatar))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(MacaronCore.IsMacaronMaterial(m) && !result.ContainsKey(m)) result.Add(m, r);
                }
            }
            return result;
        }

        //------------------------------------------------------------------------------------------
        // Values

        /// <summary>Reach from the avatar's eye height (VRC Avatar Descriptor view position, else 93% of the bounds height).</summary>
        public static float ReachFor(GameObject avatar)
        {
            float eye = -1f;
#if VRC_SDK_VRCSDK3
            VRCAvatarDescriptor d = avatar.GetComponent<VRCAvatarDescriptor>();
            if(d != null) eye = d.ViewPosition.y * avatar.transform.lossyScale.y;
#endif
            if(!(eye > 0.1f))
            {
                bool any = false;
                Bounds b = new Bounds();
                foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(false))
                {
                    if(!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                eye = any ? (b.max.y - avatar.transform.position.y) * 0.93f : 1.3f;
            }
            return Mathf.Clamp(eye * ReachPerEyeHeight, ReachMin, ReachMax);
        }

        private static readonly string[] InnerNames = { "body", "skin", "face", "head", "素体", "素體", "体", "顔", "肌" };
        private static readonly string[] OuterNames = { "hair", "髪", "前髪", "後髪", "bang", "accessor", "acce", "ribbon", "リボン", "hat", "帽" };

        /// <summary>Layer Offset of a mesh: body / face larger (inner), hair and accessories small (outer), clothes between.</summary>
        public static float BiasFor(Renderer r)
        {
            if(r == null) return BiasMiddle;
            string name = r.name;
            Mesh mesh = r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : null;
            if(mesh == null && r is MeshRenderer)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if(f != null) mesh = f.sharedMesh;
            }
            string meshName = mesh != null ? mesh.name : "";
            if(Has(name, OuterNames) || Has(meshName, OuterNames)) return BiasOuter;
            if(Has(name, InnerNames) || Has(meshName, InnerNames)) return BiasInner;
            return BiasMiddle;
        }

        private static bool Has(string s, string[] parts)
        {
            if(string.IsNullOrEmpty(s)) return false;
            foreach(string p in parts) if(s.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        //------------------------------------------------------------------------------------------
        // IsLocal layer (part of the Macaron Light Adjuster controller)

        /// <summary>Adds the layer "Macaron Uzumore IsLocal" (and the Bool parameter IsLocal, VRChat built-in) to a
        /// controller: Remote (default, property 0) &lt;-&gt; Local (property 1). The clips are stored in the controller.</summary>
        public static void AddIsLocalLayer(AnimatorController controller, Transform avatarRoot, List<Renderer> renderers)
        {
            bool hasParam = false;
            foreach(AnimatorControllerParameter p in controller.parameters) if(p.name == "IsLocal") { hasParam = true; break; }
            if(!hasParam) controller.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);

            const string layerName = "Macaron Uzumore IsLocal";
            controller.AddLayer(layerName);
            AnimatorControllerLayer[] ls = controller.layers;
            int index = ls.Length - 1;

            AnimationClip remote = MakeClip(avatarRoot, renderers, "Uzumore Remote", 0f);
            AnimationClip local = MakeClip(avatarRoot, renderers, "Uzumore Local", 1f);
            AssetDatabase.AddObjectToAsset(remote, controller);
            AssetDatabase.AddObjectToAsset(local, controller);

            ls[index].defaultWeight = 1f;
            AnimatorStateMachine sm = ls[index].stateMachine;
            AnimatorState sRemote = sm.AddState("Remote");
            sRemote.motion = remote;
            sRemote.writeDefaultValues = false;   // MA "Match Avatar Write Defaults" adjusts this at build
            AnimatorState sLocal = sm.AddState("Local");
            sLocal.motion = local;
            sLocal.writeDefaultValues = false;
            sm.defaultState = sRemote;
            AnimatorStateTransition toLocal = sRemote.AddTransition(sLocal);
            toLocal.hasExitTime = false; toLocal.duration = 0f;
            toLocal.AddCondition(AnimatorConditionMode.If, 0f, "IsLocal");
            AnimatorStateTransition toRemote = sLocal.AddTransition(sRemote);
            toRemote.hasExitTime = false; toRemote.duration = 0f;
            toRemote.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsLocal");
            controller.layers = ls;
        }

        private static AnimationClip MakeClip(Transform avatarRoot, List<Renderer> renderers, string name, float value)
        {
            AnimationClip clip = new AnimationClip { name = name };
            AnimationCurve curve = AnimationCurve.Constant(0f, 1f / 60f, value);
            foreach(Renderer r in renderers)
            {
                string p = AnimationUtility.CalculateTransformPath(r.transform, avatarRoot);
                clip.SetCurve(p, r.GetType(), "material." + PropIsLocal, curve);
            }
            return clip;
        }
    }
}
#endif
