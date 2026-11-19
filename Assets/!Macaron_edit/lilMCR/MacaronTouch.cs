#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
#endif

namespace liltoon_MCR
{
    // Touch Dent (_MCR_TouchEnable, Macaron_TouchOS in custom_insert.hlsl): the fingers of other players press the
    // surface in.
    //
    // Builds "MCR_Touch" under the avatar root: Contact Receivers with "Write to Shaders" (VRChat SDK 3.10.6 or newer)
    // for the tag "Finger", which the standard finger colliders of every avatar carry, so the other player needs
    // nothing. Each receiver writes the senders in contact (at most 8) to up to 4 renderers that use Macaron
    // materials, and Touch Dent is turned on in those materials.
    public static class MacaronTouch
    {
        public const string ObjectName = "MCR_Touch";
        public const string ShaderParameter = "lilMCRTouch";    // _VRChatContact_Transforms_lilMCRTouch in the shader
        public const string FingerTag = "Finger";
        private const string DialogTitle = "lilMCR Touch Dent";
        private const string PrefSelf = "Macaron.Touch.AllowSelf";
        private const int RenderersPerReceiver = 4;
        private const int MaxReceivers = 6;

        public struct Result
        {
            public bool Ok;
            public int Receivers;
            public int Renderers;
            public int SkippedRenderers;
            public int Materials;
            public bool AllowSelf;
            public string Error;
        }

        // The receiver fields of "Write to Shaders" exist (VRChat SDK 3.10.6 or newer).
        public static bool Supported
        {
            get
            {
#if VRC_SDK_VRCSDK3
                return typeof(ContactReceiver).GetField("writeToMaterials", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
#else
                return false;
#endif
            }
        }

        public static Transform Find(GameObject avatar)
        {
            if(avatar == null) return null;
            for(int i = 0; i < avatar.transform.childCount; i++)
            {
                Transform c = avatar.transform.GetChild(i);
                if(c.name == ObjectName) return c;
            }
            return null;
        }

        // Removes MCR_Touch and turns Touch Dent off in the avatar's Macaron materials.
        public static bool Remove(GameObject avatar)
        {
            Transform t = Find(avatar);
            if(t == null) return false;
            Undo.DestroyObjectImmediate(t.gameObject);
            SetEnabled(MacaronRenderers(avatar), false);
            return true;
        }

        private static List<Renderer> MacaronRenderers(GameObject avatar)
        {
            List<Renderer> renderers = new List<Renderer>();
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                foreach(Material m in r.sharedMaterials)
                {
                    if(MacaronCore.IsMacaronMaterial(m)) { renderers.Add(r); break; }
                }
            }
            return renderers;
        }

        private static int SetEnabled(List<Renderer> renderers, bool on)
        {
            HashSet<Material> done = new HashSet<Material>();
            foreach(Renderer r in renderers)
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(!MacaronCore.IsMacaronMaterial(m) || !m.HasProperty("_MCR_TouchEnable") || !done.Add(m)) continue;
                    if(m.GetFloat("_MCR_TouchEnable") > 0.5f == on) continue;
                    Undo.RecordObject(m, "lilMCR Touch Dent");
                    m.SetFloat("_MCR_TouchEnable", on ? 1f : 0f);
                    EditorUtility.SetDirty(m);
                }
            }
            return done.Count;
        }

        private static bool RendererShown(Renderer r)
        {
            return r.enabled && r.gameObject.activeInHierarchy && !r.CompareTag("EditorOnly");
        }

        private static int RendererVertexCount(Renderer r)
        {
            Mesh mesh = null;
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if(smr != null) mesh = smr.sharedMesh;
            else
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if(filter != null) mesh = filter.sharedMesh;
            }
            return mesh != null ? mesh.vertexCount : 0;
        }

