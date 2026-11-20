#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using liltoon_MCR;

namespace lilMCR.PCSS
{
    /// <summary>
    /// Macaron > lilMCR PCSS > Batch Settings: one place to adjust every lilMCR PCSS material of an avatar,
    /// the shadow color and the light direction. Changes are written at once (Undo).
    /// </summary>
    public class LilMCRPcssBatch : EditorWindow
    {
        private enum Kind { Color, Slider, IntSlider }

        private struct Row
        {
            public string Prop;    // property name without the prefix
            public string Label;   // zh (key of PcssText)
            public string Help;    // zh, may be null
            public Kind Kind;
        }

        private struct Section
        {
            public string Title;
            public Row[] Rows;
        }

        private const string Prefix = "_lilMCRPCSS_";

        private static readonly List<Material> propertyTargets = new List<Material>();
        private static readonly Section[] Sections =
        {
            new Section { Title = "影子", Rows = new[]
            {
                new Row { Prop = "ShadowColor", Label = "影子顏色", Help = "落影處乘上的顏色（亮度與色調）。越暗影子越深。", Kind = Kind.Color },
                new Row { Prop = "AlbedoTint", Label = "沿用底色", Help = "影子帶上材質本身的顏色（色相），不會變成灰色。0 = 只用影子顏色。", Kind = Kind.Slider },
                new Row { Prop = "ShadowSaturation", Label = "影子飽和度", Help = "影子顏色的飽和度。0 = 中性（只變暗，保留原本的顏色），1 = 沿用的顏色本身，越大越鮮豔。", Kind = Kind.Slider },
                new Row { Prop = "Strength", Label = "影子強度", Kind = Kind.Slider },
                new Row { Prop = "PenumbraTint", Label = "半影著色", Help = "影子邊緣（亮面與影子之間的過渡帶）染上半影顏色的程度。0 = 關閉。過渡帶越寬（柔和度越大、離遮擋物越遠）越明顯。", Kind = Kind.Slider },
                new Row { Prop = "PenumbraColor", Label = "半影顏色", Help = "影子邊緣的色調。只取色相，不會讓畫面變亮。皮膚常用偏紅的暖色。", Kind = Kind.Color },
                new Row { Prop = "BackShadow", Label = "背光面的影子", Help = "背對專用光源的面也加上影子。1 = 背光面也有影子，0 = 只在受光面顯示落影。", Kind = Kind.Slider },
                new Row { Prop = "LightSize", Label = "柔和度（光源大小）", Help = "越小影子越銳利、越接近遮擋物的形狀；越大離遮擋物越遠的地方越模糊。", Kind = Kind.Slider },
                new Row { Prop = "BlockerRange", Label = "遮擋物搜尋範圍", Kind = Kind.Slider },
                new Row { Prop = "Quality", Label = "品質", Kind = Kind.IntSlider },
                new Row { Prop = "MinBlur", Label = "最小模糊", Help = "貼近遮擋物處的最小模糊（陰影貼圖像素）。0 = 最銳利（邊緣仍會平滑處理）。", Kind = Kind.Slider },
            } },
            new Section { Title = "進階", Rows = new[]
            {
                new Row { Prop = "NormalBias", Label = "法線偏移", Kind = Kind.Slider },
                new Row { Prop = "DepthBias", Label = "深度偏移", Help = "基礎偏移為 0.002 m；此值為額外偏移，0 即使用基礎值。條紋出現時調高，影子分離時調低。", Kind = Kind.Slider },
                new Row { Prop = "ConeEdge", Label = "光錐邊緣柔和度", Kind = Kind.Slider },
            } },
        };

        private GameObject avatar;
        private readonly HashSet<Material> excluded = new HashSet<Material>();
        private bool showMaterials;
        private Vector2 scroll;

        // Light direction (avatar space, as Macaron's direction pad), cached: computing it walks the renderers.
        private bool hasDirection;
        private Vector3 direction = Vector3.forward;

        [MenuItem("Macaron/lilMCR PCSS/Batch Settings 一括設定", false, 41)]
        public static void Open()
        {
            LilMCRPcssBatch w = GetWindow<LilMCRPcssBatch>();
            w.titleContent = new GUIContent("lilMCR PCSS Batch");
            w.minSize = new Vector2(380f, 460f);
            w.PickSelection();
            w.Show();
        }

        public static void Open(GameObject avatar)
        {
            LilMCRPcssBatch w = GetWindow<LilMCRPcssBatch>();
            w.titleContent = new GUIContent("lilMCR PCSS Batch");
            w.minSize = new Vector2(380f, 460f);
            if(avatar != null) w.SetAvatar(avatar); else w.PickSelection();
            w.Show();
        }

