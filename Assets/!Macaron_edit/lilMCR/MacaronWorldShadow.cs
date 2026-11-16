#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;
#endif

namespace liltoon_MCR
{
    // Editor-only authoring tool. The generated objects use only Unity / VRChat components (and Modular Avatar for the menu).
    public class MacaronWorldShadow : EditorWindow
    {
        public const string ObjectName = "Macaron World Shadow";
        public const string CapturePath = "Heading/Direction/Capture";
        public const string ProjectionPath = "Heading/Direction/Projection";
        GameObject avatar;
        public const int DefaultResolution = 1024;
        int resolution = DefaultResolution;
        [SerializeField] int directionSource;
        float strength = 0.4f, softness = 1.5f;
        Color tint = new Color(0.35f,0.38f,0.43f,1);
        string status;
        MessageType statusType;
        Vector2 scroll;
        ML.Language statusLanguage;

        // ---- lilMCR PCSS dependency ------------------------------------------------------------
        // World Shadow only works on avatars that use lilMCR PCSS. PCSS is an optional add-on, so it is
        // looked up by reflection (no assembly reference): removing the PCSS folder stays safe.
        const string PcssSetupType = "lilMCR.PCSS.LilMCRPcssSetup, lilMCR.PCSS.Editor";
        const string PcssWindowType = "lilMCR.PCSS.LilMCRPcssWindow, lilMCR.PCSS.Editor";

        // Looked up once per script reload (importing or removing the add-on also reloads scripts).
        static bool pcssLookedUp;
        static Type pcssSetup;
        static System.Reflection.MethodInfo pcssIsPcss;
        static void LookUpPcss()
        {
            if(pcssLookedUp) return;
            pcssLookedUp = true;
            pcssSetup = Type.GetType(PcssSetupType);
            pcssIsPcss = pcssSetup == null ? null : pcssSetup.GetMethod("IsPcss", new[] { typeof(Material) });
        }

        /// <summary>True when the lilMCR PCSS add-on is imported in the project.</summary>
        public static bool PcssInstalled { get { LookUpPcss(); return pcssSetup != null; } }

        /// <summary>True when the avatar has at least one material switched to lilMCR PCSS.</summary>
        public static bool HasPcss(GameObject avatar)
        {
            if(avatar == null) return false;
            LookUpPcss();
            var isPcss = pcssIsPcss;
            if(isPcss == null) return false;
            var previous = avatar.transform.Find(ObjectName);
            foreach(var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(previous != null && r.transform.IsChildOf(previous)) continue;
                foreach(var m in r.sharedMaterials)
                    if(m != null && (bool)isPcss.Invoke(null, new object[] { m })) return true;
            }
            return false;
        }

        static void OpenPcssSetup()
        {
            var type = Type.GetType(PcssWindowType);
            var open = type == null ? null : type.GetMethod("Open", Type.EmptyTypes);
            if(open != null) open.Invoke(null, null);
        }

        // Greys out the menu entry while the lilMCR PCSS add-on is not imported.
        [MenuItem("Macaron/World Shadow ワールド影", true)]
        static bool OpenValidate() { return PcssInstalled; }

        [MenuItem("Macaron/World Shadow ワールド影", false, 50)]
        public static void Open()
        {
            if(!PcssInstalled)
            {
                EditorUtility.DisplayDialog(ML.T("Macaron 世界落影"), ML.T("世界落影需要 lilMCR PCSS。請先匯入 lilMCR_PCSS 資料夾。"), "OK");
                return;
            }
            var w = GetWindow<MacaronWorldShadow>();
            // Unity keeps an EditorWindow's fields (including private ones) across script reloads and layouts, so a
            // window opened before still holds its old value: every open from the menu starts at the default quality.
            w.resolution = DefaultResolution;
            w.titleContent = new GUIContent(ML.T("Macaron 世界落影"));
            w.minSize = new Vector2(420f, 500f);
            var selected = Selection.activeGameObject;
            if(selected != null)
            {
                var root = MacaronCore.FindAvatarRoot(selected.transform);
                w.avatar = root != null ? root.gameObject : selected;
            }
        }