#if VRC_SDK_VRCSDK3
        private static bool SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if(p == null) return false;
            p.boolValue = value;
            return true;
        }

        private static float MaxScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        private static bool AddReceiver(Transform holder, string name, Transform root, float worldRadius, bool allowSelf, List<Renderer> renderers, int first)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(holder, false);
            VRCContactReceiver receiver = go.AddComponent<VRCContactReceiver>();
            receiver.rootTransform = root;
            receiver.shapeType = ContactBase.ShapeType.Sphere;
            receiver.radius = Mathf.Min(worldRadius, 3f) / Mathf.Max(MaxScale(root), 1e-4f);
            receiver.position = Vector3.zero;
            receiver.rotation = Quaternion.identity;
            receiver.localOnly = false;                 // everyone sees the dent, not only the wearer
            receiver.allowSelf = allowSelf;
            receiver.allowOthers = true;
            receiver.receiverType = ContactReceiver.ReceiverType.Proximity;
            receiver.parameter = string.Empty;          // no animator parameter: shader properties only
            receiver.collisionTags = new List<string> { FingerTag };

            // "Write to Shaders" fields by name, so that this file still compiles with an older SDK.
            SerializedObject so = new SerializedObject(receiver);
            bool ok = SetBool(so, "writeToMaterials", true);
            SerializedProperty parameter = so.FindProperty("separateShaderParameter");
            SerializedProperty targets = so.FindProperty("shaderTargetRenderers");
            if(parameter == null || targets == null || !targets.isArray) ok = false;
            if(ok)
            {
                parameter.stringValue = ShaderParameter;
                int count = Mathf.Min(RenderersPerReceiver, renderers.Count - first);
                targets.arraySize = count;
                for(int i = 0; i < count; i++) targets.GetArrayElementAtIndex(i).objectReferenceValue = renderers[first + i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(receiver);
            return ok;
        }
#endif

        // Rebuilds MCR_Touch for the avatar (an existing one is replaced) and turns Touch Dent on in its materials.
        // allowSelf: the wearer's own fingers dent the surface too (handy to test alone; neighboring fingers and a
        // closed fist then press on each other as well).
        public static Result Setup(GameObject avatar, bool allowSelf)
        {
            Result result = new Result();
            result.AllowSelf = allowSelf;
#if VRC_SDK_VRCSDK3
            if(avatar == null) { result.Error = ML.T("沒有選擇角色。"); return result; }
            if(!Supported) { result.Error = ML.T("需要 VRChat SDK 3.10.6 以上。"); return result; }

            List<Renderer> renderers = MacaronRenderers(avatar);
            if(renderers.Count == 0) { result.Error = ML.T("角色上沒有使用 Macaron 材質的 Renderer。"); return result; }
            // When there are more renderers than the receivers can write to: the ones shown now first, then the
            // largest meshes (hierarchy order for equal keys).
            List<Renderer> ordered = new List<Renderer>(renderers);
            renderers.Sort((x, y) =>
            {
                int byShown = RendererShown(y).CompareTo(RendererShown(x));
                if(byShown != 0) return byShown;
                int bySize = RendererVertexCount(y).CompareTo(RendererVertexCount(x));
                return bySize != 0 ? bySize : ordered.IndexOf(x).CompareTo(ordered.IndexOf(y));
            });

            // Center and size of the receivers: the chest and the height of the avatar when it is humanoid, else the
            // bounds of its renderers.
            Transform center = avatar.transform;
            Bounds bounds = new Bounds(avatar.transform.position, Vector3.zero);
            foreach(Renderer r in renderers) if(RendererShown(r)) bounds.Encapsulate(r.bounds);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.3f);
            Animator animator = avatar.GetComponent<Animator>();
            if(animator != null && animator.isHuman)
            {
                Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if(chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if(chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if(chest != null && head != null)
                {
                    center = chest;
                    radius = Mathf.Max(Vector3.Dot(head.position - avatar.transform.position, avatar.transform.up) * 1.25f, 0.3f);
                }
            }

            Transform old = Find(avatar);
            if(old != null) Undo.DestroyObjectImmediate(old.gameObject);
            GameObject holderObject = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(holderObject, "lilMCR Touch Dent");
            holderObject.transform.SetParent(avatar.transform, false);

            int receivers = 0;
            bool fieldsOk = true;
            for(int first = 0; first < renderers.Count && receivers < MaxReceivers; first += RenderersPerReceiver)
            {
                fieldsOk &= AddReceiver(holderObject.transform, "Receiver " + (receivers + 1), center, radius, allowSelf, renderers, first);
                receivers++;
            }
            if(!fieldsOk)
            {
                Undo.DestroyObjectImmediate(holderObject);
                result.Error = ML.T("這個版本的 VRChat SDK 沒有「Write to Shaders」的欄位。");
                return result;
            }

            result.Ok = true;
            result.Receivers = receivers;
            result.Renderers = Mathf.Min(renderers.Count, receivers * RenderersPerReceiver);
            result.SkippedRenderers = renderers.Count - result.Renderers;
            result.Materials = SetEnabled(renderers.GetRange(0, result.Renderers), true);
#else
            result.Error = ML.T("需要 VRChat SDK。");
#endif
            return result;
        }

        public static string ResultMessage(Result r)
        {
            if(!r.Ok) return r.Error ?? string.Empty;
            string message = string.Format(ML.T("已建立 MCR_Touch：{0} 個 Receiver，寫入 {1} 個 Renderer，{2} 個材質開啟觸碰凹陷。"), r.Receivers, r.Renderers, r.Materials);
            message += "\n" + (r.AllowSelf ? ML.T("自己的手指也會造成凹陷。") : ML.T("只有別人的手指會造成凹陷。"));
            if(r.SkippedRenderers > 0) message += "\n" + string.Format(ML.T("有 {0} 個 Renderer 超過上限，沒有寫入（不會凹陷）。"), r.SkippedRenderers);
            message += "\n" + ML.T("凹陷只有在 Play 模式和 VRChat 裡看得到。");
            return message;
        }

        //------------------------------------------------------------------------------------------
        // Menu

        private static GameObject SelectedAvatar()
        {
            GameObject go = Selection.activeGameObject;
            if(go == null || EditorUtility.IsPersistent(go)) return null;
            Transform root = MacaronCore.FindAvatarRoot(go.transform);
            return root != null ? root.gameObject : null;
        }

        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Add or Update 追加・更新 (Selected Avatar)", false, 80)]
        private static void MenuAdd()
        {
            GameObject avatar = SelectedAvatar();
            if(avatar == null) return;
            EditorUtility.DisplayDialog(DialogTitle, ResultMessage(Setup(avatar, EditorPrefs.GetBool(PrefSelf, false))), "OK");
        }

        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Remove 削除 (Selected Avatar)", false, 81)]
        private static void MenuRemove()
        {
            GameObject avatar = SelectedAvatar();
            if(avatar == null) return;
            EditorUtility.DisplayDialog(DialogTitle, Remove(avatar) ? ML.T("已移除 MCR_Touch，並關閉材質的觸碰凹陷。") : ML.T("這個角色沒有 MCR_Touch。"), "OK");
        }

        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Add or Update 追加・更新 (Selected Avatar)", true)]
        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Remove 削除 (Selected Avatar)", true)]
        private static bool MenuValidate()
        {
            return SelectedAvatar() != null;
        }

        private const string SelfMenu = MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Own Fingers Too 自分の指も (next Add or Update)";

        [MenuItem(SelfMenu, false, 95)]
        private static void MenuSelf()
        {
            EditorPrefs.SetBool(PrefSelf, !EditorPrefs.GetBool(PrefSelf, false));
        }

        [MenuItem(SelfMenu, true)]
        private static bool MenuSelfValidate()
        {
            Menu.SetChecked(SelfMenu, EditorPrefs.GetBool(PrefSelf, false));
            return true;
        }
    }
}
#endif
