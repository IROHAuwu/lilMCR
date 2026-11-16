#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Collections;
using System.Reflection;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace liltoon_MCR
{
    /// <summary>
    /// Macaron Light Adjuster
    /// Non-destructive avatar menu (Modular Avatar) for brightness, color temperature, saturation, a fixed
    /// light direction and a lightweight on / off switch.
    ///
    /// How it works:
    ///  - The Macaron shader has hidden _MCR_Adj* properties with neutral defaults (custom_insert.hlsl, Macaron_Adjust).
    ///  - One animator layer per menu value: a single Motion Time state whose clip writes the property on every
    ///    renderer that has a Macaron material. Only these hidden properties are animated, so per-material settings
    ///    (_Color, _LightMaxLimit, ...) are never overwritten and no texture is baked.
    ///  - A child object "Macaron Light Adjuster" carries MA Merge Animator (FX), MA Parameters and MA Menu Installer /
    ///    Menu Items. Modular Avatar merges it at build time; delete the object to remove everything.
    ///  - The animator controller is saved next to the converted materials. "Add / Update" rebuilds it, so renderers
    ///    added later (outfits) are included after running it again.
    /// </summary>
    public static class MacaronLightAdjuster
    {
        public const string ObjectName = "Macaron Light Adjuster";
        public const string ControllerName = "MacaronLightAdjuster.controller";

        // Enable cameras through animation on both local and remote avatars.
        // Capture ownership is checked in the shaders, not by IsLocal.
        public static string InstallWorldShadowMenu(GameObject avatar, GameObject root, string folder)
        {
#if VRC_SDK_VRCSDK3
            if(!Available) return ML.T(" 未安裝 Modular Avatar：僅建立投影，沒有遊戲內開關。");
            const string parameter = "Macaron/WorldShadow";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/WorldShadow.controller");
            controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            var cp = controller.parameters; cp[0].defaultBool = true; controller.parameters = cp;
            controller.AddParameter(new AnimatorControllerParameter { name = "Macaron/Active", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            var sm = controller.layers[0].stateMachine;
            AnimatorState[] states = new AnimatorState[2];
            for(int i=0;i<2;i++)
            {
                var clip = new AnimationClip { name = i == 0 ? "Off" : "On" };
                string path = AnimationUtility.CalculateTransformPath(root.transform, avatar.transform);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path + "/" + MacaronWorldShadow.CapturePath, typeof(Camera), "m_Enabled"), AnimationCurve.Constant(0,1,i));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path + "/" + MacaronWorldShadow.ProjectionPath, typeof(MeshRenderer), "m_Enabled"), AnimationCurve.Constant(0,1,i));
                AssetDatabase.AddObjectToAsset(clip, controller);
                states[i] = sm.AddState(clip.name); states[i].motion = clip; states[i].writeDefaultValues = false;
            }
            sm.defaultState = states[0];
            for(int i=0;i<2;i++)
            {
                var tr = states[i].AddTransition(states[1-i]); tr.hasExitTime = false; tr.duration = 0;
                tr.AddCondition(i == 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, parameter);
                if(i == 0) { tr.AddCondition(AnimatorConditionMode.If, 0, "Macaron/Active"); }
            }
            var inactive = states[1].AddTransition(states[0]); inactive.hasExitTime = false; inactive.duration = 0;
            inactive.AddCondition(AnimatorConditionMode.IfNot, 0, "Macaron/Active");
            var merge = GetOrAdd(root, MAType("ModularAvatarMergeAnimator"));
            SetMember(merge, "animator", controller, true);
            SetMember(merge, "layerType", VRCAvatarDescriptor.AnimLayerType.FX, true);
            SetEnum(merge, "pathMode", "Absolute", true);
            SetMember(merge, "matchAvatarWriteDefaults", true, false); Dirty(merge);
            Type config = MAType("ParameterConfig");
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(config));
            object cfg = Activator.CreateInstance(config);
            SetField(cfg, "nameOrPrefix", parameter, true); SetEnumField(cfg, "syncType", "Bool", true);
            SetField(cfg, "defaultValue", 1f, true); SetField(cfg, "hasExplicitDefaultValue", true, false);
            SetField(cfg, "saved", true, false); list.Add(cfg);
            var parameters = GetOrAdd(root, MAType("ModularAvatarParameters"));
            SetMember(parameters, "parameters", list, true); Dirty(parameters);
            Dirty(GetOrAdd(root, MAType("ModularAvatarMenuInstaller")));
            var item = GetOrAdd(root, MAType("ModularAvatarMenuItem"));
            SetSubMenu(item, ML.T("世界落影")); Dirty(item);
            var toggle = GetOrAdd(CreateChild(root.transform, ML.T("開關")), MAType("ModularAvatarMenuItem"));
            SetMember(toggle, "Control", new VRCExpressionsMenu.Control {
                name = ML.T("開關"), type = VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter }, value = 1,
                subParameters = new VRCExpressionsMenu.Control.Parameter[0], labels = new VRCExpressionsMenu.Control.Label[0]
            }, true);
            Dirty(toggle);
            string[] names = {ML.T("強度"), ML.T("柔度"), ML.T("方向（360°）"), ML.T("長短與傾角"), ML.T("固定方向")};
            string[] props = {"_Strength", "_Softness"};
            var projection = MacaronWorldShadow.FindProjection(root.transform).GetComponent<Renderer>();
            var aim = root.transform.Find("Heading/Direction");
            var fixedRotation = root.GetComponent<VRCRotationConstraint>();
            string rootPath = AnimationUtility.CalculateTransformPath(root.transform, avatar.transform);
            float pitch = Mathf.DeltaAngle(0, aim.localEulerAngles.x);
            float yaw = Mathf.DeltaAngle(0, aim.parent.localEulerAngles.y);
            string[] keys = {"Strength", "Softness", "YawV2", "PitchV2", "Fixed"};
            for(int i=0;i<5;i++)
            {
                string key = "Macaron/WorldShadow" + keys[i];
                float max = i==0 ? 1 : 6;
                float value = i < 2 ? projection.sharedMaterial.GetFloat(props[i])/max : i == 2 ? 0.5f : i == 3 ? 0.5f : (fixedRotation.FreezeToWorld ? 1f : 0f);
                controller.AddParameter(new AnimatorControllerParameter { name=key, type=i == 4 ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Float, defaultFloat=value, defaultBool=value > 0.5f });
                controller.AddLayer(names[i]);
                var layers=controller.layers; layers[layers.Length-1].defaultWeight=1; controller.layers=layers;
                var state=layers[layers.Length-1].stateMachine.AddState("Value");
                var clip=new AnimationClip { name=names[i] };
                if(i < 2)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(projection.transform,avatar.transform),typeof(MeshRenderer),"material."+props[i]), AnimationCurve.Linear(0,0,1,max));
                else if(i == 2)
                {
                    var turn = new AnimationCurve();
                    // Intermediate keys preserve a full revolution even if Unity converts
                    // Euler rotation bindings to quaternion interpolation during import.
                    for(int k=0;k<=8;k++) turn.AddKey(new Keyframe(k/8f, yaw-180+45*k, 360, 360));
                    SetWorldShadowEuler(clip, rootPath+"/Heading", "y", turn);
                }
                else if(i == 3)
                {
                    float first = 2*(pitch-90), second = 2*(-90-pitch);
                    var tilt = new AnimationCurve(new Keyframe(0,90,first,first),
                        new Keyframe(0.5f,pitch,first,second), new Keyframe(1,-90,second,second));
                    SetWorldShadowEuler(clip, rootPath+"/Heading/Direction", "x", tilt);
                }
                else
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(rootPath,typeof(VRCRotationConstraint),"FreezeToWorld"), AnimationCurve.Linear(0,0,1,1));
                AssetDatabase.AddObjectToAsset(clip,controller);
                state.motion=clip; state.writeDefaultValues=false; state.timeParameter=key; state.timeParameterActive=true;
                if(i == 4) ConfigureBoolStates(controller, layers[layers.Length-1].stateMachine, state, clip, key, value > 0.5f);
                object setting=Activator.CreateInstance(config);
                SetField(setting,"nameOrPrefix",key,true); SetEnumField(setting,"syncType",i == 4 ? "Bool" : "Float",true);
                SetField(setting,"defaultValue",value,true); SetField(setting,"hasExplicitDefaultValue",true,false); SetField(setting,"saved",true,false); list.Add(setting);
                var radial=GetOrAdd(CreateChild(root.transform,names[i]),MAType("ModularAvatarMenuItem"));
                SetMember(radial,"Control",new VRCExpressionsMenu.Control {
                    name=names[i], type=i == 4 ? VRCExpressionsMenu.Control.ControlType.Toggle : VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    parameter=new VRCExpressionsMenu.Control.Parameter {name=i == 4 ? key : ""}, value=1,
                    subParameters=i == 4 ? new VRCExpressionsMenu.Control.Parameter[0] : new[] { new VRCExpressionsMenu.Control.Parameter { name=key } },
                    labels=new VRCExpressionsMenu.Control.Label[0]
                },true); Dirty(radial);
            }
            SetMember(parameters,"parameters",list,true); Dirty(parameters);
            AddWorldShadowScaleLayer(controller, avatar, root);
            return ML.T(" 已加入世界落影選單開關。");
