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
        public const string PrefSelf = "Macaron.Touch.AllowSelf";
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

        // The renderers the avatar's MCR_Touch writes to now, and whether it takes the wearer's own fingers.
        public static HashSet<Renderer> CurrentTargets(GameObject avatar, out bool allowSelf)
        {
            HashSet<Renderer> targets = new HashSet<Renderer>();
            allowSelf = false;
            Transform holder = Find(avatar);
            if(holder == null) return targets;
#if VRC_SDK_VRCSDK3
            foreach(VRCContactReceiver receiver in holder.GetComponentsInChildren<VRCContactReceiver>(true))
            {
                allowSelf |= receiver.allowSelf;
                SerializedProperty list = new SerializedObject(receiver).FindProperty("shaderTargetRenderers");
                if(list == null || !list.isArray) continue;
                for(int i = 0; i < list.arraySize; i++)
                {
                    Renderer r = list.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                    if(r != null) targets.Add(r);
                }
            }
#endif
            return targets;
        }

        public const int MaxRenderers = RenderersPerReceiver * MaxReceivers;

        // Removes MCR_Touch and turns Touch Dent off in the avatar's Macaron materials.
        public static bool Remove(GameObject avatar)
        {
            Transform t = Find(avatar);
            if(t == null) return false;
            Undo.DestroyObjectImmediate(t.gameObject);
            SetEnabled(MacaronRenderers(avatar), false);
            return true;
        }

        public static List<Renderer> MacaronRenderers(GameObject avatar)
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

        public static int RendererVertexCount(Renderer r)
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
            return Setup(avatar, allowSelf, null);
        }

        // only: the renderers to dent (null = every renderer with a Macaron material). Touch Dent is turned off in the
        // materials no chosen renderer uses.
        public static Result Setup(GameObject avatar, bool allowSelf, ICollection<Renderer> only)
        {
            Result result = new Result();
            result.AllowSelf = allowSelf;
#if VRC_SDK_VRCSDK3
            if(avatar == null) { result.Error = ML.T("沒有選擇角色。"); return result; }
            if(!Supported) { result.Error = ML.T("需要 VRChat SDK 3.10.6 以上。"); return result; }

            List<Renderer> all = MacaronRenderers(avatar);
            if(all.Count == 0) { result.Error = ML.T("角色上沒有使用 Macaron 材質的 Renderer。"); return result; }
            List<Renderer> renderers = only == null ? new List<Renderer>(all) : all.FindAll(r => only.Contains(r));
            if(renderers.Count == 0) { result.Error = ML.T("沒有勾選任何網格。"); return result; }
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
            List<Renderer> written = renderers.GetRange(0, result.Renderers);
            result.Materials = SetEnabled(written, true);
            // Off in the materials only the other renderers use (a material shared with a chosen renderer stays on:
            // a renderer no receiver writes to gets no contact data and does not move).
            HashSet<Material> kept = new HashSet<Material>();
            foreach(Renderer r in written) foreach(Material m in r.sharedMaterials) if(m != null) kept.Add(m);
            foreach(Renderer r in all)
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || kept.Contains(m) || !MacaronCore.IsMacaronMaterial(m) || !m.HasProperty("_MCR_TouchEnable") || m.GetFloat("_MCR_TouchEnable") < 0.5f) continue;
                    Undo.RecordObject(m, "lilMCR Touch Dent");
                    m.SetFloat("_MCR_TouchEnable", 0f);
                    EditorUtility.SetDirty(m);
                }
            }
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

        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Add or Update (All Meshes) 追加・更新 (Selected Avatar)", false, 80)]
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

        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Add or Update (All Meshes) 追加・更新 (Selected Avatar)", true)]
        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Remove 削除 (Selected Avatar)", true)]
        private static bool MenuValidate()
        {
            return SelectedAvatar() != null;
        }

        // A stand-in for another player's finger, to try Touch Dent in Play mode: a Contact Sender with the tag
        // "Finger" (capsule, 0.9 cm x 6 cm, along its Y axis) and a visible capsule of the same size. Placed at the
        // scene root, so the receivers take it as someone else's. Not part of the avatar: delete it before uploading.
        public const string TestFingerName = "MCR Test Finger";

        [MenuItem(MacaronCore.MenuRoot + "Touch Dent 触れ凹み/Create Test Finger テスト用の指を作成", false, 96)]
        private static void MenuTestFinger()
        {
#if VRC_SDK_VRCSDK3
            GameObject go = new GameObject(TestFingerName);
            Undo.RegisterCreatedObjectUndo(go, "lilMCR Test Finger");
            GameObject avatar = SelectedAvatar();
            Animator animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if(head != null)
            {
                // In front of the face, pointing at it.
                go.transform.position = head.position + avatar.transform.forward * 0.25f + avatar.transform.up * 0.03f;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, -avatar.transform.forward);
            }
            VRCContactSender sender = go.AddComponent<VRCContactSender>();
            sender.shapeType = ContactBase.ShapeType.Capsule;
            sender.radius = 0.009f;
            sender.height = 0.06f;
            sender.collisionTags = new List<string> { FingerTag };
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(0.018f, 0.03f, 0.018f);
            Selection.activeGameObject = go;