        public static void OpenFor(GameObject target)
        {
            Open();
            if(!PcssInstalled) return;
            GetWindow<MacaronWorldShadow>().avatar = target;
        }

        void OnGUI()
        {
            if(statusLanguage != ML.Current) { status = null; statusLanguage = ML.Current; }
            MacaronGUI.Banner(ML.T("世界落影"), ML.T("預設使用 lilMCR 光照方向，可改為沿用材質方向。PC 支援本地與遠端投影。"));
            if(ML.LanguagePopup()) status = null;
            titleContent.text = ML.T("Macaron 世界落影");
            scroll = EditorGUILayout.BeginScrollView(scroll);
            MacaronGUI.Step(1, ML.T("選擇角色"));
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField(ML.T("角色"), avatar, typeof(GameObject), true);
            if(EditorGUI.EndChangeCheck()) status = null;
            if(avatar == null)
                EditorGUILayout.HelpBox(ML.T("從 Hierarchy 拖曳角色到上方欄位。"), MessageType.Info);
            else if(EditorUtility.IsPersistent(avatar))
                EditorGUILayout.HelpBox(ML.T("請放入場景（Hierarchy）中的角色，而不是 Project 裡的 Prefab。"), MessageType.Warning);

            bool pcssInstalled = PcssInstalled;
            bool pcssReady = pcssInstalled && avatar != null && !EditorUtility.IsPersistent(avatar) && HasPcss(avatar);
            if(!pcssInstalled)
                EditorGUILayout.HelpBox(ML.T("世界落影需要 lilMCR PCSS。請先匯入 lilMCR_PCSS 資料夾。"), MessageType.Error);
            else if(avatar != null && !EditorUtility.IsPersistent(avatar) && !pcssReady)
            {
                EditorGUILayout.HelpBox(ML.T("此角色尚未套用 lilMCR PCSS。請先用 lilMCR PCSS 設定套用後，再建立世界落影。"), MessageType.Warning);
                if(GUILayout.Button(ML.T("開啟 lilMCR PCSS 設定"))) OpenPcssSetup();
            }

            GUILayout.Space(12f);
            MacaronGUI.Step(2, ML.T("世界落影設定"));
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            directionSource = EditorGUILayout.Popup(ML.T("方向來源"), directionSource, ML.TA(new[] { "lilMCR 預設光向（45°）", "沿用材質接觸陰影方向" }));
            resolution = EditorGUILayout.IntPopup(ML.T("品質"), resolution, ML.TA(new[] {"輕量 512", "標準 1024", "精細 2048"}), new[] {512,1024,2048});
            strength = EditorGUILayout.Slider(ML.T("強度"), strength, 0, 1);
            softness = EditorGUILayout.Slider(ML.T("柔度"), softness, 0, 6);
            tint = EditorGUILayout.ColorField(ML.T("陰影透射色"), tint);
            EditorGUILayout.EndVertical();
            EditorGUILayout.HelpBox(ML.T("遊戲內選單可調整固定／跟隨角色、360° 方向與陰影長短。變更語言後請重新建立／更新，套用遊戲內選單文字。"), MessageType.Info);

            EditorGUILayout.HelpBox(ML.T("遠端顯示需要對方保留你的攝影機（好友或 Show Avatar）。請使用更新後的 lilMCR／PCSS；其他角色擋住捕捉視線時，影子可能有缺口。"), MessageType.Info);

            GUILayout.Space(12f);
            MacaronGUI.Step(3, ML.T("建立與維護"));
            using(new EditorGUI.DisabledScope(avatar == null || EditorUtility.IsPersistent(avatar)))
            {
                bool create;
                using(new EditorGUI.DisabledScope(!pcssReady))
                    create = GUILayout.Button(ML.T("建立 / 更新"), GUILayout.Height(32f));
                if(create)
                {
                    try
                    {
                        Vector3 direction; int space;
                        if(directionSource == 0) { direction = DefaultLightDirection(); space = 0; }
                        else ReadDirection(avatar, out direction, out space);
                        status = Generate(avatar, MacaronCore.ConvertedFolder + "/" + MacaronCore.SafeFileName(avatar.name), space, direction, resolution, strength, softness, tint);
                        statusType = MessageType.Info;
                    }
                    catch(Exception e) { status = e.Message; statusType = MessageType.Error; Debug.LogException(e); }
                }
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if(GUILayout.Button(ML.T("修復角色接收 / 共用深度光（保留投影設定）")))
                {
                    try { status = RepairExisting(avatar); statusType = MessageType.Info; }
                    catch(Exception e) { status = e.Message; statusType = MessageType.Error; Debug.LogException(e); }
                }
                if(GUILayout.Button(ML.T("移除世界落影")))
                {
                    Undo.IncrementCurrentGroup();
                    int group = Undo.GetCurrentGroup();
                    var old = avatar.transform.Find(ObjectName);
                    if(old != null) Undo.DestroyObjectImmediate(old.gameObject);
                    ClearCaptureKeys(avatar);
                    Undo.CollapseUndoOperations(group);
                    status = ML.T("已移除場景物件，產生的資產保留。"); statusType = MessageType.Info;
                }
                EditorGUILayout.EndVertical();
            }
            if(!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
            EditorGUILayout.EndScrollView();
        }

        // Without the module nothing captures: drop the capture keys so the capture pass turns off again.
        // The receiver stencil bit stays (harmless, and Create / Update reuses it).
        static void ClearCaptureKeys(GameObject avatar)
        {
            var done = new HashSet<Material>();
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            foreach(Material m in r.sharedMaterials)
            {
                if(m == null || !done.Add(m) || !m.HasProperty("_MCR_WorldCapture") || m.GetVector("_MCR_WorldCapture") == Vector4.zero) continue;
                Undo.RecordObject(m, "Macaron Remove World Shadow");
                m.SetVector("_MCR_WorldCapture", Vector4.zero);
                EditorUtility.SetDirty(m);
            }
            MacaronCore.SyncCapturePasses(avatar);
            AssetDatabase.SaveAssets();
        }

        public static string RepairExisting(GameObject avatar)
        {
            var module=avatar.transform.Find(ObjectName);
            if(module==null) throw new InvalidOperationException(ML.T("角色尚未建立世界落影，請使用建立 / 更新。"));
            var projection=FindProjection(module);
            var renderer=projection == null ? null : projection.GetComponent<Renderer>();
            if(renderer == null || renderer.sharedMaterial == null) throw new InvalidOperationException(ML.T("角色尚未建立世界落影，請使用建立 / 更新。"));
            string path=AssetDatabase.GetAssetPath(renderer.sharedMaterial);
            string folder=string.IsNullOrEmpty(path) ? MacaronCore.ConvertedFolder+"/"+MacaronCore.SafeFileName(avatar.name) : System.IO.Path.GetDirectoryName(path).Replace('\\','/');
            MacaronCore.EnsureFolder(folder);
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Macaron Repair World Shadow");
            MacaronDepthSource.Ensure(avatar);
            string result=MacaronWorldShadowReceivers.Apply(avatar,folder);
            MacaronCore.SyncCapturePasses(avatar);
            var capture = module.GetComponentInChildren<Camera>(true);
            if(capture != null && renderer.sharedMaterial.GetFloat("_CaptureKey") > 0)
            {
                Undo.RecordObject(capture, "Repair capture layers");
                capture.renderingPath = RenderingPath.Forward;
                capture.cullingMask = (1 << Layer("MirrorReflection",18)) | (1 << Layer("Player",9));
            }
            Undo.RecordObject(renderer.sharedMaterial, "Repair projection queue");
            renderer.sharedMaterial.renderQueue = 2499;
            result += ML.T(" 舊版模組請執行建立 / 更新，以換成支援遠端顯示的角色識別捕捉與選單。");
            Undo.CollapseUndoOperations(group); AssetDatabase.SaveAssets();
            return ML.T("已修復接收設定並統一深度方向光；保留原本的投影方向、品質與顏色。")+result;
        }

        public static Vector3 DefaultLightDirection()
        {
            // Use the Light Adjuster shader defaults, not the low contact-shadow angle.
            var shader = MacaronCore.MainShader;
            int yawIndex = shader == null ? -1 : shader.FindPropertyIndex("_MCR_AdjLightYaw");
            int pitchIndex = shader == null ? -1 : shader.FindPropertyIndex("_MCR_AdjLightPitch");
            float yaw = (yawIndex < 0 ? 0 : shader.GetPropertyDefaultFloatValue(yawIndex)) * Mathf.Deg2Rad;
            float pitch = (pitchIndex < 0 ? 45 : shader.GetPropertyDefaultFloatValue(pitchIndex)) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw)*Mathf.Cos(pitch)).normalized;
        }

