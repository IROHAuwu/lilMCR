#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace liltoon_MCR
{
    /// <summary>
    /// Macaron > Batch Settings
    /// Change Macaron settings on many converted materials at once.
    /// 1. Pick the targets (an avatar / a folder / the whole project).
    /// 2. Change a value: it is written to every target material immediately (one Undo step per change).
    ///    The rows show the first target's values; rows where the targets differ are drawn as "mixed" (—).
    /// </summary>
    public class MacaronBatchEditor : EditorWindow
    {
        private enum Source { Avatar, Folder, Project }
        private static readonly string[] SourceLabels = { "角色", "資料夾", "整個專案" };

        private Source source = Source.Avatar;
        private GameObject avatar;
        private DefaultAsset folder;

        private readonly List<Material> targets = new List<Material>();
        private readonly HashSet<Material> excluded = new HashSet<Material>();
        private bool targetsDirty = true;
        private bool showTargets;

        // Values being edited live on a hidden material, so every type uses the same code path.
        // It always mirrors the first active target (reloaded whenever the target list changes).
        private Material work;
        private List<Material> activeCache = new List<Material>();
        private readonly Dictionary<string, bool> sectionOpen = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> sectionAdvanced = new Dictionary<string, bool>();
        private Material loadFrom;
        private Vector2 scroll;
        private string status;
        // IsMixed results of the current Layout / Repaint pair (rebuilt on every Layout event).
        private readonly Dictionary<string, bool> mixedCache = new Dictionary<string, bool>();

        // "lilToon 標準設定": lilToon's own settings, on the Macaron targets above and (optionally) on the plain lilToon
        // materials of the same source (lilTargets = lilToon materials that are not Macaron).
        private readonly List<Material> lilTargets = new List<Material>();
        private List<Material> lilCache = new List<Material>();
        private bool lilOpen;
        private bool includePlainLil = true;
        private bool drawingLil;          // true while the lilToon rows are drawn / applied

        [MenuItem("Macaron/lilMCR/Batch Settings 一括設定", false, 1)]
        public static void Open()
        {
            MacaronBatchEditor w = GetWindow<MacaronBatchEditor>();
            w.titleContent = new GUIContent(ML.T("Macaron 批次設定"));
            w.minSize = new Vector2(420f, 560f);
            if(w.avatar == null && Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject))
            {
                Transform root = MacaronCore.FindAvatarRoot(Selection.activeGameObject.transform);
                if(root != null) w.avatar = root.gameObject;
            }
            w.targetsDirty = true;
        }

        public static void OpenFor(GameObject avatar)
        {
            Open();
            MacaronBatchEditor w = GetWindow<MacaronBatchEditor>();
            w.source = Source.Avatar;
            w.avatar = avatar;
            w.targetsDirty = true;
        }

        private void OnEnable() { targetsDirty = true; Undo.undoRedoPerformed += OnUndo; }
        private void OnUndo()
        {
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            CollectTargets();
            activeCache = ActiveTargets();
            lilCache = LilScopeTargets();
            Repaint();
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            if(work != null) DestroyImmediate(work);
            work = null;
        }
        // Only the avatar source depends on the hierarchy; folder / project sources are rescanned on project changes.
        private void OnHierarchyChange() { if(source == Source.Avatar) { targetsDirty = true; Repaint(); } }
        private void OnProjectChange() { targetsDirty = true; Repaint(); }

        private bool EnsureWork()
        {
            if(work != null) return true;
            Shader shader = MacaronCore.MainShader;
            if(shader == null) return false;
            work = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "Macaron Batch (temp)" };
            return true;
        }

        //------------------------------------------------------------------------------------------
        // GUI

        private void OnGUI()
        {
            titleContent.text = ML.T("Macaron 批次設定");
            MacaronGUI.Banner(ML.T("Macaron 批次設定"), ML.T("一次修改所有已轉換材質的設定。改動會立刻套用到全部目標材質。"));
            ML.LanguagePopup();

            if(!EnsureWork())
            {
                EditorGUILayout.HelpBox(ML.F("找不到 Macaron 著色器（{0}）。", MacaronCore.ShaderMain), MessageType.Error);
                return;
            }

            if(targetsDirty) CollectTargets();
            activeCache = ActiveTargets();
            lilCache = LilScopeTargets();
            if(Event.current.type == EventType.Layout) mixedCache.Clear();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            // 1. Targets
            MacaronGUI.Step(1, ML.T("要修改哪些材質"));
            EditorGUI.BeginChangeCheck();
            source = (Source)GUILayout.Toolbar((int)source, ML.TA(SourceLabels));
            switch(source)
            {
                case Source.Avatar:
                    avatar = (GameObject)EditorGUILayout.ObjectField(ML.T("角色"), avatar, typeof(GameObject), true);
                    break;
                case Source.Folder:
                    folder = (DefaultAsset)EditorGUILayout.ObjectField(ML.T("資料夾"), folder, typeof(DefaultAsset), false);
                    if(folder == null && GUILayout.Button(ML.F("使用轉換資料夾（{0}）", MacaronCore.ConvertedFolder), EditorStyles.miniButton))
                    {
                        folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(MacaronCore.ConvertedFolder);
                    }
                    break;
                default:
                    EditorGUILayout.LabelField(ML.T("Assets 內所有使用 Macaron 著色器的材質。"), EditorStyles.wordWrappedMiniLabel);
                    break;
            }
            if(EditorGUI.EndChangeCheck())
            {
                excluded.Clear();
                CollectTargets();
                activeCache = ActiveTargets();
                lilCache = LilScopeTargets();
            }

            int active = activeCache.Count;
            showTargets = EditorGUILayout.Foldout(showTargets, ML.F("找到 {0} 個 Macaron 材質（{1} 個會被修改）", targets.Count, active), true);
            if(showTargets) DrawTargetList();

            GUILayout.Space(10f);

            // 2. Values (written immediately)
            MacaronGUI.Step(2, ML.T("修改設定（立即套用）"));
            if(active == 0)
            {
                EditorGUILayout.HelpBox(ML.T("沒有要修改的材質。請先在上面選擇角色、資料夾或整個專案。"), MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(ML.T("顯示的是第一個材質的數值；顯示「—」的項目代表各材質的數值不同，修改後會統一。"), EditorStyles.wordWrappedMiniLabel);
            }
            // One-click quality tiers for every active material.
            using(new EditorGUI.DisabledScope(active == 0))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(ML.T("一鍵套用品質檔次"), EditorStyles.boldLabel);
                int tier = MacaronGUI.TierButtons();
                if(tier >= 0)
                {
                    int n = MacaronCore.ApplyTier(ActiveTargets(), tier);
                    if(source == Source.Avatar) MacaronCore.ApplyTierToAvatar(avatar, tier);
                    AssetDatabase.SaveAssets();
                    LoadFromFirstTarget();
                    status = ML.F("「{0}」已套用到 {1} 個材質。（可用 Ctrl+Z 復原）", ML.T(MacaronCore.TierLabels[tier]), n);
                }
            }
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            loadFrom = (Material)EditorGUILayout.ObjectField(loadFrom, typeof(Material), false);
            using(new EditorGUI.DisabledScope(active == 0 || loadFrom == null || !MacaronCore.IsMacaronMaterial(loadFrom)))
            {
                if(GUILayout.Button(ML.T("套用這個材質的全部設定"), EditorStyles.miniButton, GUILayout.Width(170f))) ApplyAllFrom(loadFrom);
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4f);

            float oldLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(180f, position.width * 0.38f);
            using(new EditorGUI.DisabledScope(active == 0))
            {
                foreach(MacaronSection section in MacaronCore.Sections) DrawSection(section);
            }

            // Parent: standard lilToon settings (its own targets, so it also works when there is no Macaron material).
            GUILayout.Space(8f);
            DrawLilToonSettings();
            EditorGUIUtility.labelWidth = oldLabel;

            EditorGUILayout.EndScrollView();

            // Tools (fixed at the bottom)
            GUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            using(new EditorGUI.DisabledScope(active == 0))
            {
                if(GUILayout.Button(ML.T("全部材質重置為預設值"), EditorStyles.miniButton)) ResetTargets();
                using(new EditorGUI.DisabledScope(source != Source.Avatar || avatar == null))
                {
                    if(GUILayout.Button(ML.T("重新偵測角色方向"), EditorStyles.miniButton)) RefreshAxis();
                }
            }
            EditorGUILayout.EndHorizontal();
            if(!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawTargetList()
        {
            EditorGUI.indentLevel++;
            foreach(Material m in targets)
            {
                EditorGUILayout.BeginHorizontal();
                bool on = !excluded.Contains(m);
                bool now = EditorGUILayout.Toggle(on, GUILayout.Width(30f));
                if(now != on)
                {
                    if(now) excluded.Remove(m); else excluded.Add(m);
                    activeCache = ActiveTargets();
                    LoadFromFirstTarget();
                }
                using(new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(m, typeof(Material), false);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        private void DrawSection(MacaronSection section)
        {
            bool open;
            if(!sectionOpen.TryGetValue(section.Id, out open)) open = false;     // sections start folded

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            open = EditorGUILayout.Foldout(open, section.DisplayTitle, true, EditorStyles.foldoutHeader);
            sectionOpen[section.Id] = open;

            if(open)
            {
                if(drawingLil && section.Help != null) EditorGUILayout.LabelField(ML.T(section.Help), EditorStyles.wordWrappedMiniLabel);
                if(section.EnableProp != null) DrawRow(new MacaronProp { Name = section.EnableProp, Label = "啟用", Kind = MacaronPropKind.Toggle });

                bool hasAdvanced = false;
                foreach(MacaronProp prop in section.Props)
                {
                    if(prop.Advanced) { hasAdvanced = true; continue; }
                    DrawRow(prop);
                }
                if(hasAdvanced)
                {
                    bool adv;
                    sectionAdvanced.TryGetValue(section.Id, out adv);
                    adv = MacaronGUI.InsetFoldout(adv, ML.T("進階設定"));
                    sectionAdvanced[section.Id] = adv;
                    if(adv)
                    {
                        EditorGUI.indentLevel++;
                        foreach(MacaronProp prop in section.Props) if(prop.Advanced) DrawRow(prop);
                        EditorGUI.indentLevel--;
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private float WorkValue(string name)
        {
            return work != null && work.HasProperty(name) ? work.GetFloat(name) : 0f;
        }

        private void DrawRow(MacaronProp prop)
        {
            if(!prop.IsVisible(WorkValue)) return;
            if(!work.HasProperty(prop.Name)) return;
            if(MacaronCore.IsAvatarDataProp(prop.Name)) return;

            if(prop.SubHeader != null)
            {
                GUILayout.Space(2f);
                EditorGUILayout.LabelField(prop.Text(prop.SubHeader), EditorStyles.miniBoldLabel);
            }

            bool mixed = IsMixed(prop.Name);

            if(prop.Kind == MacaronPropKind.Direction)
            {
                EditorGUILayout.LabelField(prop.Content);
                Vector4 v = work.GetVector(prop.Name);
                Vector3 dir = new Vector3(v.x, v.y, v.z);
                int padSpace = Mathf.RoundToInt(WorkValue(prop.SpaceProp ?? MacaronCore.PropShadowSpace));
                Vector3 reset = MacaronCore.DefaultDirectionFor(activeCache.Count > 0 ? activeCache[0] : null, padSpace);
                if(MacaronGUI.DirectionPad(ref dir, padSpace, mixed, reset))
                {
                    work.SetVector(prop.Name, new Vector4(dir.x, dir.y, dir.z, 0f));
                    ApplyToTargets(prop);
                }
                return;
            }

            GUIContent label = prop.Content;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            switch(prop.Kind)
            {
                case MacaronPropKind.Toggle:
                {
                    bool b = EditorGUILayout.Toggle(label, work.GetFloat(prop.Name) > 0.5f);
                    if(EditorGUI.EndChangeCheck()) { work.SetFloat(prop.Name, b ? 1f : 0f); ApplyToTargets(prop); }
                    break;
                }
                case MacaronPropKind.Popup:
                {
                    float[] values;
                    GUIContent[] options = prop.Choices(WorkValue, out values);
                    float cur = prop.ShownValue(work.GetFloat(prop.Name), WorkValue);
                    int idx = 0;
                    for(int i = 0; i < values.Length; i++) if(Mathf.Approximately(cur, values[i])) idx = i;
                    idx = EditorGUILayout.Popup(label, idx, options);
                    if(EditorGUI.EndChangeCheck()) { work.SetFloat(prop.Name, values[idx]); ApplyToTargets(prop); }
                    break;
                }
                case MacaronPropKind.Color:
                {
                    Color c = prop.Hdr
                        ? EditorGUILayout.ColorField(label, work.GetColor(prop.Name), true, true, true)
                        : EditorGUILayout.ColorField(label, work.GetColor(prop.Name));
                    if(EditorGUI.EndChangeCheck()) { work.SetColor(prop.Name, c); ApplyToTargets(prop); }
                    break;
                }
                case MacaronPropKind.Range2:
                {
                    Vector4 v = work.GetVector(prop.Name);
                    EditorGUILayout.LabelField(label);
                    EditorGUI.indentLevel++;
                    float x = EditorGUILayout.Slider(ML.T(prop.PairLabels[0]), v.x, prop.PairMin, prop.PairMax);
                    float y = EditorGUILayout.Slider(ML.T(prop.PairLabels[1]), v.y, prop.PairMin, prop.PairMax);
                    EditorGUI.indentLevel--;
                    if(EditorGUI.EndChangeCheck()) { work.SetVector(prop.Name, new Vector4(x, y, v.z, v.w)); ApplyToTargets(prop); }
                    break;
                }
                case MacaronPropKind.Vec4:
                {
                    Vector4 v = work.GetVector(prop.Name);
                    EditorGUILayout.LabelField(label);
                    EditorGUI.indentLevel++;
                    // 2 to 4 values; without a range they are plain number fields (e.g. lilToon's AO map scale / offset).
                    int count = Mathf.Min(prop.VecLabels.Length, 4);
                    for(int c = 0; c < count; c++)
                    {
                        string vl = prop.Text(prop.VecLabels[c]);
                        if(prop.VecMin != null && prop.VecMax != null) v[c] = EditorGUILayout.Slider(vl, v[c], prop.VecMin[c], prop.VecMax[c]);
                        else v[c] = EditorGUILayout.FloatField(vl, v[c]);
                    }
                    EditorGUI.indentLevel--;
                    if(EditorGUI.EndChangeCheck()) { work.SetVector(prop.Name, v); ApplyToTargets(prop); }
                    break;
                }
                case MacaronPropKind.Texture:
                {
                    Texture tex = (Texture)EditorGUILayout.ObjectField(label, work.GetTexture(prop.Name), typeof(Texture), false, GUILayout.Height(18f));
                    if(EditorGUI.EndChangeCheck()) { work.SetTexture(prop.Name, tex); ApplyToTargets(prop); }
                    break;
                }
                default:
                {
                    float value = work.GetFloat(prop.Name);
                    Vector2 range;
                    if(TryGetRange(prop.Name, out range)) value = EditorGUILayout.Slider(label, value, range.x, range.y);
                    else value = EditorGUILayout.FloatField(label, value);
                    if(EditorGUI.EndChangeCheck()) { work.SetFloat(prop.Name, value); ApplyToTargets(prop); }
                    break;
                }
            }
            EditorGUI.showMixedValue = false;
        }

        // true when the active targets do not all have the same value for this property.
        private bool IsMixed(string name)
        {
            bool mixed;
            if(mixedCache.TryGetValue(name, out mixed)) return mixed;
            mixed = ComputeMixed(name);
            mixedCache[name] = mixed;
            return mixed;
        }

        private bool ComputeMixed(string name)
        {
            List<Material> scope = drawingLil ? lilCache : activeCache;
            if(scope.Count < 2) return false;
            Shader s = work.shader;
            int i = s.FindPropertyIndex(name);
            if(i < 0) return false;
            ShaderPropertyType type = s.GetPropertyType(i);
            Material first = null;
            foreach(Material m in scope) if(m != null && m.HasProperty(name)) { first = m; break; }
            if(first == null) return false;
            for(int k = 0; k < scope.Count; k++)
            {
                Material m = scope[k];
                if(m == first) continue;
                if(m == null || !m.HasProperty(name)) continue;
                switch(type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        if(!Mathf.Approximately(first.GetFloat(name), m.GetFloat(name))) return true;
                        break;
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        if((first.GetVector(name) - m.GetVector(name)).sqrMagnitude > 1e-10f) return true;
                        break;
                    case ShaderPropertyType.Texture:
                        if(first.GetTexture(name) != m.GetTexture(name)) return true;
                        break;
                }
            }
            return false;
        }

        private bool TryGetRange(string name, out Vector2 range)
        {
            range = Vector2.zero;
            Shader s = work.shader;
            int i = s.FindPropertyIndex(name);
            if(i < 0 || s.GetPropertyType(i) != ShaderPropertyType.Range) return false;
            range = s.GetPropertyRangeLimits(i);
            return true;
        }

        //------------------------------------------------------------------------------------------
        // Targets

        private List<Material> ActiveTargets()
        {
            List<Material> list = new List<Material>();
            foreach(Material m in targets) if(m != null && !excluded.Contains(m)) list.Add(m);
            return list;
        }

        private void CollectTargets()
        {
            targetsDirty = false;
            CollectTargetsCore();
            // The rows always show the current targets' values.
            LoadFromFirstTarget();
        }

        private void CollectTargetsCore()
        {
            targets.Clear();
            lilTargets.Clear();
            HashSet<Material> seen = new HashSet<Material>();

            if(source == Source.Avatar)
            {
                if(avatar == null) return;
                foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
                {
                    foreach(Material m in r.sharedMaterials) AddTarget(m, seen);
                }
                return;
            }

            string[] searchIn;
            if(source == Source.Folder)
            {
                if(folder == null) return;
                string path = AssetDatabase.GetAssetPath(folder);
                if(!AssetDatabase.IsValidFolder(path)) return;
                searchIn = new[] { path };
            }
            else
            {
                searchIn = new[] { "Assets" };
            }

            foreach(string guid in AssetDatabase.FindAssets("t:Material", searchIn))
            {
                AddTarget(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)), seen);
            }
        }

        // Macaron materials -> targets; other lilToon materials -> lilTargets (only used by "lilToon 標準設定").
        private void AddTarget(Material m, HashSet<Material> seen)
        {
            if(m == null || !seen.Add(m)) return;
            if(MacaronCore.IsMacaronMaterial(m)) targets.Add(m);
            else if(MacaronCore.IsLilToonMaterial(m)) lilTargets.Add(m);
        }

        /// <summary>Materials changed by the lilToon rows: the active Macaron targets, plus the plain lilToon ones when chosen.</summary>
        private List<Material> LilScopeTargets()
        {
            List<Material> list = ActiveTargets();
            if(includePlainLil) foreach(Material m in lilTargets) if(m != null) list.Add(m);
            return list;
        }

        //------------------------------------------------------------------------------------------
        // Actions

        private void LoadFrom(Material m)
        {
            if(m == null || work == null) return;
            foreach(int i in MacaronCore.McrPropertyIndices(work.shader))
            {
                MacaronCore.CopyProperty(m, work, work.shader.GetPropertyName(i));
            }
        }

        private void LoadFromFirstTarget()
        {
            if(work == null) return;
            List<Material> list = ActiveTargets();
            if(list.Count > 0) LoadFrom(list[0]);
            else MacaronCore.ResetToDefaults(work, true);
            LoadLilFromFirstTarget();
        }

        // The lilToon rows show the first lilToon target that has each property.
        private void LoadLilFromFirstTarget()
        {
            if(work == null) return;
            List<Material> list = LilScopeTargets();
            foreach(string name in MacaronCore.LilToonPropertyNames())
            {
                Material from = null;
                foreach(Material m in list) if(m != null && m.HasProperty(name)) { from = m; break; }
                if(from != null) MacaronCore.CopyProperty(from, work, name);
                else MacaronCore.ResetPropertyToDefault(work, name);
            }
        }

        private static bool IsDirectionProp(string name)
        {
            return name == MacaronCore.PropShadowDirection || name == MacaronCore.PropShadowSpace;
        }

        // One changed row -> every active target, immediately.
        private void ApplyToTargets(MacaronProp prop)
        {
            if(drawingLil) { ApplyLilToTargets(prop); return; }
            List<Material> mats = ActiveTargets();
            if(mats.Count == 0 || MacaronCore.IsAvatarDataProp(prop.Name)) return;

            // Unity merges the records of one drag (slider, color picker, direction pad) into one Undo step.
            Undo.RecordObjects(mats.ToArray(), "Macaron Batch Settings");
            foreach(Material m in mats)
            {
                // Method change: each material keeps its own per-method shared settings.
                if(prop.Name == MacaronCore.PropOccMethod) MacaronCore.SwitchOccMethod(m, work.GetFloat(prop.Name));
                else MacaronCore.CopyProperty(work, m, prop.Name);
                EditorUtility.SetDirty(m);
            }
            // A method change also restores that method's own shared settings: show them.
            if(prop.Name == MacaronCore.PropOccMethod) LoadFromFirstTarget();
            mixedCache.Clear();

            // Uzumore turned on for materials that were never set up (converted before this feature): their per-avatar
            // values (MacaronUzumore.Setup), so the switch alone is enough.
            if(prop.Name == "_MCR_UzuEnable" && work.GetFloat(prop.Name) > 0.5f && source == Source.Avatar && avatar != null
                && !MacaronUzumore.IsSetUp(mats))
            {
                Debug.Log("[Macaron] " + avatar.name + "\n" + MacaronUzumore.Setup(avatar));
            }

            // Direction changed on an avatar: refresh its axis too.
            if(source == Source.Avatar && avatar != null && IsDirectionProp(prop.Name))
            {
                MacaronCore.WriteAvatarBasisForAvatar(avatar, new HashSet<Material>(mats));
            }
            activeCache = mats;
            status = ML.F("「{0}」已套用到 {1} 個材質。（可用 Ctrl+Z 復原）", prop.Content.text, mats.Count);
        }

        // lilToon row -> every lilToon target that has the property. lilToonMulti keeps its features in shader keywords,
        // so those materials are set up again the way lilToon's own Inspector does after a change.
        private void ApplyLilToTargets(MacaronProp prop)
        {
            List<Material> mats = new List<Material>();
            foreach(Material m in LilScopeTargets()) if(m != null && m.HasProperty(prop.Name)) mats.Add(m);
            if(mats.Count == 0) return;

            Undo.RecordObjects(mats.ToArray(), "lilToon Batch Settings");
            foreach(Material m in mats)
            {
                MacaronCore.CopyProperty(work, m, prop.Name);
                if(m.shader.name.Contains("Multi")) global::lilToon.lilMaterialUtils.SetupMultiMaterial(m);
                EditorUtility.SetDirty(m);
            }
            mixedCache.Clear();
            lilCache = LilScopeTargets();
            status = ML.F("「{0}」已套用到 {1} 個材質。（可用 Ctrl+Z 復原）", prop.Content.text, mats.Count);
        }

        //------------------------------------------------------------------------------------------
        // lilToon standard settings (parent section)

        private void DrawLilToonSettings()
        {
            lilOpen = MacaronGUI.ParentFoldout(ML.T("lilToon 標準設定"), lilOpen);
            if(!lilOpen) return;

            EditorGUILayout.LabelField(ML.T("統一調整 lilToon 本身的設定（光照、陰影、邊緣光、背光、輪廓線、色調）。不會改動 Macaron 的設定。"), EditorStyles.wordWrappedMiniLabel);
            EditorGUI.BeginChangeCheck();
            includePlainLil = EditorGUILayout.ToggleLeft(ML.F("也套用到未轉換的 lilToon 材質（{0} 個）", lilTargets.Count), includePlainLil);
            if(EditorGUI.EndChangeCheck())
            {
                lilCache = LilScopeTargets();
                LoadLilFromFirstTarget();
                mixedCache.Clear();
            }

            int count = lilCache.Count;
            if(count == 0)
            {
                EditorGUILayout.HelpBox(ML.T("沒有可修改的 lilToon 材質。請先在上面選擇角色、資料夾或整個專案。"), MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField(ML.F("會修改 {0} 個 lilToon 材質。", count), EditorStyles.miniLabel);

            using(new MacaronGUI.ChildScope())
            {
                drawingLil = true;
                try
                {
                    foreach(MacaronSection section in MacaronCore.LilToonSections) DrawSection(section);
                }
                finally
                {
                    drawingLil = false;
                }
            }
        }

        private void ApplyAllFrom(Material sourceMaterial)
        {
            List<Material> mats = ActiveTargets();
            if(sourceMaterial == null || mats.Count == 0) return;
            if(!EditorUtility.DisplayDialog(ML.T("Macaron 批次設定"), ML.F("把「{0}」的全部 Macaron 設定套用到 {1} 個材質？\n（影子方向的角色軸會保留，可用 Ctrl+Z 復原）", sourceMaterial.name, mats.Count), ML.T("套用"), ML.T("取消"))) return;

            LoadFrom(sourceMaterial);
            Undo.RecordObjects(mats.ToArray(), "Macaron Batch Settings");
            foreach(Material m in mats)
            {
                if(m == sourceMaterial) continue;
                foreach(int i in MacaronCore.McrPropertyIndices(work.shader))
                {
                    string name = work.shader.GetPropertyName(i);
                    if(MacaronCore.IsAvatarDataProp(name) || MacaronCore.IsPerMeshProp(name)) continue;
                    MacaronCore.CopyProperty(work, m, name);
                }
                EditorUtility.SetDirty(m);
            }
            if(source == Source.Avatar && avatar != null) MacaronCore.WriteAvatarBasisForAvatar(avatar, new HashSet<Material>(mats));
            AssetDatabase.SaveAssets();
            status = ML.F("已把「{0}」的設定套用到 {1} 個材質。（可用 Ctrl+Z 復原）", sourceMaterial.name, mats.Count);
        }

        private void ResetTargets()
        {
            List<Material> mats = ActiveTargets();
            if(mats.Count == 0) return;
            if(!EditorUtility.DisplayDialog(ML.T("Macaron 批次設定"), ML.F("{0} 個材質的 Macaron 設定全部恢復成預設值？\n（影子方向的角色軸會保留，可用 Ctrl+Z 復原）", mats.Count), ML.T("重置"), ML.T("取消"))) return;

            Undo.RecordObjects(mats.ToArray(), "Macaron Batch Reset");
            foreach(Material m in mats)
            {
                MacaronCore.ResetToDefaults(m, true);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            LoadFromFirstTarget();
            status = ML.F("{0} 個材質已重置為預設值。", mats.Count);
        }

        private void RefreshAxis()
        {
            List<Material> mats = ActiveTargets();
            if(avatar == null || mats.Count == 0) return;
            Undo.RecordObjects(mats.ToArray(), "Macaron Refresh Avatar Axis");
            int n = MacaronCore.WriteAvatarBasisForAvatar(avatar, new HashSet<Material>(mats));
            AssetDatabase.SaveAssets();
            status = ML.F("已重新偵測 {0} 個材質的角色方向。", n);
        }
    }
}
#endif