#else
            EditorUtility.DisplayDialog(DialogTitle, ML.T("需要 VRChat SDK。"), "OK");
#endif
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

    // Touch Dent setup window: every mesh of the avatar as a button. Hovering one lights it up in the Scene view,
    // clicking checks it, and "Apply" builds MCR_Touch for the checked meshes only.
    public class MacaronTouchWindow : EditorWindow
    {
        [SerializeField] private GameObject avatar;
        [SerializeField] private List<Renderer> chosen = new List<Renderer>();
        [SerializeField] private bool allowSelf;
        [SerializeField] private Vector2 scroll;
        private GameObject listedAvatar;
        private readonly List<Renderer> macaron = new List<Renderer>();     // can dent
        private readonly List<Renderer> others = new List<Renderer>();      // not converted to Macaron
        private Renderer hover;
        private Renderer bakedFor;
        private Mesh baked;
        private Material highlight;
        private string resultMessage;
        private bool resultOk;

        [MenuItem("Macaron/lilMCR/Touch Dent 触れ凹み", false, 3)]
        public static void Open()
        {
            MacaronTouchWindow w = GetWindow<MacaronTouchWindow>();
            w.titleContent = new GUIContent(ML.T("Macaron 觸碰凹陷"));
            w.minSize = new Vector2(380f, 520f);
            if(Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject) && w.avatar == null)
            {
                Transform root = MacaronCore.FindAvatarRoot(Selection.activeGameObject.transform);
                w.avatar = root != null ? root.gameObject : null;
            }
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += WatchMouse;
            listedAvatar = null;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= WatchMouse;
            SetHover(null);
            if(baked != null) DestroyImmediate(baked);
            if(highlight != null) DestroyImmediate(highlight);
        }

        // The highlight goes away when the pointer leaves the window.
        private void WatchMouse()
        {
            if(hover != null && mouseOverWindow != this) { SetHover(null); Repaint(); }
        }

        private void SetHover(Renderer r)
        {
            if(hover == r) return;
            hover = r;
            SceneView.RepaintAll();
        }

        private void Relist()
        {
            listedAvatar = avatar;
            macaron.Clear();
            others.Clear();
            chosen.RemoveAll(r => r == null);
            if(avatar == null) return;
            HashSet<Renderer> canDent = new HashSet<Renderer>(MacaronTouch.MacaronRenderers(avatar));
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if(MacaronTouch.RendererVertexCount(r) == 0) continue;
                if(canDent.Contains(r)) macaron.Add(r); else others.Add(r);
            }
            // Start from what the avatar's MCR_Touch already writes to.
            bool self;
            HashSet<Renderer> current = MacaronTouch.CurrentTargets(avatar, out self);
            chosen.Clear();
            foreach(Renderer r in macaron) if(current.Contains(r)) chosen.Add(r);
            if(current.Count > 0) allowSelf = self;
            else allowSelf = EditorPrefs.GetBool(MacaronTouch.PrefSelf, false);
        }

        private void OnGUI()
        {
            titleContent.text = ML.T("Macaron 觸碰凹陷");
            MacaronGUI.Banner("Macaron", ML.T("選出要被手指壓凹的網格，按一下就完成。"));
            ML.LanguagePopup();

            if(!MacaronTouch.Supported)
            {
                EditorGUILayout.HelpBox(ML.T("觸碰凹陷需要 VRChat SDK 3.10.6 以上（Contact Receiver 的 Write to Shaders）。"), MessageType.Warning);
                return;
            }

            // 1. Avatar
            MacaronGUI.Step(1, ML.T("選擇角色"));
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true, GUILayout.Height(20f));
            if(EditorGUI.EndChangeCheck()) resultMessage = null;
            if(avatar != null && EditorUtility.IsPersistent(avatar))
            {
                EditorGUILayout.HelpBox(ML.T("請放入場景（Hierarchy）中的角色，而不是 Project 裡的 Prefab。"), MessageType.Error);
                return;
            }
            if(avatar != listedAvatar) Relist();
            if(avatar == null)
            {
                EditorGUILayout.LabelField(ML.T("從 Hierarchy 拖曳角色到上方欄位。"), EditorStyles.wordWrappedMiniLabel);
                SetHover(null);
                return;
            }

            // 2. Meshes
            MacaronGUI.Step(2, ML.T("勾選要凹陷的網格"));
            EditorGUILayout.LabelField(ML.T("滑鼠移到按鈕上，Scene 視窗裡對應的網格會亮起來；確認是要的部位後點一下勾選。"), EditorStyles.wordWrappedMiniLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button(ML.T("全選"), EditorStyles.miniButtonLeft)) { chosen.Clear(); chosen.AddRange(macaron); }
                if(GUILayout.Button(ML.T("全不選"), EditorStyles.miniButtonMid)) chosen.Clear();
                if(GUILayout.Button(ML.T("重新整理"), EditorStyles.miniButtonRight)) Relist();
            }

            Renderer over = null;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if(macaron.Count == 0)
            {
                EditorGUILayout.HelpBox(ML.T("這個角色沒有使用 Macaron 材質的網格。請先用「Avatar Converter」轉換。"), MessageType.Info);
            }
            foreach(Renderer r in macaron)
            {
                if(r == null) continue;
                bool on = chosen.Contains(r);
                if(DrawRow(r, on, true, ref over))
                {
                    if(on) chosen.Remove(r); else chosen.Add(r);
                }
            }
            if(others.Count > 0)
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField(ML.T("尚未轉換成 Macaron 的網格（不能凹陷）"), EditorStyles.miniBoldLabel);
                foreach(Renderer r in others)
                {
                    if(r != null) DrawRow(r, false, false, ref over);
                }
            }
            EditorGUILayout.EndScrollView();
            if(Event.current.type == EventType.Repaint || Event.current.type == EventType.MouseMove) SetHover(over);
            if(Event.current.type == EventType.MouseMove) Repaint();

            // 3. Apply
            MacaronGUI.Step(3, ML.T("套用"));
            int count = chosen.Count;
            int limit = MacaronTouch.MaxRenderers;
            EditorGUILayout.LabelField(ML.F("已勾選 {0} 個網格（上限 {1} 個），會使用 {2} 個 Contact Receiver。", count, limit, Mathf.Min((count + 3) / 4, limit / 4)), EditorStyles.wordWrappedMiniLabel);
            if(count > limit)
            {
                EditorGUILayout.HelpBox(ML.F("超過上限：只有顯示中、面數最多的 {0} 個會有效果。", limit), MessageType.Warning);
            }
            allowSelf = EditorGUILayout.ToggleLeft(new GUIContent(ML.T("自己的手指也會造成凹陷"), ML.T("方便一個人對著鏡子測試。相鄰的手指和握拳時也會互相壓到。")), allowSelf);

            using(new EditorGUI.DisabledScope(count == 0))
            {
                if(MacaronGUI.BigButton(count > 0 ? ML.F("套用到勾選的網格（{0} 個）", count) : ML.T("套用到勾選的網格"), 36f))
                {
                    EditorPrefs.SetBool(MacaronTouch.PrefSelf, allowSelf);
                    MacaronTouch.Result result = MacaronTouch.Setup(avatar, allowSelf, chosen);
                    resultOk = result.Ok;
                    resultMessage = MacaronTouch.ResultMessage(result);
                }
            }
            using(new EditorGUI.DisabledScope(MacaronTouch.Find(avatar) == null))
            {
                if(GUILayout.Button(ML.T("移除這個角色的觸碰凹陷")))
                {
                    MacaronTouch.Remove(avatar);
                    chosen.Clear();
                    resultOk = true;
                    resultMessage = ML.T("已移除 MCR_Touch，並關閉材質的觸碰凹陷。");
                }
            }
            if(!string.IsNullOrEmpty(resultMessage)) EditorGUILayout.HelpBox(resultMessage, resultOk ? MessageType.Info : MessageType.Error);
            GUILayout.Space(4f);
        }

        // One mesh as a wide toggle button. Returns true when it was clicked; over is set when the pointer is on it.
        private bool DrawRow(Renderer r, bool on, bool enabled, ref Renderer over)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, 26f, GUILayout.ExpandWidth(true));
            rect.xMin += 2f;
            rect.xMax -= 2f;
            bool hovered = rect.Contains(Event.current.mousePosition);
            if(hovered) over = r;
            bool shown = r.enabled && r.gameObject.activeInHierarchy;
            bool clicked = false;
            using(new EditorGUI.DisabledScope(!enabled))
            {
                Color old = GUI.backgroundColor;
                if(on) GUI.backgroundColor = MacaronGUI.Accent;
                clicked = GUI.Button(rect, GUIContent.none);
                GUI.backgroundColor = old;
            }
            if(Event.current.type == EventType.Repaint)
            {
                if(hovered) EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), new Color(1f, 0.75f, 0.2f));
                Rect box = new Rect(rect.x + 8f, rect.y + 5f, 16f, 16f);
                if(enabled) EditorStyles.toggle.Draw(box, GUIContent.none, false, false, on, false);
                Rect label = new Rect(rect.x + 30f, rect.y, rect.width - 150f, rect.height);
                GUIStyle style = new GUIStyle(on ? EditorStyles.boldLabel : EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                if(!shown || !enabled) style.normal.textColor = new Color(style.normal.textColor.r, style.normal.textColor.g, style.normal.textColor.b, 0.5f);
                GUI.Label(label, r.name, style);
                GUIStyle info = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                string note = ML.F("{0} 頂點", MacaronTouch.RendererVertexCount(r)) + (shown ? string.Empty : " · " + ML.T("隱藏中"));
                GUI.Label(new Rect(rect.xMax - 124f, rect.y, 118f, rect.height), note, info);
            }
            return clicked && enabled;
        }

        // Scene view: the hovered mesh, filled with a color that shows through whatever is in front of it.
        private void OnSceneGUI(SceneView view)
        {
            if(hover == null || Event.current.type != EventType.Repaint) return;
            if(highlight == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if(shader == null) return;
                highlight = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                highlight.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                highlight.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                highlight.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                highlight.SetInt("_ZWrite", 0);
                highlight.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            Mesh mesh = null;
            Matrix4x4 matrix = hover.transform.localToWorldMatrix;
            SkinnedMeshRenderer smr = hover as SkinnedMeshRenderer;
            if(smr != null)
            {
                if(smr.sharedMesh == null) return;
                if(baked == null) baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                if(bakedFor != hover)
                {
                    baked.Clear();
                    smr.BakeMesh(baked, true);      // with the renderer's scale: only position and rotation are left
                    bakedFor = hover;
                }
                mesh = baked;
                matrix = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            }
            else
            {
                MeshFilter filter = hover.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }
            if(mesh == null) return;
            highlight.SetColor("_Color", new Color(1f, 0.72f, 0.15f, 0.55f));
            highlight.SetPass(0);
            for(int i = 0; i < mesh.subMeshCount; i++) Graphics.DrawMeshNow(mesh, matrix, i);
        }
    }
}
#endif
