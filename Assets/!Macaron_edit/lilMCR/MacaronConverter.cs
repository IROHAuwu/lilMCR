#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace liltoon_MCR
{
    /// <summary>
    /// Macaron > 角色轉換
    /// Drop an avatar, pick the shadow direction, press one button.
    ///  - lilToon (all variants, incl. transparent / fur / gem) -> same Macaron variant (copy)
    ///  - Already Macaron -> settings reset to the defaults in place
    ///  - Original materials are never modified.
    /// </summary>
    public class MacaronConverter : EditorWindow
    {
        private enum Job { Convert, Reset, Skip }

        private class Entry
        {
            public Material Material;
            public Renderer Renderer;
            public Job Action;
            public string Target;
            public string Note;
        }

        private GameObject avatar;
        private int space = 0;
        private Vector3 direction;
        private bool directionInit;

        private List<Entry> plan = new List<Entry>();
        private bool planDirty = true;
        private bool showList;
        private const string PrefAddAdjuster = "Macaron.Converter.AddLightAdjuster";
        // Quality tier of the converted materials: 0 Lightest .. 4 Overkill.
        private const string PrefTier = "Macaron.Converter.Tier5";
        private const string PrefRestoreCleanup = "Macaron.Converter.RestoreCleanup";
        // Where occlusion and contact shadow come from: 0 = camera depth texture, 1 = Material Contacts (MacaronContactRig.cs).
        private const string PrefContactMode = "Macaron.Converter.ContactMode";
        private static bool ContactModeSelected { get { return EditorPrefs.GetInt(PrefContactMode, 0) == 1 && MacaronContactRig.Supported; } }
        // Stored in the .meta (importer userData) of every converted material: "MacaronSource:<guid>:<local id>".
        private const string SourceTag = "MacaronSource:";
        private Dictionary<Material, Material> restoreMap = new Dictionary<Material, Material>();
        // FindSource results (its name fallback searches the project). Cleared when the project changes.
        private readonly Dictionary<Material, Material> sourceCache = new Dictionary<Material, Material>();
        private int restoreMissing;
        private Vector2 scroll;
        private string resultMessage;
        private MessageType resultType = MessageType.Info;

        //------------------------------------------------------------------------------------------
        // lilToon 2.x shader name -> Macaron variant of the same kind.
        private static readonly Dictionary<string, string> LilToonToMacaron = BuildMap();

        private static Dictionary<string, string> BuildMap()
        {
            string r = MacaronCore.ShaderRoot;
            string h = "Hidden/" + r;
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "lilToon",                                             r + "/lilToon" },
                { "Hidden/lilToonCutout",                                h + "/Cutout" },
                { "Hidden/lilToonTransparent",                           h + "/Transparent" },
                { "Hidden/lilToonOnePassTransparent",                    h + "/OnePassTransparent" },
                { "Hidden/lilToonTwoPassTransparent",                    h + "/TwoPassTransparent" },
                { "Hidden/lilToonOutline",                               h + "/OpaqueOutline" },
                { "Hidden/lilToonCutoutOutline",                         h + "/CutoutOutline" },
                { "Hidden/lilToonTransparentOutline",                    h + "/TransparentOutline" },
                { "Hidden/lilToonOnePassTransparentOutline",             h + "/OnePassTransparentOutline" },
                { "Hidden/lilToonTwoPassTransparentOutline",             h + "/TwoPassTransparentOutline" },
                { "Hidden/lilToonTessellation",                          h + "/Tessellation/Opaque" },
                { "Hidden/lilToonTessellationCutout",                    h + "/Tessellation/Cutout" },
                { "Hidden/lilToonTessellationTransparent",               h + "/Tessellation/Transparent" },
                { "Hidden/lilToonTessellationOnePassTransparent",        h + "/Tessellation/OnePassTransparent" },
                { "Hidden/lilToonTessellationTwoPassTransparent",        h + "/Tessellation/TwoPassTransparent" },
                { "Hidden/lilToonTessellationOutline",                   h + "/Tessellation/OpaqueOutline" },
                { "Hidden/lilToonTessellationCutoutOutline",             h + "/Tessellation/CutoutOutline" },
                { "Hidden/lilToonTessellationTransparentOutline",        h + "/Tessellation/TransparentOutline" },
                { "Hidden/lilToonTessellationOnePassTransparentOutline", h + "/Tessellation/OnePassTransparentOutline" },
                { "Hidden/lilToonTessellationTwoPassTransparentOutline", h + "/Tessellation/TwoPassTransparentOutline" },
                { "Hidden/lilToonLite",                                  r + "/lilToonLite" },
                { "Hidden/lilToonLiteCutout",                            h + "/Lite/Cutout" },
                { "Hidden/lilToonLiteTransparent",                       h + "/Lite/Transparent" },
                { "Hidden/lilToonLiteOnePassTransparent",                h + "/Lite/OnePassTransparent" },
                { "Hidden/lilToonLiteTwoPassTransparent",                h + "/Lite/TwoPassTransparent" },
                { "Hidden/lilToonLiteOutline",                           h + "/Lite/OpaqueOutline" },
                { "Hidden/lilToonLiteCutoutOutline",                     h + "/Lite/CutoutOutline" },
                { "Hidden/lilToonLiteTransparentOutline",                h + "/Lite/TransparentOutline" },
                { "Hidden/lilToonLiteOnePassTransparentOutline",         h + "/Lite/OnePassTransparentOutline" },
                { "Hidden/lilToonLiteTwoPassTransparentOutline",         h + "/Lite/TwoPassTransparentOutline" },
                { "Hidden/lilToonRefraction",                            h + "/Refraction" },
                { "Hidden/lilToonRefractionBlur",                        h + "/RefractionBlur" },
                { "Hidden/lilToonFur",                                   h + "/Fur" },
                { "Hidden/lilToonFurCutout",                             h + "/FurCutout" },
                { "Hidden/lilToonFurTwoPass",                            h + "/FurTwoPass" },
                { "Hidden/lilToonGem",                                   h + "/Gem" },
                { "_lil/lilToonMulti",                                   r + "/lilToonMulti" },
                { "Hidden/lilToonMultiOutline",                          h + "/MultiOutline" },
                { "Hidden/lilToonMultiRefraction",                       h + "/MultiRefraction" },
                { "Hidden/lilToonMultiFur",                              h + "/MultiFur" },
                { "Hidden/lilToonMultiGem",                              h + "/MultiGem" },
            };
        }

        [MenuItem("Macaron/lilMCR/Avatar Converter 変換", false, 0)]
        public static void Open()
        {
            MacaronConverter w = GetWindow<MacaronConverter>();
            w.titleContent = new GUIContent(ML.T("Macaron 角色轉換"));
            w.minSize = new Vector2(400f, 560f);
            if(Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject) && w.avatar == null)
            {
                Transform root = MacaronCore.FindAvatarRoot(Selection.activeGameObject.transform);
                w.avatar = root != null ? root.gameObject : null;
                w.planDirty = true;
            }
        }

        private void OnHierarchyChange() { planDirty = true; Repaint(); }
        private void OnProjectChange() { sourceCache.Clear(); planDirty = true; Repaint(); }
        private void OnFocus() { planDirty = true; }

        //------------------------------------------------------------------------------------------
        // GUI

        private void OnGUI()
        {
            if(!directionInit)
            {
                direction = MacaronCore.DefaultDirection();
                space = MacaronCore.DefaultSpace();
                directionInit = true;
            }

            titleContent.text = ML.T("Macaron 角色轉換");
            MacaronGUI.Banner("Macaron", ML.T("把角色拖進來，選好影子方向，按一下就完成。"));
            if(ML.LanguagePopup()) planDirty = true;   // the notes in the plan are written in the current language

            if(MacaronCore.MainShader == null)
            {
                EditorGUILayout.HelpBox(ML.F("找不到 Macaron 著色器（{0}）。\n請確認 lilToon 已安裝，且著色器資料夾裡的 .lilcontainer 檔案完整。", MacaronCore.ShaderMain), MessageType.Error);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            // 1. Avatar
            MacaronGUI.Step(1, ML.T("選擇角色"));
            EditorGUI.BeginChangeCheck();
            avatar = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true, GUILayout.Height(20f));
            if(EditorGUI.EndChangeCheck())
            {
                planDirty = true;
                resultMessage = null;
            }

            if(avatar != null && EditorUtility.IsPersistent(avatar))
            {
                EditorGUILayout.HelpBox(ML.T("請放入場景（Hierarchy）中的角色，而不是 Project 裡的 Prefab。"), MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(avatar == null)
            {
                EditorGUILayout.LabelField(ML.T("從 Hierarchy 拖曳角色到上方欄位。"), EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                if(planDirty) BuildPlan();
                DrawPlanSummary();
            }

            GUILayout.Space(12f);

            // 2. Shadow direction
            MacaronGUI.Step(2, ML.T("影子方向"));
            space = GUILayout.Toolbar(space, ML.TA(MacaronCore.SpaceLabels));
            EditorGUILayout.LabelField(ML.T(MacaronCore.SpaceHelp[space]), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4f);
            MacaronGUI.DirectionPad(ref direction, space, false);

            GUILayout.Space(14f);

            // 3. Convert
            MacaronGUI.Step(3, ML.T("轉換"));
            EditorGUILayout.LabelField(ML.T("轉換時統一最低亮度 0.06、最高亮度 1；Fur 材質關閉 AO 與接觸陰影。"), EditorStyles.wordWrappedMiniLabel);
            // Quality tier written to every converted / reset material.
            EditorGUILayout.LabelField(ML.T("品質檔次"), EditorStyles.boldLabel);
            int tier = Mathf.Clamp(EditorPrefs.GetInt(PrefTier, MacaronCore.TierStandard), 0, MacaronCore.TierOverkill);
            int picked = MacaronGUI.TierButtons(tier);
            if(picked >= 0) EditorPrefs.SetInt(PrefTier, picked);
            GUILayout.Space(4f);
            // Source of occlusion / contact shadow, written to every converted / reset material.
            EditorGUILayout.LabelField(ML.T("遮蔽與接觸陰影的計算方式"), EditorStyles.boldLabel);
            using(new EditorGUI.DisabledScope(!MacaronContactRig.Supported))
            {
                int mode = ContactModeSelected ? 1 : 0;
                int nextMode = EditorGUILayout.Popup(mode, new[] { ML.T("深度貼圖（原本的方式，細節完整）"), ML.T("Material Contacts（實驗，輕量但沒有細節）") });
                if(nextMode != mode) EditorPrefs.SetInt(PrefContactMode, nextMode);
            }
            if(!MacaronContactRig.Supported)
            {
                EditorGUILayout.LabelField(ML.T("Material Contacts 需要 VRChat SDK 3.10.6 以上。"), EditorStyles.wordWrappedMiniLabel);
            }
            else if(ContactModeSelected)
            {
                EditorGUILayout.LabelField(ML.T("用頭、軀幹、手臂、手、大腿的膠囊體計算遮蔽與接觸陰影，不讀深度貼圖，也不需要 MCR_DepthSource。只有大範圍的柔和陰影，沒有髮絲、皺摺等細節。會在角色底下建立 MCR_Contacts（Contact 數量會計入效能評級）。PCSS 不受影響。"), EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(4f);
            int work = CountWork();
            using(new EditorGUI.DisabledScope(avatar == null || work == 0))
            {
                if(MacaronGUI.BigButton(work > 0 ? ML.F("開始轉換（{0} 個材質）", work) : ML.T("開始轉換"), 36f))
                {
                    Run();
                }
            }
            EditorGUILayout.LabelField(ML.F("轉換後的材質存放在 {0}/<角色名稱>，原本的材質不會被修改。可用 Ctrl+Z 復原。", MacaronCore.ConvertedFolder), EditorStyles.wordWrappedMiniLabel);
            if(!ContactModeSelected)
            {
                EditorGUILayout.LabelField(ML.T("同時會在角色底下建立 MCR_DepthSource（讓 Unity 產生深度貼圖用的光源，已經有就不會重複建立）。"), EditorStyles.wordWrappedMiniLabel);
            }
            using(new EditorGUI.DisabledScope(!MacaronLightAdjuster.Available))
            {
                bool add = EditorPrefs.GetBool(PrefAddAdjuster, true);
                bool now = EditorGUILayout.ToggleLeft(ML.T("加入光照調節器（Modular Avatar 選單）"), add && MacaronLightAdjuster.Available);
                if(MacaronLightAdjuster.Available && now != add) EditorPrefs.SetBool(PrefAddAdjuster, now);
                if(now && MacaronLightAdjuster.Available)
                {
                    EditorGUI.indentLevel++;
                    for(int i = 0; i < MacaronLightAdjuster.ItemCount; i++)
                    {
                        bool on = MacaronLightAdjuster.ItemEnabled(i);
                        bool next = EditorGUILayout.ToggleLeft(MacaronLightAdjuster.ItemLabel(i), on);
                        if(next != on) MacaronLightAdjuster.SetItemEnabled(i, next);
                    }
                    EditorGUI.indentLevel--;
                    if(!MacaronLightAdjuster.AnyItemEnabled)
                    {
                        EditorGUILayout.LabelField(ML.T("沒有選擇任何光照項目：只加入埋入用的圖層（自己的視角不推回），不加入選單。"), EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            if(!MacaronLightAdjuster.Available)
            {
                EditorGUILayout.LabelField(ML.T("需要 Modular Avatar 才能使用光照調節器。"), EditorStyles.wordWrappedMiniLabel);
            }
            // Uzumore (MacaronUzumore.cs): on by default, switched in Batch Settings.
            EditorGUILayout.LabelField(ML.T("埋入（鏡頭穿入）預設開啟，自己的視角預設不推回（需要光照調節器）。推回範圍依角色身高、圖層間距依網格自動設定。可在批次設定中開關。"), EditorStyles.wordWrappedMiniLabel);

            if(!string.IsNullOrEmpty(resultMessage))
            {
                GUILayout.Space(6f);
                EditorGUILayout.HelpBox(resultMessage, resultType);
                if(GUILayout.Button(ML.T("開啟批次設定（一次調整全部材質）")))
                {
                    MacaronBatchEditor.OpenFor(avatar);
                }
            }

            GUILayout.Space(14f);
            MacaronGUI.Step(4, ML.T("遮罩生成器（選用）"));
            MacaronMaskLauncher.Draw(avatar);
            if(avatar != null) DrawRestore();

            EditorGUILayout.EndScrollView();
        }

        private void DrawRestore()
        {
            GUILayout.Space(14f);
            EditorGUILayout.LabelField(ML.T("額外操作（選用）"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(ML.T("還原"), EditorStyles.miniBoldLabel);
            using(new EditorGUI.DisabledScope(restoreMap.Count == 0))
            {
                if(GUILayout.Button(restoreMap.Count > 0 ? ML.F("換回原本的材質（{0} 個）", restoreMap.Count) : ML.T("換回原本的材質"), GUILayout.Height(24f)))
                {
                    Restore();
                }
            }
            bool cleanup = EditorPrefs.GetBool(PrefRestoreCleanup, true);
            bool nextCleanup = EditorGUILayout.ToggleLeft(ML.T("同時移除 MCR_DepthSource、光照調節器和世界落影"), cleanup);
            if(nextCleanup != cleanup) EditorPrefs.SetBool(PrefRestoreCleanup, nextCleanup);
            EditorGUILayout.LabelField(ML.T("只會換回角色上的材質，轉換產生的材質檔不會刪除。可用 Ctrl+Z 復原。"), EditorStyles.wordWrappedMiniLabel);
            if(restoreMissing > 0)
            {
                EditorGUILayout.LabelField(ML.F("有 {0} 個 Macaron 材質找不到原本的材質（原本的材質已被刪除或改名，或本來就是 Macaron 材質），會保持不變。", restoreMissing), EditorStyles.wordWrappedMiniLabel);
            }
        }

        private int CountWork()
        {
            int n = 0;
            foreach(Entry e in plan) if(e.Action != Job.Skip) n++;
            return n;
        }

        private void DrawPlanSummary()
        {
            int convert = 0, reset = 0, skip = 0;
            foreach(Entry e in plan)
            {
                switch(e.Action)
                {
                    case Job.Convert: convert++; break;
                    case Job.Reset: reset++; break;
                    default: skip++; break;
                }
            }

            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            MacaronGUI.Chip(ML.F("轉換 {0}", convert), MacaronGUI.Mint, 84f);
            MacaronGUI.Chip(ML.F("重置 {0}", reset), MacaronGUI.Lemon, 84f);
            MacaronGUI.Chip(ML.F("不支援 {0}", skip), MacaronGUI.Neutral, 96f);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if(reset > 0)
            {
                EditorGUILayout.LabelField(ML.T("「重置」= 已經是 Macaron 的材質，設定會恢復成預設值並套用下面的影子方向。"), EditorStyles.wordWrappedMiniLabel);
            }

            showList = EditorGUILayout.Foldout(showList, ML.F("材質清單（{0}）", plan.Count), true);
            if(!showList) return;

            foreach(Entry e in plan)
            {
                EditorGUILayout.BeginHorizontal();
                string tag;
                switch(e.Action)
                {
                    case Job.Convert: tag = ML.T("轉換"); break;
                    case Job.Reset: tag = ML.T("重置"); break;
                    default: tag = ML.T("略過"); break;
                }
                EditorGUILayout.LabelField(tag, EditorStyles.miniBoldLabel, GUILayout.Width(64f));
                using(new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(e.Material, typeof(Material), false);
                }
                EditorGUILayout.EndHorizontal();
                if(!string.IsNullOrEmpty(e.Note))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField(e.Note, EditorStyles.wordWrappedMiniLabel);
                    EditorGUI.indentLevel--;
                }
            }
        }

        //------------------------------------------------------------------------------------------
        // Plan

        private void BuildPlan()
        {
            planDirty = false;
            plan.Clear();
            restoreMap.Clear();
            restoreMissing = 0;
            if(avatar == null) return;

            HashSet<Material> seen = new HashSet<Material>();
            foreach(Renderer r in MacaronCore.AvatarRenderers(avatar))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || !seen.Add(m)) continue;
                    plan.Add(Classify(m, r));
                    if(!MacaronCore.IsMacaronShader(m.shader)) continue;
                    Material source;
                    if(!sourceCache.TryGetValue(m, out source)) sourceCache[m] = source = FindSource(m);
                    if(source != null) restoreMap[m] = source;
                    else restoreMissing++;
                }
            }
        }

        private static Entry Classify(Material m, Renderer r)
        {
            Entry e = new Entry { Material = m, Renderer = r, Action = Job.Skip };
            string shaderName = m.shader ? m.shader.name : string.Empty;

            if(MacaronCore.IsMacaronShader(m.shader))
            {
                e.Action = Job.Reset;
                return e;
            }

            string mapped;
            if(LilToonToMacaron.TryGetValue(shaderName, out mapped))
            {
                if(Shader.Find(mapped) == null)
                {
                    e.Note = ML.F("找不到轉換目標：{0}（.lilcontainer 可能被刪除）", mapped);
                    return e;
                }
                e.Action = Job.Convert;
                e.Target = mapped;
                return e;
            }

            e.Note = NameHas(shaderName, "liltoon")
                ? ML.F("沒有對應版本的 lilToon 著色器（[Optional] 系列或其他客製著色器）：{0}", shaderName)
                : ML.F("不支援的著色器：{0}", shaderName);
            return e;
        }

        // Case-insensitive substring test without allocating a lower-case copy of the name.
        private static bool NameHas(string name, string part)
        {
            return name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        //------------------------------------------------------------------------------------------
        // Run

        private void Run()
        {
            BuildPlan();
            if(avatar == null) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Macaron Avatar Convert");
            int undoGroup = Undo.GetCurrentGroup();

            // Material Contacts mode reads no depth texture: the depth light is not added, and an existing one is removed
            // unless World Shadow (which still needs the depth texture) is on the avatar.
            bool contactMode = ContactModeSelected;
            bool depthSourceAdded = false;
            bool depthSourceRemoved = false;
            if(!contactMode)
            {
                depthSourceAdded = EnsureDepthSource();
            }
            else
            {
                Transform depth = avatar.transform.Find(MacaronCore.DepthSourceName);
                if(depth != null && avatar.transform.Find(MacaronWorldShadow.ObjectName) == null)
                {
                    Undo.DestroyObjectImmediate(depth.gameObject);
                    depthSourceRemoved = true;
                }
            }
            int tier = Mathf.Clamp(EditorPrefs.GetInt(PrefTier, MacaronCore.TierStandard), 0, MacaronCore.TierOverkill);

            Dictionary<Material, Material> result = new Dictionary<Material, Material>(plan.Count);
            string folder = MacaronCore.ConvertedFolder + "/" + MacaronCore.SafeFileName(avatar.name);
            MacaronCore.EnsureFolder(folder);
            int converted = 0, reset = 0, skipped = 0;
            StringBuilder log = new StringBuilder();

            foreach(Entry e in plan)
            {
                Material output = null;
                switch(e.Action)
                {
                    case Job.Convert:
                        output = ConvertLilToon(e.Material, Shader.Find(e.Target), folder);
                        converted++;
                        log.AppendLine(ML.F("轉換  {0} : {1} → {2}", e.Material.name, e.Material.shader.name, e.Target));
                        break;
                    case Job.Reset:
                        Undo.RecordObject(e.Material, "Macaron Reset");
                        MacaronCore.ResetToDefaults(e.Material, true);
                        if(MacaronCore.SyncCapturePass(e.Material)) EditorUtility.SetDirty(e.Material);
                        output = e.Material;
                        reset++;
                        log.AppendLine(ML.F("重置  {0}", e.Material.name));
                        break;
                    default:
                        skipped++;
                        log.AppendLine(ML.F("略過  {0} : {1}", e.Material.name, e.Note));
                        break;
                }
                if(output == null) continue;

                MacaronCore.ApplyTier(output, tier);
                ApplyConversionPolicy(output);
                if(output.HasProperty("_MCR_ContactMode")) output.SetFloat("_MCR_ContactMode", contactMode ? 1f : 0f);
                MacaronCore.ApplyDirection(output, space, direction);
                MacaronCore.SetConvertedDirection(output, space, direction);
                MacaronCore.WriteAvatarBasis(output, e.Renderer, avatar.transform);
                EditorUtility.SetDirty(output);
                result[e.Material] = output;
            }

            // Swap the converted materials in on every renderer that uses one of the sources.
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                Material[] current = r.sharedMaterials;
                Material[] swapped = current.Select(x => x != null && result.ContainsKey(x) ? result[x] : x).ToArray();
                if(current.SequenceEqual(swapped)) continue;
                Undo.RecordObject(r, "Macaron Assign Materials");
                r.sharedMaterials = swapped;
                EditorUtility.SetDirty(r);
                if(PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }

            WarnSharedAxis(result, log);
            // Per-avatar part of the tier (lilMCR PCSS light resolution, when that add-on and its light exist).
            MacaronCore.ApplyTierToAvatar(avatar, tier);

            // Material Contacts: after the new materials are assigned (the receivers target renderers with Macaron
            // materials). In depth mode a rig left from an earlier conversion is removed.
            string contactMessage = null;
            if(contactMode)
            {
                contactMessage = MacaronContactRig.ResultMessage(MacaronContactRig.Setup(avatar));
                log.AppendLine(contactMessage);
            }
            else if(MacaronContactRig.Remove(avatar))
            {
                contactMessage = ML.T("已移除 MCR_Contacts。");
                log.AppendLine(contactMessage);
            }

            // Light Adjuster: after the new materials are assigned (it targets renderers with Macaron materials).
            string adjusterMessage = null;
            if(MacaronLightAdjuster.Available && EditorPrefs.GetBool(PrefAddAdjuster, true))
            {
                MacaronLightAdjuster.Result ar = MacaronLightAdjuster.Generate(avatar, folder);
                adjusterMessage = MacaronLightAdjuster.ResultMessage(ar);
                log.AppendLine(adjusterMessage);
            }

            // Uzumore per-avatar values (the IsLocal layer is part of the Light Adjuster above): after the new materials are assigned (it works on the avatar's
            // Macaron materials). The on / off switch comes from the tier.
            string uzumoreMessage = MacaronUzumore.Setup(avatar);
            log.AppendLine(uzumoreMessage);

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            foreach(Entry e in plan)
            {
                Material output;
                if(e.Action == Job.Convert && result.TryGetValue(e.Material, out output)) RecordSource(output, e.Material);
            }
            sourceCache.Clear();

            Debug.Log("[Macaron] " + avatar.name + "\n" + log);
            resultType = MessageType.Info;
            resultMessage = ML.F("完成！轉換 {0}、重置 {1}、略過 {2}。", converted, reset, skipped) +
                            (depthSourceAdded ? "\n" + ML.T("已把 MCR_DepthSource（產生深度貼圖用的光源）加到角色底下。") : string.Empty) +
                            (depthSourceRemoved ? "\n" + ML.T("已移除 MCR_DepthSource。") : string.Empty) +
                            (contactMessage != null ? "\n" + contactMessage : string.Empty) +
                            (adjusterMessage != null ? "\n" + adjusterMessage : string.Empty) +
                            (string.IsNullOrEmpty(uzumoreMessage) ? string.Empty : "\n" + uzumoreMessage) +
                            "\n" + ML.T("詳細內容已輸出到 Console。");
            planDirty = true;
        }

        // Used for both new conversions and re-converting/resetting existing lilMCR materials.
        private static void ApplyConversionPolicy(Material material)
        {
            if(material.HasProperty("_LightMinLimit")) material.SetFloat("_LightMinLimit", 0.06f);
            if(material.HasProperty("_LightMaxLimit")) material.SetFloat("_LightMaxLimit", 1.0f);
            if(material.shader != null && material.shader.name.IndexOf("Fur", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if(material.HasProperty("_MCR_OccEnable")) material.SetFloat("_MCR_OccEnable", 0.0f);
                if(material.HasProperty("_MCR_ShadowEnable")) material.SetFloat("_MCR_ShadowEnable", 0.0f);
            }
        }

        private static Material ConvertLilToon(Material source, Shader target, string folder)
        {
            // Assigning a shader resets the render queue to that shader's default: remember a custom one first.
            bool keepQueue = source.shader == null || source.renderQueue != source.shader.renderQueue;
            int queue = source.renderQueue;

            Material copy = new Material(source) { name = ConvertedName(source.name), shader = target };
            if(keepQueue) copy.renderQueue = queue;
            // The source may have used Macaron before and still store _MCR_ values: start from the defaults.
            MacaronCore.ResetToDefaults(copy, true);
            // A converted material has no world shadow capture until World Shadow is created: clear a capture key
            // copied from the source and turn the capture pass off.
            if(copy.HasProperty("_MCR_WorldCapture")) copy.SetVector("_MCR_WorldCapture", Vector4.zero);
            MacaronCore.SyncCapturePass(copy);
            return Save(copy, folder);
        }

        // CreateAsset turns the object itself into the asset, so the same instance is returned (no reload).
        // A name already taken in the folder gets Unity's " 1", " 2"... numbering.
        private static Material Save(Material m, string folder)
        {
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + MacaronCore.SafeFileName(m.name) + ".mat"));
            return m;
        }

        // Output name = the source name with every Unity " (Instance)" tag and any earlier Macaron suffix removed,
        // plus one Macaron suffix. Converting a converted material therefore never stacks "_MCR_MCR".
        private static string ConvertedName(string name)
        {
            string core = name.Replace(" (Instance)", string.Empty).TrimEnd();
            string suffix = MacaronCore.ConvertedSuffix;
            while(core.Length > suffix.Length && core.EndsWith(suffix, StringComparison.Ordinal)) core = core.Substring(0, core.Length - suffix.Length);
            return core + suffix;
        }

        //------------------------------------------------------------------------------------------
        // Restore

        // Remembers which material a converted one was made from. Written to the converted material's .meta after the
        // asset is saved, so the material itself is not reimported while it is still being edited.
        private static void RecordSource(Material converted, Material source)
        {
            string guid;
            long localId;
            if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out guid, out localId)) return;   // not an asset
            string path = AssetDatabase.GetAssetPath(converted);
            AssetImporter importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path);
            if(importer == null) return;
            importer.userData = SourceTag + guid + ":" + localId;
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        // The material a converted one was made from, or null. Materials converted before the source was recorded are
        // matched by name (the converted name minus the Macaron suffix), only when exactly one non-Macaron material
        // outside the converted folder has that name.
        private static Material FindSource(Material converted)
        {
            string path = AssetDatabase.GetAssetPath(converted);
            if(string.IsNullOrEmpty(path)) return null;

            AssetImporter importer = AssetImporter.GetAtPath(path);
            string data = importer != null ? importer.userData : null;
            if(!string.IsNullOrEmpty(data) && data.StartsWith(SourceTag, StringComparison.Ordinal))
            {
                string[] parts = data.Substring(SourceTag.Length).Split(':');
                string sourcePath = AssetDatabase.GUIDToAssetPath(parts[0]);
                long wantedId = 0;
                bool hasId = parts.Length > 1 && long.TryParse(parts[1], out wantedId);
                if(!string.IsNullOrEmpty(sourcePath))
                {
                    foreach(UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                    {
                        Material m = o as Material;
                        if(m == null || m == converted) continue;
                        string g;
                        long id;
                        if(!hasId || (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out g, out id) && id == wantedId)) return m;
                    }
                }
                return null;   // recorded source was deleted
            }

            string suffix = MacaronCore.ConvertedSuffix;
            if(!path.StartsWith(MacaronCore.ConvertedFolder + "/", StringComparison.Ordinal)) return null;
            if(!converted.name.EndsWith(suffix, StringComparison.Ordinal)) return null;
            string core = converted.name.Substring(0, converted.name.Length - suffix.Length);
            if(core.Length == 0) return null;

            Material found = null;
            foreach(string guid in AssetDatabase.FindAssets(core + " t:Material"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if(p.StartsWith(MacaronCore.ConvertedFolder + "/", StringComparison.Ordinal)) continue;
                Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if(m == null || m.name != core || MacaronCore.IsMacaronShader(m.shader)) continue;
                if(found != null && found != m) return null;   // more than one candidate: do not guess
                found = m;
            }
            return found;
        }

        private void Restore()
        {
            BuildPlan();
            if(avatar == null || restoreMap.Count == 0) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Macaron Restore Materials");
            int undoGroup = Undo.GetCurrentGroup();

            int restored = restoreMap.Count;
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                Material[] current = r.sharedMaterials;
                Material[] swapped = current.Select(x => x != null && restoreMap.ContainsKey(x) ? restoreMap[x] : x).ToArray();
                if(current.SequenceEqual(swapped)) continue;
                Undo.RecordObject(r, "Macaron Restore Materials");
                r.sharedMaterials = swapped;
                EditorUtility.SetDirty(r);
                if(PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }

            StringBuilder message = new StringBuilder(ML.F("已換回 {0} 個原本的材質。", restored));
            if(EditorPrefs.GetBool(PrefRestoreCleanup, true))
            {
                // World Shadow first: the restored materials are not excluded from its projection.
                Transform worldShadow = avatar.transform.Find(MacaronWorldShadow.ObjectName);
                if(worldShadow != null)
                {
                    Undo.DestroyObjectImmediate(worldShadow.gameObject);
                    message.Append("\n").Append(ML.T("已移除世界落影。"));
                }
                Transform depth = avatar.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != avatar.transform && t.name == MacaronCore.DepthSourceName);
                if(depth != null && avatar.transform.Find(MacaronWorldShadow.ObjectName) == null)
                {
                    Undo.DestroyObjectImmediate(depth.gameObject);
                    message.Append("\n").Append(ML.T("已移除 MCR_DepthSource。"));
                }
                Transform adjuster = MacaronLightAdjuster.FindAdjuster(avatar);
                if(adjuster != null)
                {
                    Undo.DestroyObjectImmediate(adjuster.gameObject);
                    message.Append("\n").Append(ML.T("已移除光照調節器。"));
                }
                if(MacaronContactRig.Remove(avatar))
                {
                    message.Append("\n").Append(ML.T("已移除 MCR_Contacts。"));
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            resultType = MessageType.Info;
            resultMessage = message.ToString();
            planDirty = true;
        }

        // One material used by renderers with different object spaces (e.g. body + a hat on the head bone)
        // can only store one avatar axis. Report it so the user can split the material if needed.
        private void WarnSharedAxis(Dictionary<Material, Material> result, StringBuilder log)
        {
            Dictionary<Material, Transform> first = new Dictionary<Material, Transform>();
            HashSet<Material> warned = new HashSet<Material>();
            foreach(Renderer r in MacaronCore.AvatarRenderers(avatar))
            {
                Transform os = MacaronCore.ObjectSpaceTransform(r);
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || !result.ContainsValue(m)) continue;
                    Transform prev;
                    if(!first.TryGetValue(m, out prev)) { first[m] = os; continue; }
                    if(prev == os || warned.Contains(m)) continue;
                    if(Quaternion.Angle(prev.rotation, os.rotation) > 1f)
                    {
                        warned.Add(m);
                        log.AppendLine(ML.F("注意  {0} 被多個不同骨骼空間的 Renderer 共用（{1} / {2}），「跟隨角色」方向以 {1} 為準。", m.name, prev.name, os.name));
                    }
                }
            }
        }

        //------------------------------------------------------------------------------------------
        // Depth source

        // Occlusion and Contact Shadow read the camera depth texture, which Unity only renders while a directional light
        // with shadows is active. The converter adds one under the avatar root that lights nothing visible: black,
        // almost zero intensity, per-vertex ("Not Important", no extra pixel-light pass) and limited to the VRChat
        // layers StereoLeft / MirrorReflection. An existing MCR_DepthSource keeps its object but gets these light settings.
        // Returns true when a new object was created.
        private bool EnsureDepthSource() { return MacaronDepthSource.Ensure(avatar); }
    }
}
#endif