        private void OnEnable() { Undo.undoRedoPerformed += OnUndo; }
        private void OnDisable() { Undo.undoRedoPerformed -= OnUndo; }

        // Closing the window turns the editor preview off (the light must not reach the Default layer when uploading).
        private void OnDestroy() { PreviewOff(avatar); }

        private static void PreviewOff(GameObject a)
        {
            if(a != null && LilMCRPcssSetup.IsEditorPreview(a)) LilMCRPcssSetup.SetEditorPreview(a, false);
        }
        // Same as Macaron's Batch Settings: drop the field being edited so every row shows the restored value.
        private void OnUndo()
        {
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            LilMCRPcssSetup.SaveCoverageAfterUndo();
            ReadDirection();
            Repaint();
        }

        private void OnSelectionChange()
        {
            if(avatar == null) { PickSelection(); Repaint(); }
        }

        private void PickSelection()
        {
            GameObject a = LilMCRPcssSetup.SelectedAvatar();
            if(a != null) SetAvatar(a);
        }

        private void SetAvatar(GameObject a)
        {
            if(avatar != null && avatar != a) PreviewOff(avatar);
            avatar = a;
            excluded.Clear();
            ReadDirection();
        }

        private void ReadDirection()
        {
            Vector3 d = Vector3.forward;
            hasDirection = avatar != null && LilMCRPcssSetup.CurrentDirection(avatar, out d);
            if(hasDirection) direction = d;
        }

        private List<Material> Targets(List<Material> all)
        {
            return all.Where(m => !excluded.Contains(m)).ToList();
        }

        //------------------------------------------------------------------------------------------

