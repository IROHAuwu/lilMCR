#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using lilMCR.PCSS;
using liltoon_MCR;

namespace lilToon
{
    /// <summary>
    /// Inspector of the PCSS variants ("Macaron/PCSS/..."): Macaron's own inspector (whatever its current version draws),
    /// with the PCSS section added as a child of the Macaron block (after AO / contact shadow).
    /// Rendering mode changes in lilToon's UI pick the PCSS variant of the new shader.
    /// </summary>
    public class LilMCRPcssInspector : MacaronInspector
    {
        private const string Prefix = "_lilMCRPCSS_";
        private const string MasterSwitch = "_MCR_Active";
        private const string SectionId = "pcss";      // folded by default, like the Macaron sections

        private static bool openShadow = true;
        private static bool openAdvanced;
        private static bool openMask = true;
        private const string MaskGeneratorMenu = "Macaron/Mask Generator マスク生成";   // optional lilMCR_MaskGenerator add-on

        private readonly Dictionary<string, MaterialProperty> pcss = new Dictionary<string, MaterialProperty>();

        protected override void LoadCustomProperties(MaterialProperty[] properties, Material material)
        {
            base.LoadCustomProperties(properties, material);
            pcss.Clear();
            foreach(MaterialProperty p in properties)
            {
                if(p != null && p.name.StartsWith(Prefix, System.StringComparison.Ordinal)) pcss[p.name] = p;
            }
        }

        // Drawn inside the Macaron block (MacaronInspector.DrawCustomProperties calls it), so PCSS always stays
        // under the Macaron header, also when more add-on sections are added later.
        protected override void DrawChildSections(Material material)
        {
            base.DrawChildSections(material);
            if(pcss.Count == 0) return;
            if(ChildSectionFoldout(SectionId, "PCSS")) DrawPcss(material);
        }

        protected override void ReplaceToCustomShaders()
        {
            base.ReplaceToCustomShaders();       // Macaron's variants
            RemapToPcss();                       // -> their PCSS variants (when generated)
        }

        /// <summary>Points every lilToon shader slot that holds a Macaron shader to its PCSS variant.</summary>
        public static void RemapToPcss()
        {
            foreach(FieldInfo f in typeof(lilShaderManager).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if(f.FieldType != typeof(Shader)) continue;
                Shader s = f.GetValue(null) as Shader;
                if(s == null) continue;
                string n = LilMCRPcssGenerator.ToPcssName(s.name);
                if(n == null) continue;
                Shader p = Shader.Find(n);
                if(p != null) f.SetValue(null, p);
            }
        }