#else
            return ML.T(" 未安裝 VRChat SDK：僅建立投影，沒有遊戲內開關。");
#endif
        }

        // Avatar scaling (VRChat built-in ScaleFactor, 1 at the uploaded size). The rig scales with the avatar, but a
        // camera's size and clip planes ignore transform scale: they follow ScaleFactor here, so the capture and the
        // projection volume stay in the same units. Macaron_IsWorldCapture accepts the scaled values.
        const string ScaleParameter = "ScaleFactor";
        const float ScaleBlendMax = 1000f;
        static void AddWorldShadowScaleLayer(AnimatorController controller, GameObject avatar, GameObject root)
        {
            Transform captureTransform = root.transform.Find(MacaronWorldShadow.CapturePath);
            Camera camera = captureTransform != null ? captureTransform.GetComponent<Camera>() : null;
            if(camera == null) return;
            string path = AnimationUtility.CalculateTransformPath(captureTransform, avatar.transform);
            controller.AddParameter(new AnimatorControllerParameter { name = ScaleParameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddLayer("Macaron World Shadow Scale");
            var layers = controller.layers; layers[layers.Length-1].defaultWeight = 1; controller.layers = layers;
            var tree = new BlendTree { name = "Scale", blendType = BlendTreeType.Simple1D, blendParameter = ScaleParameter, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            // Two end clips; a 1D blend between them is exactly linear in ScaleFactor.
            foreach(float t in new[] { 0f, ScaleBlendMax })
            {
                var clip = new AnimationClip { name = t == 0f ? "Scale 0" : "Scale Max" };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Camera), "orthographic size"), AnimationCurve.Constant(0, 1, camera.orthographicSize * t));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Camera), "near clip plane"), AnimationCurve.Constant(0, 1, camera.nearClipPlane * t));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Camera), "far clip plane"), AnimationCurve.Constant(0, 1, camera.farClipPlane * t));
                AssetDatabase.AddObjectToAsset(clip, controller);
                tree.AddChild(clip, t);
            }
            var state = layers[layers.Length-1].stateMachine.AddState("Scale");
            state.motion = tree; state.writeDefaultValues = false;
            layers[layers.Length-1].stateMachine.defaultState = state;
        }

        static void SetWorldShadowEuler(AnimationClip clip, string path, string axis, AnimationCurve curve)
        {
            // Explicitly bind all axes: partially animated Euler rotations can reset
            // authored axes on Animator layers. Heading and elevation have separate pivots.
            foreach(string component in new[] {"x", "y", "z"})
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw."+component),
                    component == axis ? curve : AnimationCurve.Constant(0,1,0));
        }

        // Menu texts follow the Macaron language setting (Auto = lilToon's language). They are written into the
        // avatar when it is generated, so run "Add or Update" again after changing the language.
        private static string RootLabel { get { return ML.T("Macaron 光照"); } }
        private static string DialogTitle { get { return ML.T("Macaron 光照調節器"); } }

        public enum Result { Added, Updated, NoModularAvatar, NoMaterials, NoControls }

        private struct Control
        {
            public string Label;      // l10n key
            public string Parameter;  // avatar parameter (0..1)
            public string Property;   // shader property
            public float Default;     // parameter default (0..1)
            public Vector2[] Keys;    // (parameter, property value)
            public bool Direction;    // in the "light direction" sub menu
            public bool Toggle;       // on / off menu toggle (parameter 0 / 1) instead of a radial puppet
            public bool OffByDefault; // item not selected in the Converter until the user turns it on
        }

        private static readonly Control[] Controls =
        {
            // Lightweight switch: off skips every lilMCR rendering feature (occlusion, contact shadow and any later feature).
            // The light adjustments below are cheap and stay active.
            new Control { Label = "lilMCR 總開關", Parameter = "Macaron/Active", Property = "_MCR_AdjActive", Default = 1f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) }, Toggle = true },
            // Local Only from the menu (mcr_local_only.hlsl): on = lilMCR only on the wearer's own client. A synced Bool,
            // so other players' clients know it. Off in game by default, and the item itself is not added unless chosen.
            new Control { Label = "僅自己可見", Parameter = "Macaron/LocalOnly", Property = "_MCR_AdjLocalOnly", Default = 0f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) }, Toggle = true, OffByDefault = true },
            new Control { Label = "亮度", Parameter = "Macaron/Brightness", Property = "_MCR_AdjBrightness", Default = 0.5f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 5f) } },
            new Control { Label = "固定亮度", Parameter = "Macaron/FixedBrightness", Property = "_MCR_AdjFixedBrightness", Default = 0f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) } },
            new Control { Label = "光源顏色影響", Parameter = "Macaron/LightColorInfluence", Property = "_MCR_AdjLightColorInfluence", Default = 1f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) } },
            new Control { Label = "色溫", Parameter = "Macaron/ColorTemperature", Property = "_MCR_AdjTemperature", Default = 0.5f,
                          Keys = new[] { new Vector2(0f, -1f), new Vector2(1f, 1f) } },
            new Control { Label = "彩度", Parameter = "Macaron/Saturation", Property = "_MCR_AdjSaturation", Default = 0.5f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 2f) } },
            // Direction defaults match the shader (strength 0 = off, pitch 45°), so the avatar looks the same with or
            // without the menu until the user changes it.
            new Control { Label = "固定方向強度", Parameter = "Macaron/LightFix", Property = "_MCR_AdjLightStrength", Default = 0f,
                          Keys = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f) }, Direction = true },
            new Control { Label = "左右", Parameter = "Macaron/LightYaw", Property = "_MCR_AdjLightYaw", Default = 0.5f,
                          Keys = new[] { new Vector2(0f, -180f), new Vector2(1f, 180f) }, Direction = true },
            new Control { Label = "上下", Parameter = "Macaron/LightPitch", Property = "_MCR_AdjLightPitch", Default = 0.75f,
                          Keys = new[] { new Vector2(0f, -90f), new Vector2(1f, 90f) }, Direction = true },
        };

        //------------------------------------------------------------------------------------------
        // Modular Avatar is looked up by reflection. No compile-time reference / asmdef define is needed,
        // so the tool works regardless of assembly compile order or define state. VRChat Avatars SDK types come from
        // the precompiled VRCSDK3A.dll (auto referenced, VRC_SDK_VRCSDK3 define).

        private const string MANamespace = "nadena.dev.modular_avatar.core.";
        private static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        private static Type FindType(string fullName)
        {
            Type t;
            if(typeCache.TryGetValue(fullName, out t)) return t;   // cleared on domain reload
            t = null;
            foreach(Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName, false); } catch { t = null; }
                if(t != null) break;
            }
            typeCache[fullName] = t;
            return t;
        }

        internal static Type MAType(string name) { return FindType(MANamespace + name); }

        //------------------------------------------------------------------------------------------
        // Item selection (converter window). The three light direction controls are one item. Saved in EditorPrefs.

        private const string PrefItemPrefix = "Macaron.LightAdjuster.Item.";
        private const string DirectionKey = "Direction";

        private struct Item { public string Key; public string Label; public bool OffByDefault; }
        private static Item[] items;

        private static string ItemKey(Control c) { return c.Direction ? DirectionKey : c.Parameter; }

        private static Item[] Items
        {
            get
            {
                if(items == null)
                {
                    List<Item> list = new List<Item>();
                    foreach(Control c in Controls)
                    {
                        string key = ItemKey(c);
                        if(list.Exists(x => x.Key == key)) continue;
                        list.Add(new Item { Key = key, Label = c.Direction ? "光源方向" : c.Label, OffByDefault = c.OffByDefault });
                    }
                    items = list.ToArray();
                }
                return items;
            }
        }

        public static int ItemCount { get { return Items.Length; } }
        public static string ItemLabel(int i) { return ML.T(Items[i].Label); }
        public static bool ItemEnabled(int i) { return EditorPrefs.GetBool(PrefItemPrefix + Items[i].Key, !Items[i].OffByDefault); }
        public static void SetItemEnabled(int i, bool value) { EditorPrefs.SetBool(PrefItemPrefix + Items[i].Key, value); }

        public static bool AnyItemEnabled
        {
            get
            {
                for(int i = 0; i < ItemCount; i++) if(ItemEnabled(i)) return true;
                return false;
            }
        }

        private static Control[] SelectedControls()
        {
            List<Control> list = new List<Control>();
            foreach(Control c in Controls) if(EditorPrefs.GetBool(PrefItemPrefix + ItemKey(c), !c.OffByDefault)) list.Add(c);
            return list.ToArray();
        }

        public static bool Available
        {
            get
            {
#if VRC_SDK_VRCSDK3
                return MAType("ModularAvatarMergeAnimator") != null
                    && MAType("ModularAvatarParameters") != null
                    && MAType("ModularAvatarMenuInstaller") != null
                    && MAType("ModularAvatarMenuItem") != null
                    && MAType("ParameterConfig") != null;
#else
                return false;
#endif
            }
        }

        public static string ResultMessage(Result r)
        {
            switch(r)
            {
                case Result.Added: return ML.F("已加入光照調節器（Modular Avatar 選單：{0}）。", RootLabel);
                case Result.Updated: return ML.F("已更新光照調節器（Modular Avatar 選單：{0}）。", RootLabel);
                case Result.NoControls: return ML.T("沒有選擇任何光照項目，沒有加入光照調節器。");
                case Result.NoModularAvatar: return ML.T("專案沒有 Modular Avatar（或 VRChat Avatars SDK），沒有加入光照調節器。");
                default: return ML.T("這個角色沒有 Macaron 材質，沒有加入光照調節器。");
            }
        }

        //------------------------------------------------------------------------------------------
        // Menu

        [MenuItem(MacaronCore.MenuRoot + "Light Adjuster 光調整/Add or Update 追加・更新 (Selected Avatar)", false, 70)]
        private static void MenuAdd()
        {
            GameObject avatar = SelectedAvatar();
            if(avatar == null) return;
            Result r = Generate(avatar);
            EditorUtility.DisplayDialog(DialogTitle, ResultMessage(r), "OK");
        }

        [MenuItem(MacaronCore.MenuRoot + "Light Adjuster 光調整/Add or Update 追加・更新 (Selected Avatar)", true)]
        [MenuItem(MacaronCore.MenuRoot + "Light Adjuster 光調整/Remove 削除 (Selected Avatar)", true)]
        private static bool MenuValidate()
        {
            return SelectedAvatar() != null;
        }

        [MenuItem(MacaronCore.MenuRoot + "Light Adjuster 光調整/Remove 削除 (Selected Avatar)", false, 71)]
        private static void MenuRemove()
        {
            GameObject avatar = SelectedAvatar();
            if(avatar == null) return;
            Transform t = FindAdjuster(avatar);
            if(t == null)
            {
                EditorUtility.DisplayDialog(DialogTitle, ML.T("這個角色沒有光照調節器。"), "OK");
                return;
            }
            if(!EditorUtility.DisplayDialog(DialogTitle, ML.F("從「{0}」移除光照調節器物件？（可用 Ctrl+Z 復原；產生的動畫檔不會刪除）", avatar.name), ML.T("移除"), ML.T("取消"))) return;
            Undo.DestroyObjectImmediate(t.gameObject);
        }

        private static GameObject SelectedAvatar()
        {
            GameObject go = Selection.activeGameObject;
            if(go == null || EditorUtility.IsPersistent(go)) return null;
            Transform root = MacaronCore.FindAvatarRoot(go.transform);
            return root != null ? root.gameObject : null;
        }

        public static Transform FindAdjuster(GameObject avatar)
        {
            if(avatar == null) return null;
            for(int i = 0; i < avatar.transform.childCount; i++)
            {
                Transform c = avatar.transform.GetChild(i);
                if(c.name == ObjectName) return c;
            }
            return null;
        }

        //------------------------------------------------------------------------------------------
        // Generate

        /// <summary>Adds (or rebuilds) the adjuster on the avatar. Assets go to the avatar's converted folder.</summary>
        public static Result Generate(GameObject avatar)
        {
            if(avatar == null) return Result.NoMaterials;
            string folder = MacaronCore.ConvertedFolder + "/" + MacaronCore.SafeFileName(avatar.name);
            return Generate(avatar, folder);
        }

        public static Result Generate(GameObject avatar, string folder)
        {
#if VRC_SDK_VRCSDK3
            if(!Available) return Result.NoModularAvatar;
            List<Renderer> renderers = TargetRenderers(avatar);
            if(renderers.Count == 0) return Result.NoMaterials;
            Control[] controls = SelectedControls();
            // Uzumore / Local Only: the controller also carries the IsLocal layer (Uzumore's own view not pushed back,
            // Local Only's effect on the wearer's client), even with no menu item.
            bool uzumore = MacaronUzumore.Uses(renderers);
            if(controls.Length == 0 && !uzumore) return Result.NoControls;

            Type tMerge = MAType("ModularAvatarMergeAnimator");
            Type tParams = MAType("ModularAvatarParameters");
            Type tInstaller = MAType("ModularAvatarMenuInstaller");
            Type tItem = MAType("ModularAvatarMenuItem");
            Type tConfig = MAType("ParameterConfig");

            MacaronCore.EnsureFolder(folder);
            Transform existing = FindAdjuster(avatar);
            // Update the controller this avatar already uses (keeps its GUID); new adjusters get their own file
            // so avatars with the same name never overwrite each other.
            AnimatorController reuse = null;
            if(existing != null)
            {
                Component oldMerge = existing.GetComponent(tMerge);
                reuse = oldMerge != null ? GetMember(oldMerge, "animator") as AnimatorController : null;
                string reusePath = reuse != null ? AssetDatabase.GetAssetPath(reuse) : "";
                if(string.IsNullOrEmpty(reusePath) || !System.IO.Path.GetFileName(reusePath).StartsWith("MacaronLightAdjuster")) reuse = null;
                // A duplicated avatar still points at the original's controller: give it its own file instead.
                if(reuse != null)
                {
                    foreach(UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(tMerge, true))
                    {
                        Component other = o as Component;
                        if(other == null || other == oldMerge || other.transform.IsChildOf(avatar.transform)) continue;
                        if(GetMember(other, "animator") as AnimatorController == reuse) { reuse = null; break; }
                    }
                }
            }
            string controllerPath = reuse != null ? AssetDatabase.GetAssetPath(reuse) : AssetDatabase.GenerateUniqueAssetPath(folder + "/" + ControllerName);
            AnimatorController controller = BuildController(avatar.transform, renderers, controls, controllerPath, reuse, uzumore);

            bool added = existing == null;
            GameObject go;
            if(added)
            {
                go = new GameObject(ObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Macaron Add Light Adjuster");
                Undo.SetTransformParent(go.transform, avatar.transform, "Macaron Add Light Adjuster");
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
            else
            {
                go = existing.gameObject;
                // Rebuild the menu children (they are generated).
                for(int i = go.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(go.transform.GetChild(i).gameObject);
            }

            // MA Merge Animator (FX, absolute paths: clip paths are relative to the avatar root)
            Component merge = GetOrAdd(go, tMerge);
            Undo.RecordObject(merge, "Macaron Light Adjuster");
            SetMember(merge, "animator", controller, true);
            SetMember(merge, "layerType", VRCAvatarDescriptor.AnimLayerType.FX, true);
            SetEnum(merge, "pathMode", "Absolute", true);
            SetMember(merge, "matchAvatarWriteDefaults", true, false);
            SetMember(merge, "deleteAttachedAnimator", true, false);
            Dirty(merge);

            // MA Parameters (Bool toggles / Float radials, synced, saved, explicit default)
            Component parameters = GetOrAdd(go, tParams);
            Undo.RecordObject(parameters, "Macaron Light Adjuster");
            IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tConfig));
            foreach(Control c in controls)
            {
                object cfg = Activator.CreateInstance(tConfig);   // struct: boxed copy, fields set on the box
                SetField(cfg, "nameOrPrefix", c.Parameter, true);
                SetField(cfg, "remapTo", "", false);
                SetEnumField(cfg, "syncType", c.Toggle ? "Bool" : "Float", true);
                SetField(cfg, "defaultValue", c.Default, true);
                SetField(cfg, "hasExplicitDefaultValue", true, false);
                SetField(cfg, "saved", true, false);
                SetField(cfg, "localOnly", false, false);
                SetField(cfg, "isPrefix", false, false);
                SetField(cfg, "internalParameter", false, false);
                list.Add(cfg);
            }
            SetMember(parameters, "parameters", list, true);
            Dirty(parameters);

            // MA Menu Installer + root sub menu (children). Only the Uzumore IsLocal layer (no light item chosen): no menu.
            if(controls.Length == 0)
            {
                Component oldItem = go.GetComponent(tItem);
                if(oldItem != null) Undo.DestroyObjectImmediate(oldItem);
                Component oldInstaller = go.GetComponent(tInstaller);
                if(oldInstaller != null) Undo.DestroyObjectImmediate(oldInstaller);
                EditorUtility.SetDirty(go);
                AssetDatabase.SaveAssets();
                return added ? Result.Added : Result.Updated;
            }
            Dirty(GetOrAdd(go, tInstaller));
            Component root = GetOrAdd(go, tItem);
            Undo.RecordObject(root, "Macaron Light Adjuster");
            SetSubMenu(root, RootLabel);
            Dirty(root);

            GameObject dirMenu = null;
            foreach(Control c in controls)
            {
                Transform parent = go.transform;
                if(c.Direction)
                {
                    if(dirMenu == null)
                    {
                        dirMenu = CreateChild(go.transform, ML.T("光源方向"));
                        Component dm = dirMenu.AddComponent(tItem);
                        SetSubMenu(dm, ML.T("光源方向"));
                        Dirty(dm);
                    }
                    parent = dirMenu.transform;
                }
                GameObject item = CreateChild(parent, ML.T(c.Label));
                Component mi = item.AddComponent(tItem);
                VRCExpressionsMenu.Control control = c.Toggle
                    ? new VRCExpressionsMenu.Control
                    {
                        name = ML.T(c.Label),
                        type = VRCExpressionsMenu.Control.ControlType.Toggle,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = c.Parameter },
                        subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                        labels = new VRCExpressionsMenu.Control.Label[0],
                        value = 1f,
                    }
                    : new VRCExpressionsMenu.Control
                    {
                        name = ML.T(c.Label),
                        type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                        subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = c.Parameter } },
                        labels = new VRCExpressionsMenu.Control.Label[0],
                        value = 1f,
                    };
                SetMember(mi, "Control", control, true);
                SetMember(mi, "label", ML.T(c.Label), false);
                SetMember(mi, "isSynced", true, false);
                SetMember(mi, "isSaved", true, false);
                Dirty(mi);
            }

            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();
            return added ? Result.Added : Result.Updated;