        private void OnGUI()
        {
            titleContent.text = PcssText.S("lilMCR PCSS 統一調整");
            MacaronGUI.Banner(PcssText.S("lilMCR PCSS 統一調整"), PcssText.S("一次調整角色所有 lilMCR PCSS 材質的設定，也可以調整影子顏色與光源方向。"));
            PcssText.LanguagePopup();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            // 1. Avatar and materials
            MacaronGUI.Step(1, PcssText.S("選擇角色"));
            EditorGUI.BeginChangeCheck();
            GameObject a = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true, GUILayout.Height(20f));
            if(EditorGUI.EndChangeCheck())
            {
                if(a != null && EditorUtility.IsPersistent(a)) a = null;
                if(a != null) a = LilMCRPcssSetup.FindAvatarRoot(a.transform).gameObject;
                SetAvatar(a);
            }
            if(avatar == null)
            {
                EditorGUILayout.LabelField(PcssText.S("請選擇場景中的角色。"), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndScrollView();
                return;
            }

            List<Material> all = LilMCRPcssSetup.AvatarMaterials(avatar).Where(LilMCRPcssSetup.IsPcss).ToList();
            if(all.Count == 0 || LilMCRPcssSetup.FindLight(avatar) == null)
            {
                EditorGUILayout.HelpBox(PcssText.S("這個角色還沒有套用 lilMCR PCSS。請先在 Setup 按「套用」。"), MessageType.Warning);
                if(MacaronGUI.BigButton(PcssText.S("開啟 Setup"), 28f)) LilMCRPcssWindow.Open();
                EditorGUILayout.EndScrollView();
                return;
            }

            GUILayout.Space(4f);
            LilMCRPcssSetup.PreviewButton(avatar);
            EditorGUILayout.LabelField(PcssText.S("柔和度為 0 時，只使用最小模糊，寬度不隨遮擋物距離改變；大於 0 時啟用距離相關柔影。最小模糊以陰影貼圖像素計算。"), EditorStyles.wordWrappedMiniLabel);
            DrawMaterials(all);
            List<Material> targets = Targets(all);

            GUILayout.Space(10f);

            // 2. Light direction
            MacaronGUI.Step(2, PcssText.S("光源方向"));
            DrawDirection();

            GUILayout.Space(10f);

            // 3. Macaron features that overlap with PCSS
            MacaronGUI.Step(3, PcssText.S("lilMCR 功能"));
            using(new EditorGUI.DisabledScope(targets.Count == 0))
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawToggle(targets, MacaronContactShadow, PcssText.S("接觸陰影"));
                EditorGUILayout.LabelField(PcssText.S("效能消耗非常高，且和 PCSS 的功能相近，建議關閉。"), EditorStyles.wordWrappedMiniLabel);
                DrawToggle(targets, MacaronOcclusion, PcssText.S("全體 AO（環境光遮蔽）"));
                EditorGUILayout.LabelField(PcssText.S("可依喜好開或關。"), EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(4f);
                // Macaron quality tiers: Macaron and lilMCR PCSS values of these materials and the light's shadow map resolution.
                EditorGUILayout.LabelField(PcssText.S("一鍵套用品質檔次"), EditorStyles.boldLabel);
                int tier = MacaronGUI.TierButtons();
                if(tier >= 0)
                {
                    MacaronCore.ApplyTier(targets, tier);
                    MacaronCore.ApplyTierToAvatar(avatar, tier);     // shadow map resolution of the light
                    AssetDatabase.SaveAssets();
                }
            }
            GUILayout.Space(10f);

            LilMCRPcssSharedShadow.Draw(avatar, true);

            // 4. Shadow / 5. Advanced
            using(new EditorGUI.DisabledScope(targets.Count == 0))
            {
                int step = 4;
                foreach(Section s in Sections)
                {
                    MacaronGUI.Step(step++, PcssText.S(s.Title));
                    DrawSection(s, targets);
                    GUILayout.Space(10f);
                }
            }

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button(PcssText.S("全部恢復預設值")) && targets.Count > 0
                && EditorUtility.DisplayDialog("lilMCR PCSS", PcssText.F("把 {0} 個材質的 lilMCR PCSS 設定恢復成預設值？（可用 Ctrl+Z 復原）", targets.Count), "OK", PcssText.S("取消")))
                {
                    ResetAll(targets);
                }
                if(GUILayout.Button(PcssText.S("開啟 Setup"))) LilMCRPcssWindow.Open();
            }
            EditorGUILayout.EndScrollView();
        }

        // Macaron's own switches (property names only; rows are hidden when Macaron has no such property).
        private const string MacaronContactShadow = "_MCR_ShadowEnable";
        private const string MacaronOcclusion = "_MCR_OccEnable";

        private static void DrawToggle(List<Material> targets, string prop, string label)
        {
            List<Material> ms = propertyTargets;
            ms.Clear();
            foreach(Material m in targets) if(m != null && m.HasProperty(prop)) ms.Add(m);
            if(ms.Count == 0) return;
            bool on = ms[0].GetFloat(prop) > 0.5f;
            EditorGUI.showMixedValue = ms.Any(m => (m.GetFloat(prop) > 0.5f) != on);
            EditorGUI.BeginChangeCheck();
            bool next = EditorGUILayout.ToggleLeft(label, on, EditorStyles.boldLabel);
            EditorGUI.showMixedValue = false;
            if(EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(ms.ToArray(), "lilMCR PCSS Batch");
                foreach(Material m in ms) { m.SetFloat(prop, next ? 1f : 0f); EditorUtility.SetDirty(m); }
            }
        }

        private void DrawMaterials(List<Material> all)
        {
            int on = all.Count(m => !excluded.Contains(m));
            showMaterials = EditorGUILayout.Foldout(showMaterials, PcssText.F("調整對象：{0} / {1} 個材質", on, all.Count), true);
            if(!showMaterials) return;
            EditorGUI.indentLevel++;
            foreach(Material m in all)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    bool inc = !excluded.Contains(m);
                    bool next = EditorGUILayout.ToggleLeft(m.name, inc);
                    if(next != inc) { if(next) excluded.Remove(m); else excluded.Add(m); }
                    if(GUILayout.Button("→", EditorStyles.miniButton, GUILayout.Width(24f))) EditorGUIUtility.PingObject(m);
                }
            }
            EditorGUI.indentLevel--;
        }

        private void DrawSection(Section s, List<Material> targets)
        {
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                float lw = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Min(170f, position.width * 0.42f);
                foreach(Row r in s.Rows) DrawRow(r, targets);
                EditorGUIUtility.labelWidth = lw;
            }
        }

        private static void DrawRow(Row r, List<Material> targets)
        {
            string prop = Prefix + r.Prop;
            List<Material> ms = propertyTargets;
            ms.Clear();
            foreach(Material m in targets) if(m != null && m.HasProperty(prop)) ms.Add(m);
            if(ms.Count == 0) return;
            Shader shader = ms[0].shader;
            int idx = shader.FindPropertyIndex(prop);
            GUIContent label = new GUIContent(PcssText.S(r.Label), r.Help != null ? PcssText.S(r.Help) : null);

            if(r.Kind == Kind.Color)
            {
                Color c = ms[0].GetColor(prop);
                EditorGUI.showMixedValue = ms.Any(m => m.GetColor(prop) != c);
                EditorGUI.BeginChangeCheck();
                Color n = EditorGUILayout.ColorField(label, c, true, false, false);
                EditorGUI.showMixedValue = false;
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(ms.ToArray(), "lilMCR PCSS Batch");
                    foreach(Material m in ms) { m.SetColor(prop, n); EditorUtility.SetDirty(m); }
                }
            }
            else
            {
                Vector2 lim = new Vector2(0f, 1f);
                if(idx >= 0 && shader.GetPropertyType(idx) == UnityEngine.Rendering.ShaderPropertyType.Range) lim = shader.GetPropertyRangeLimits(idx);
                float v = ms[0].GetFloat(prop);
                EditorGUI.showMixedValue = ms.Any(m => !Mathf.Approximately(m.GetFloat(prop), v));
                EditorGUI.BeginChangeCheck();
                float n = r.Kind == Kind.IntSlider
                    ? EditorGUILayout.IntSlider(label, Mathf.RoundToInt(v), Mathf.RoundToInt(lim.x), Mathf.RoundToInt(lim.y))
                    : EditorGUILayout.Slider(label, v, lim.x, lim.y);
                EditorGUI.showMixedValue = false;
                if(EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObjects(ms.ToArray(), "lilMCR PCSS Batch");
                    foreach(Material m in ms) { m.SetFloat(prop, n); EditorUtility.SetDirty(m); }
                }
            }
            if(r.Help != null) EditorGUILayout.LabelField(PcssText.S(r.Help), EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawDirection()
        {
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if(!hasDirection) ReadDirection();
                EditorGUILayout.LabelField(PcssText.S("拖動圓盤調整專用光源的方向（以角色為基準）。"), EditorStyles.wordWrappedMiniLabel);
                Vector3 d = direction;
                // "恢復預設方向" goes back to the add-on's default light direction (LilMCRPcssSetup.DefaultDirection).
                if(MacaronGUI.DirectionPad(ref d, 0, false, LilMCRPcssSetup.ConvertedDirection(avatar)))
                {
                    direction = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
                    LilMCRPcssSetup.SetDirection(avatar, direction);
                }

                GUILayout.Space(6f);
                float lw = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Min(170f, position.width * 0.42f);

                int res = LilMCRPcssSetup.GetShadowResolution(avatar);
                int[] values = LilMCRPcssSetup.ShadowResolutions;
                string[] labels = values.Select(v => v.ToString()).ToArray();
                int sel = System.Array.IndexOf(values, res);
                EditorGUI.BeginChangeCheck();
                int nsel = EditorGUILayout.Popup(PcssText.S("陰影貼圖解析度"), sel < 0 ? System.Array.IndexOf(values, LilMCRPcssSetup.DefaultShadowResolution) : sel, labels);
                if(EditorGUI.EndChangeCheck()) LilMCRPcssSetup.SetShadowResolution(avatar, values[nsel]);
                EditorGUILayout.LabelField(PcssText.S("解析度越高，影子越接近遮擋物的形狀；但看到你的每個人都要多用顯示記憶體與效能。"), EditorStyles.wordWrappedMiniLabel);

                EditorGUI.BeginChangeCheck();
                float cov = EditorGUILayout.Slider(new GUIContent(PcssText.S("照射範圍"), PcssText.S("光錐涵蓋角色的範圍。越小，同樣的解析度下影子越細緻；太小時手腳、頭頂、尾巴等會超出範圍而沒有影子。")),
                    LilMCRPcssSetup.GetCoverage(avatar), 0.5f, 2f);
                if(EditorGUI.EndChangeCheck())
                {
                    LilMCRPcssSetup.SetCoverage(avatar, cov);
                    LilMCRPcssSetup.SetDirection(avatar, direction);
                }
                EditorGUILayout.LabelField(PcssText.S("光錐涵蓋角色的範圍。越小，同樣的解析度下影子越細緻；太小時手腳、頭頂、尾巴等會超出範圍而沒有影子。"), EditorStyles.wordWrappedMiniLabel);
                using(new EditorGUILayout.HorizontalScope())
                {
                    MacaronGUI.Chip(PcssText.F("目前精度：約 {0:0.00} mm / 像素", LilMCRPcssSetup.TexelSizeMM(avatar)), MacaronGUI.Mint, 220f);
                    GUILayout.FlexibleSpace();
                }

                EditorGUIUtility.labelWidth = lw;
            }
        }

        private static void ResetAll(List<Material> targets)
        {
            Undo.RecordObjects(targets.ToArray(), "lilMCR PCSS Reset");
            foreach(Material m in targets)
            {
                Shader s = m.shader;
                foreach(Section sec in Sections)
                {
                    foreach(Row r in sec.Rows)
                    {
                        int i = s.FindPropertyIndex(Prefix + r.Prop);
                        if(i < 0) continue;
                        if(r.Kind == Kind.Color) m.SetColor(Prefix + r.Prop, (Color)s.GetPropertyDefaultVectorValue(i));
                        else m.SetFloat(Prefix + r.Prop, s.GetPropertyDefaultFloatValue(i));
                    }
                }
                EditorUtility.SetDirty(m);
            }
        }
    }
}
#endif