        //------------------------------------------------------------------------------------------

        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "SelfOnly", "僅自身陰影" }, { "ShadowColor", "影子顏色" }, { "AlbedoTint", "沿用底色" }, { "ShadowSaturation", "影子飽和度" }, { "BackShadow", "背光面的影子" }, { "Strength", "影子強度" }, { "PenumbraTint", "半影著色" }, { "PenumbraColor", "半影顏色（亮側）" }, { "PenumbraColor2", "半影顏色（影側）" }, { "LightSize", "柔和度（光源大小）" },
            { "BlockerRange", "遮擋物搜尋範圍" }, { "Quality", "品質" }, { "MinBlur", "最小模糊" },
            { "NormalBias", "法線偏移" }, { "DepthBias", "深度偏移" }, { "ConeEdge", "光錐邊緣柔和度" },
        };

        private MaterialProperty P(string name)
        {
            MaterialProperty p;
            return pcss.TryGetValue(Prefix + name, out p) ? p : null;
        }

        private void Row(string name)
        {
            MaterialProperty p = P(name);
            if(name == "SelfOnly" && p != null)
            {
                EditorGUI.showMixedValue = p.hasMixedValue;
                EditorGUI.BeginChangeCheck();
                bool next = EditorGUILayout.Toggle(PcssText.S("僅自身陰影"), p.floatValue > 0.5f);
                bool changed = EditorGUI.EndChangeCheck();
                EditorGUI.showMixedValue = false;
                if(changed)
                {
                    var materials = new List<Material>();
                    foreach(var target in p.targets) if(target is Material) materials.Add((Material)target);
                    LilMCRPcssSharedShadow.Set(materials, next);
                }
                EditorGUILayout.LabelField(PcssText.S("角色共用：修改此項會同步所有 PCSS 材質。"), EditorStyles.wordWrappedMiniLabel);
                return;
            }
            string zh;
            if(p != null) m_MaterialEditor.ShaderProperty(p, Labels.TryGetValue(name, out zh) ? PcssText.S(zh) : p.displayName);
            if(name == "DepthBias" && p != null)
                EditorGUILayout.LabelField(PcssText.S("基礎偏移為 0.002 m；此值為額外偏移，0 即使用基礎值。條紋出現時調高，影子分離時調低。"), EditorStyles.wordWrappedMiniLabel);
        }

        // PCSS-only mask: where this add-on's shadow appears (Macaron's global mask still applies as well).
        private void DrawMask()
        {
            MaterialProperty mask = P("Mask");
            MaterialProperty amount = P("MaskAmount");
            if(mask == null) return;
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent(PcssText.S("PCSS 遮罩"), PcssText.S("白色 = 有 PCSS 影子／黑色 = 沒有 PCSS 影子（R 通道，主貼圖 UV）。")), mask, amount);
            EditorGUILayout.LabelField(PcssText.S("只影響 PCSS 影子；Macaron 的全域遮罩仍會一起套用。可用遮罩生成器的「PCSS 遮罩」生成並自動套用。"), EditorStyles.wordWrappedMiniLabel);
            if(GUILayout.Button(PcssText.S("開啟遮罩生成器")))
            {
                if(!EditorApplication.ExecuteMenuItem(MaskGeneratorMenu))
                    EditorUtility.DisplayDialog("lilMCR PCSS", PcssText.S("請匯入獨立的 lilMCR_MaskGenerator 資料夾以使用此功能。"), "OK");
            }
        }

        private void DrawPcss(Material material)
        {
            if(pcss.Count == 0) return;

            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(190f, EditorGUIUtility.currentViewWidth * 0.4f);
            try
            {
                using(new EditorGUILayout.VerticalScope(boxOuter))
                {
                    // One switch: lilMCR PCSS follows Macaron's master switch (_MCR_Active).
                    bool disabled = material.HasProperty(MasterSwitch) && material.GetFloat(MasterSwitch) <= 0.5f;
                    EditorGUILayout.LabelField("PCSS", customToggleFont);
                    EditorGUILayout.LabelField(PcssText.S(disabled ? "開關：跟隨 Macaron 總開關（目前關閉）" : "開關：跟隨 Macaron 總開關（目前開啟）"), EditorStyles.miniLabel);

                    using(new EditorGUILayout.VerticalScope(boxInner))
                    {
                        EditorGUILayout.LabelField(PcssText.S("使用 Macaron > lilMCR PCSS > Setup 為角色建立專用光源，用「Batch Settings」一次調整整個角色。"), EditorStyles.wordWrappedMiniLabel);

                        MaterialProperty id = P("LightId");
                        MaterialProperty reach = P("Reach");
                        if(id != null && reach != null && !id.hasMixedValue)
                        {
                            EditorGUILayout.LabelField(id.floatValue >= 0.5f
                                ? PcssText.F("專用光源 ID：{0}　範圍：{1:0.00} m", Mathf.RoundToInt(id.floatValue), reach.floatValue)
                                : PcssText.S("這個材質還沒有專用光源（請執行 Setup）。"), EditorStyles.miniLabel);
                        }

                        using(new EditorGUI.DisabledScope(disabled))
                        {
                            openShadow = MacaronGUI.InsetFoldout(openShadow, PcssText.S("影子"));
                            if(openShadow)
                            {
                                EditorGUI.indentLevel++;
                                Row("SelfOnly");
                                Row("Strength");
                                Row("ShadowColor");
                                Row("AlbedoTint");
                                Row("ShadowSaturation");
                                Row("PenumbraTint");
                                Row("PenumbraColor");
                                Row("PenumbraColor2");
                                Row("BackShadow");
                                Row("LightSize");
                                EditorGUILayout.LabelField(PcssText.S("柔和度為 0 時，只使用最小模糊，寬度不隨遮擋物距離改變；大於 0 時啟用距離相關柔影。最小模糊以陰影貼圖像素計算。"), EditorStyles.wordWrappedMiniLabel);
                                Row("BlockerRange");
                                Row("Quality");
                                Row("MinBlur");
                                EditorGUI.indentLevel--;
                            }

                            openMask = MacaronGUI.InsetFoldout(openMask, PcssText.S("遮罩"));
                            if(openMask)
                            {
                                EditorGUI.indentLevel++;
                                DrawMask();
                                EditorGUI.indentLevel--;
                            }

                            openAdvanced = MacaronGUI.InsetFoldout(openAdvanced, PcssText.S("進階"));
                            if(openAdvanced)
                            {
                                EditorGUI.indentLevel++;
                                Row("NormalBias");
                                Row("DepthBias");
                                Row("ConeEdge");
                                EditorGUI.indentLevel--;
                            }
                        }
                    }
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }
    }
}
#endif