        public static void ReadDirection(GameObject avatar, out Vector3 direction, out int space)
        {
            direction = MacaronCore.DefaultDirection(); space = MacaronCore.DefaultSpace();
            bool found = false;
            foreach(var r in avatar.GetComponentsInChildren<Renderer>(true))
            foreach(var m in r.sharedMaterials)
            {
                if(m == null || !m.HasProperty("_MCR_ShadowDirection")) continue;
                Vector3 d = m.GetVector("_MCR_ShadowDirection");
                int s = Mathf.RoundToInt(m.GetFloat("_MCR_ShadowSpace"));
                if(found && (s != space || Vector3.Angle(d, direction) > 0.1f))
                    throw new InvalidOperationException(ML.T("角色材質的陰影方向不一致。請先用角色轉換器統一方向。"));
                direction = d; space = s; found = true;
            }
        }

        public static string Generate(GameObject avatar, string folder, int space, Vector3 direction,
            int resolution = DefaultResolution, float strength = 0.4f, float softness = 1.5f, Color? tint = null)
        {
            if(avatar == null || EditorUtility.IsPersistent(avatar)) throw new ArgumentException(ML.T("請選擇場景角色。"));
            if(!PcssInstalled) throw new InvalidOperationException(ML.T("世界落影需要 lilMCR PCSS。請先匯入 lilMCR_PCSS 資料夾。"));
            if(!HasPcss(avatar)) throw new InvalidOperationException(ML.T("此角色尚未套用 lilMCR PCSS。請先用 lilMCR PCSS 設定套用後，再建立世界落影。"));
            if(space == 1) throw new InvalidOperationException(ML.T("世界落影不支援觀看相機座標。請在轉換器選擇跟隨角色或世界座標，再建立落影。"));
            var shader = Shader.Find("Hidden/liltoon_MCR/WorldShadow");
            if(shader == null) throw new InvalidOperationException(ML.T("找不到 WorldShadow Shader。"));
            Vector3 scale = avatar.transform.lossyScale;
            if(Mathf.Abs(scale.x-scale.y)>0.001f || Mathf.Abs(scale.y-scale.z)>0.001f || scale.x <= 0)
                throw new InvalidOperationException(ML.T("世界落影需要角色根物件使用正值等比例縮放。"));
            if(direction.sqrMagnitude < 0.000001f) direction = Vector3.up;
            direction.Normalize();
            Vector3 light = space == 0 ? -avatar.transform.right*direction.x + avatar.transform.up*direction.y + avatar.transform.forward*direction.z : direction;
            light.Normalize();
            var bounds = new Bounds(avatar.transform.position + avatar.transform.up, Vector3.one * 0.1f);
            bool hasBounds = false;
            var previous = avatar.transform.Find(ObjectName);
            foreach(var r in avatar.GetComponentsInChildren<Renderer>(false))
            {
                if(previous != null && r.transform.IsChildOf(previous)) continue;
                if(!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                if(!hasBounds) { bounds = r.bounds; hasBounds = true; } else bounds.Encapsulate(r.bounds);
            }
            var unsupported = new HashSet<string>();
            int supported = 0;
            foreach(var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(previous != null && r.transform.IsChildOf(previous)) continue;
                foreach(var m in r.sharedMaterials)
                {
                    if(m == null) continue;
                    if(m.HasProperty("_MCR_WorldCapture")) supported++;
                    else unsupported.Add(m.name);
                }
            }
            if(supported == 0) throw new InvalidOperationException(ML.T("遠端世界陰影需要更新後的 lilMCR 或 lilMCR PCSS 材質，請先轉換角色材質。"));
            if(!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
                throw new InvalidOperationException(ML.T("目前裝置不支援世界陰影所需的浮點數貼圖。"));
            float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            float extent = radius * 1.5f;
            // Per-generation key: stored with signed, linear depth data in a floating RT.
            int captureKey = (Guid.NewGuid().GetHashCode() & 0x7fffff) + 1024;
            float near = 0.01f + (captureKey / 8389632f) * 0.01f;
            float far = radius * (3 + 2 / 0.15f);
            MacaronCore.EnsureFolder(folder);
            // Unique asset set per generation avoids cross-avatar edits and keeps Undo references valid.
            string assets = AssetDatabase.GenerateUniqueAssetPath(folder + "/WorldShadow");
            AssetDatabase.CreateFolder(folder, System.IO.Path.GetFileName(assets));
            var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { name = "MCR World Owned Depth", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, antiAliasing = 1 };
            AssetDatabase.CreateAsset(rt, assets + "/Depth.renderTexture");
            var material = new Material(shader) { name = "MCR World Shadow" };
            material.SetTexture("_ShadowMap", rt);
            material.SetFloat("_CaptureKey", captureKey);
            material.SetFloat("_Extent", extent); material.SetFloat("_Near", near); material.SetFloat("_Far", far);
            material.SetFloat("_Strength", strength); material.SetFloat("_Softness", softness);
            if(tint.HasValue) material.SetColor("_Tint", tint.Value);
            material.SetFloat("_Bias", Mathf.Max(0.008f, extent / resolution * 3));
            AssetDatabase.CreateAsset(material, assets + "/Projection.mat");
            var mesh = Volume(extent, near, far);
            AssetDatabase.CreateAsset(mesh, assets + "/Volume.asset");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Macaron World Shadow");
            MacaronDepthSource.Ensure(avatar);
            string receivers = MacaronWorldShadowReceivers.Apply(avatar, assets, new Vector4(extent, near, far, captureKey));
            MacaronCore.SyncCapturePasses(avatar);
            var old = avatar.transform.Find(ObjectName);
            if(old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(ObjectName);
            root.transform.SetParent(avatar.transform, false);
            Quaternion frame = space == 0 ? avatar.transform.rotation : Quaternion.identity;
            root.transform.SetPositionAndRotation(avatar.transform.position, frame);
            root.transform.localScale = Vector3.one / scale.x;
#if VRC_SDK_VRCSDK3
            var constraint = root.AddComponent<VRCRotationConstraint>();
            constraint.Sources.Add(new VRCConstraintSource(avatar.transform, 1));
            constraint.RotationOffset = root.transform.localEulerAngles;
            constraint.RotationAtRest = root.transform.localEulerAngles;
            constraint.GlobalWeight = 1; constraint.Locked = true; constraint.IsActive = true;
            constraint.SolveInLocalSpace = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            constraint.FreezeToWorld = space == 2;
            constraint.ApplyConfigurationChanges();
#else
            // Editor preview only. World freezing requires the VRChat constraint component.
            var constraint = root.AddComponent<RotationConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = avatar.transform, weight = 1 });
            constraint.rotationOffset = root.transform.localEulerAngles;
            constraint.weight = 1; constraint.locked = true; constraint.constraintActive = true;
#endif
            var heading = new GameObject("Heading"); heading.transform.SetParent(root.transform, false);
            heading.transform.localPosition = Quaternion.Inverse(frame) * (bounds.center-avatar.transform.position);
            var aim = new GameObject("Direction"); aim.transform.SetParent(heading.transform, false);
            Vector3 localLight = Quaternion.Inverse(frame) * light;
            float azimuth = Mathf.Atan2(-localLight.x, -localLight.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Asin(Mathf.Clamp(localLight.y,-1,1)) * Mathf.Rad2Deg;
            heading.transform.localRotation = Quaternion.Euler(0, azimuth, 0);
            aim.transform.localRotation = Quaternion.Euler(elevation, 0, 0);
            var capture = new GameObject("Capture"); capture.transform.SetParent(aim.transform, false);
            capture.transform.localPosition = new Vector3(0,0,-radius*2);
            var camera = capture.AddComponent<Camera>();
            camera.renderingPath = RenderingPath.Forward;
            camera.orthographic = true; camera.orthographicSize = extent; camera.aspect = 1;
            camera.nearClipPlane = near; camera.farClipPlane = far; camera.targetTexture = rt;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(1,0,0,0);
            camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None; camera.depth = -90;
            // Local complete avatar and remote avatars. Only tagged owner samples cast.
            camera.cullingMask = (1 << Layer("MirrorReflection",18)) | (1 << Layer("Player",9));
            var volume = new GameObject("Projection"); volume.transform.SetParent(aim.transform, false);
            volume.transform.localPosition = capture.transform.localPosition;
            volume.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = volume.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Projection is saved inactive; the Animator on Direction plays Direction.anim, which turns it on.
            // If animations do not run on this avatar (e.g. blocked by Safety), the projection volume stays hidden.
            volume.SetActive(false);
            var directionAnimator = aim.AddComponent<Animator>();
            directionAnimator.runtimeAnimatorController = DirectionController(assets);
            directionAnimator.applyRootMotion = false;
            directionAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Undo.RegisterCreatedObjectUndo(root, "Macaron World Shadow");
            string menu = MacaronLightAdjuster.InstallWorldShadowMenu(avatar, root, assets);
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return ML.T("世界落影已建立，使用選定方向。") + receivers + menu +
                (unsupported.Count == 0 ? "" : ML.F(" 以下材質不支援角色識別，不會作為世界陰影來源：{0}。", string.Join(", ", unsupported)));
        }

        public static Transform FindProjection(Transform module)
        {
            return module.Find(ProjectionPath) ?? module.Find("Projection");
        }

        // Direction.anim: switch clip that activates the "Projection" child (path relative to Direction).
        static UnityEditor.Animations.AnimatorController DirectionController(string folder)
        {
            var clip = new AnimationClip { name = "Direction" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Projection", typeof(GameObject), "m_IsActive"), new AnimationCurve(new Keyframe(0, 1)));
            AssetDatabase.CreateAsset(clip, folder + "/Direction.anim");
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(folder + "/Direction.controller");
            var state = controller.layers[0].stateMachine.AddState("Direction");
            state.motion = clip; state.writeDefaultValues = false;
            controller.layers[0].stateMachine.defaultState = state;
            return controller;
        }

        static int Layer(string name, int fallback) { int l = LayerMask.NameToLayer(name); return l < 0 ? fallback : l; }
        static Mesh Volume(float e, float n, float f)
        {
            var m = new Mesh { name = "MCR Shadow Volume" };
            m.vertices = new[] {new Vector3(-e,-e,n),new Vector3(e,-e,n),new Vector3(e,e,n),new Vector3(-e,e,n),
                new Vector3(-e,-e,f),new Vector3(e,-e,f),new Vector3(e,e,f),new Vector3(-e,e,f)};
            m.triangles = new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            m.RecalculateBounds(); return m;
        }
    }
}
#endif