#else
            return Result.NoModularAvatar;
#endif
        }

#if VRC_SDK_VRCSDK3
        internal static Component GetOrAdd(GameObject go, Type t)
        {
            Component c = go.GetComponent(t);
            return c != null ? c : Undo.AddComponent(go, t);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Macaron Light Adjuster");
            go.transform.SetParent(parent, false);
            return go;
        }

        internal static void Dirty(Component c)
        {
            if(c == null) return;
            EditorUtility.SetDirty(c);
            if(PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }

        private static void SetSubMenu(Component mi, string label)
        {
            SetMember(mi, "Control", new VRCExpressionsMenu.Control
            {
                name = label,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "" },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
            }, true);
            SetEnum(mi, "MenuSource", "Children", true);
            SetMember(mi, "label", label, false);
        }
#endif

        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Sets a field or property; logs a warning when a required member is missing (MA API changed).</summary>
        internal static object GetMember(object target, string name)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, MemberFlags);
            if(f != null) return f.GetValue(target);
            PropertyInfo p = t.GetProperty(name, MemberFlags);
            return p != null && p.CanRead ? p.GetValue(target, null) : null;
        }

        internal static bool SetMember(object target, string name, object value, bool required)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, MemberFlags);
            if(f != null) { f.SetValue(target, value); return true; }
            PropertyInfo p = t.GetProperty(name, MemberFlags);
            if(p != null && p.CanWrite) { p.SetValue(target, value, null); return true; }
            if(required) Debug.LogWarning("[Macaron Light Adjuster] Modular Avatar member not found: " + t.FullName + "." + name);
            return false;
        }

        internal static bool SetEnum(object target, string name, string valueName, bool required)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, MemberFlags);
            PropertyInfo p = f == null ? t.GetProperty(name, MemberFlags) : null;
            Type et = f != null ? f.FieldType : (p != null ? p.PropertyType : null);
            if(et == null || !et.IsEnum)
            {
                if(required) Debug.LogWarning("[Macaron Light Adjuster] Modular Avatar enum member not found: " + t.FullName + "." + name);
                return false;
            }
            object v;
            try { v = Enum.Parse(et, valueName); }
            catch { if(required) Debug.LogWarning("[Macaron Light Adjuster] Enum value not found: " + et.FullName + "." + valueName); return false; }
            return SetMember(target, name, v, required);
        }

        // Fields of a boxed struct (ParameterConfig).
        private static bool SetField(object boxed, string name, object value, bool required)
        {
            FieldInfo f = boxed.GetType().GetField(name, MemberFlags);
            if(f == null)
            {
                if(required) Debug.LogWarning("[Macaron Light Adjuster] Modular Avatar field not found: " + boxed.GetType().FullName + "." + name);
                return false;
            }
            f.SetValue(boxed, value);
            return true;
        }

        private static bool SetEnumField(object boxed, string name, string valueName, bool required)
        {
            FieldInfo f = boxed.GetType().GetField(name, MemberFlags);
            if(f == null || !f.FieldType.IsEnum)
            {
                if(required) Debug.LogWarning("[Macaron Light Adjuster] Modular Avatar field not found: " + boxed.GetType().FullName + "." + name);
                return false;
            }
            f.SetValue(boxed, Enum.Parse(f.FieldType, valueName));
            return true;
        }

        /// <summary>Renderers below the avatar that use at least one Macaron material.</summary>
        public static List<Renderer> TargetRenderers(GameObject avatar)
        {
            List<Renderer> list = new List<Renderer>();
            foreach(Renderer r in MacaronCore.AvatarRenderers(avatar))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(MacaronCore.IsMacaronMaterial(m)) { list.Add(r); break; }
                }
            }
            return list;
        }

        // Bool parameters use endpoint states, never a float motion-time binding.
        private static void ConfigureBoolStates(AnimatorController controller, AnimatorStateMachine sm,
            AnimatorState off, AnimationClip source, string parameter, bool defaultOn)
        {
            var on = sm.AddState(off.name + " On");
            var states = new[] { off, on };
            for(int i = 0; i < 2; i++)
            {
                var clip = new AnimationClip { name = source.name + (i == 0 ? " Off" : " On") };
                foreach(var binding in AnimationUtility.GetCurveBindings(source))
                {
                    float value = AnimationUtility.GetEditorCurve(source, binding).Evaluate(i);
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60f, value));
                }
                AssetDatabase.AddObjectToAsset(clip, controller);
                states[i].motion = clip;
                states[i].timeParameterActive = false;
                states[i].timeParameter = "";
                states[i].writeDefaultValues = false;
            }
            var enable = off.AddTransition(on);
            enable.hasExitTime = false; enable.duration = 0;
            enable.AddCondition(AnimatorConditionMode.If, 0, parameter);
            var disable = on.AddTransition(off);
            disable.hasExitTime = false; disable.duration = 0;
            disable.AddCondition(AnimatorConditionMode.IfNot, 0, parameter);
            sm.defaultState = defaultOn ? on : off;
        }
        private static AnimatorController BuildController(Transform avatarRoot, List<Renderer> renderers, Control[] controls, string path, AnimatorController reuse, bool uzumore)
        {
            // Generated contents are rebuilt every time; an existing asset is cleared in place so its GUID stays.
            AnimatorController controller = reuse;
            if(controller != null)
            {
                foreach(UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if(o != null && o != controller) UnityEngine.Object.DestroyImmediate(o, true);
                controller.layers = new AnimatorControllerLayer[0];
                controller.parameters = new AnimatorControllerParameter[0];
            }
            else
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            AnimatorControllerParameter[] ps = new AnimatorControllerParameter[controls.Length];
            for(int i = 0; i < controls.Length; i++)
            {
                ps[i] = new AnimatorControllerParameter
                {
                    name = controls[i].Parameter,
                    type = controls[i].Toggle ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Float,
                    defaultBool = controls[i].Default > 0.5f,
                    defaultFloat = controls[i].Default,
                };
            }
            controller.parameters = ps;

            for(int i = 0; i < controls.Length; i++)
            {
                Control c = controls[i];
                string layerName = "Macaron " + c.Property.Replace("_MCR_Adj", "");
                if(i < controller.layers.Length)
                {
                    AnimatorControllerLayer[] ls0 = controller.layers;
                    ls0[i].name = layerName;
                    controller.layers = ls0;
                }
                else
                {
                    controller.AddLayer(layerName);
                }

                AnimationClip clip = new AnimationClip { name = layerName };
                AnimationCurve curve = new AnimationCurve();
                foreach(Vector2 k in c.Keys) curve.AddKey(new Keyframe(k.x, k.y));
                for(int k = 0; k < curve.length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                }
                foreach(Renderer r in renderers)
                {
                    string p = AnimationUtility.CalculateTransformPath(r.transform, avatarRoot);
                    clip.SetCurve(p, r.GetType(), "material." + c.Property, curve);
                }
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.AddObjectToAsset(clip, controller);

                AnimatorControllerLayer[] ls = controller.layers;
                ls[i].defaultWeight = 1f;
                AnimatorStateMachine sm = ls[i].stateMachine;
                AnimatorState state = sm.AddState(c.Parameter.Replace("Macaron/", ""));
                state.motion = clip;
                state.timeParameterActive = true;
                state.timeParameter = c.Parameter;
                state.writeDefaultValues = false;   // MA "Match Avatar Write Defaults" adjusts this at build
                sm.defaultState = state;
                if(c.Toggle) ConfigureBoolStates(controller, sm, state, clip, c.Parameter, c.Default > 0.5f);
                controller.layers = ls;
            }

            // Uzumore / Local Only: _MCR_UzuIsLocal = 1 on the wearer's own client (MacaronUzumore.cs).
            if(uzumore) MacaronUzumore.AddIsLocalLayer(controller, avatarRoot, renderers);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
#endif
