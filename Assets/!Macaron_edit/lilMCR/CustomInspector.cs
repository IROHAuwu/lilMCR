#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using liltoon_MCR;

namespace lilToon
{
    // Macaron material Inspector, drawn after lilToon's own settings.
    // The rows are generated from MacaronCore.Sections (shared with the Batch Settings window), so this class only
    // knows how to draw a row kind, not which properties exist.
    public class MacaronInspector : lilToonInspector
    {
        private const string shaderName = MacaronCore.ShaderRoot;

        // Foldout state per section id ("main" = the whole Macaron block), kept for the editor session.
        // Foldouts that differ from their default (the Macaron block starts open, every section starts folded).
        private static readonly HashSet<string> toggledSections = new HashSet<string>();
        private static readonly HashSet<string> openAdvanced = new HashSet<string>();

        // Snapshot of the material copied with "Copy settings" (per-avatar data is skipped when pasting).
        private static Material copiedSettings;

        private readonly Dictionary<string, MaterialProperty> props = new Dictionary<string, MaterialProperty>();
        private Material activeMaterial;

        //------------------------------------------------------------------------------------------
        // lilToon hooks

        protected override void LoadCustomProperties(MaterialProperty[] properties, Material material)
        {
            isCustomShader = true;
            ReplaceToCustomShaders();
            isShowRenderMode = !material.shader.name.Contains("Optional");

            props.Clear();
            foreach(MaterialProperty p in properties.Where(x => x != null && x.name.StartsWith(MacaronCore.PropPrefix)))
            {
                props[p.name] = p;
            }
        }

        protected override void DrawCustomProperties(Material material)
        {
            activeMaterial = material;
            // Derived state (capture key -> capture pass), also fixes materials made before this setting existed.
            if(MacaronCore.SyncCapturePass(material)) EditorUtility.SetDirty(material);

            if(!SectionFoldout("main", "Macaron", true, true)) return;

            ML.LanguagePopup();
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(190f, EditorGUIUtility.currentViewWidth * 0.4f);
            try
            {
                foreach(MacaronSection section in MacaronCore.Sections) DrawSection(section);
                // Sections of add-ons (lilMCR PCSS ...) stay inside the Macaron block, below Macaron's own sections.
                DrawChildSections(material);
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }

        /// <summary>
        /// Extra child sections drawn inside the Macaron block, after the built-in ones. Inspectors that extend Macaron
        /// (lilMCR PCSS) override this instead of DrawCustomProperties, so their sections stay under the Macaron header.
        /// Draw each one with <see cref="ChildSectionFoldout"/> followed by a lilEditorGUI.boxOuter box.
        /// </summary>
        protected virtual void DrawChildSections(Material material)
        {
        }

        //------------------------------------------------------------------------------------------
        // Layout helpers

        private MaterialProperty Find(string name)
        {
            MaterialProperty p;
            return name != null && props.TryGetValue(name, out p) ? p : null;
        }

        private float ValueOf(string name)
        {
            MaterialProperty p = Find(name);
            return p != null ? p.floatValue : 0f;
        }

        // Foldout with an open state remembered per id. The main block also gets the menu button on its right.
        private bool SectionFoldout(string id, string title, bool withMenu, bool openByDefault = false)
        {
            bool open = lilEditorGUI.Foldout(title, toggledSections.Contains(id) != openByDefault);
            if(withMenu) MenuButton(GUILayoutUtility.GetLastRect());
            return RememberOpen(id, open, openByDefault);
        }

        /// <summary>Child section header inside the Macaron block (smaller and narrower than the Macaron header).
        /// Starts folded; the open state is remembered per id for the editor session.</summary>
        protected bool ChildSectionFoldout(string id, string title)
        {
            bool open = MacaronGUI.ChildFoldout(title, toggledSections.Contains(id));
            return RememberOpen(id, open, false);
        }

        private static bool RememberOpen(string id, bool open, bool openByDefault)
        {
            if(open != openByDefault) toggledSections.Add(id);
            else toggledSections.Remove(id);
            return open;
        }

        // Toggle bound to a float property (0 / 1), mixed-value aware.
        private static void ToggleProperty(MaterialProperty p, Func<bool, bool> draw)
        {
            EditorGUI.showMixedValue = p.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            bool value = draw(p.floatValue > 0.5f);
            if(EditorGUI.EndChangeCheck()) p.floatValue = value ? 1f : 0f;
            EditorGUI.showMixedValue = false;
        }

        //------------------------------------------------------------------------------------------
        // Sections

        private void DrawSection(MacaronSection section)
        {
            string title = section.DisplayTitle;
            // The child header and the box under it both use lilToon's child box margins (boxOuter).
            if(ChildSectionFoldout(section.Id, title)) DrawSectionBody(section, title);
        }

        private void DrawSectionBody(MacaronSection section, string title)
        {
            MaterialProperty enable = Find(section.EnableProp);
            bool disabled = enable != null && !enable.hasMixedValue && enable.floatValue <= 0.5f;

            using(new EditorGUILayout.VerticalScope(boxOuter))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(enable != null) ToggleProperty(enable, on => EditorGUILayout.ToggleLeft(title, on, customToggleFont));
                    else EditorGUILayout.LabelField(title, customToggleFont);

                    GUIContent reset = new GUIContent(ML.T("重置"), ML.T("把這個區塊恢復成預設值"));
                    if(GUILayout.Button(reset, EditorStyles.miniButton, GUILayout.Width(64f))) ResetSection(section);
                }

                using(new EditorGUILayout.VerticalScope(boxInner))
                using(new EditorGUI.DisabledScope(disabled))
                {
                    if(section.Help != null) EditorGUILayout.LabelField(ML.T(section.Help), EditorStyles.wordWrappedMiniLabel);

                    bool hasAdvanced = false;
                    foreach(MacaronProp prop in section.Props)
                    {
                        if(prop.Advanced) hasAdvanced = true;
                        else DrawRow(prop);
                    }

                    if(hasAdvanced)
                    {
                        bool showAdvanced = MacaronGUI.InsetFoldout(openAdvanced.Contains(section.Id), ML.T("進階設定"));
                        if(showAdvanced) openAdvanced.Add(section.Id);
                        else openAdvanced.Remove(section.Id);
                        if(showAdvanced)
                        {
                            EditorGUI.indentLevel++;
                            foreach(MacaronProp prop in section.Props) if(prop.Advanced) DrawRow(prop);
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
        }

        private void DrawRow(MacaronProp prop)
        {
            if(prop.InlineExtra || !prop.IsVisible(ValueOf)) return;
            MaterialProperty p = Find(prop.Name);
            if(p == null) return;

            if(prop.SubHeader != null)
            {
                GUILayout.Space(2f);
                EditorGUILayout.LabelField(prop.Text(prop.SubHeader), EditorStyles.miniBoldLabel);
            }

            if(prop.Kind == MacaronPropKind.Toggle) ToggleProperty(p, on => EditorGUILayout.Toggle(prop.Content, on));
            else if(prop.Kind == MacaronPropKind.Popup) DrawChoice(p, prop, ValueOf);
            else if(prop.Kind == MacaronPropKind.Texture) DrawTexture(p, prop);
            else if(prop.Kind == MacaronPropKind.Direction) DrawDirection(p, prop);
            else if(prop.Kind == MacaronPropKind.Range2) DrawPair(p, prop);
            else m_MaterialEditor.ShaderProperty(p, prop.Content);
        }

        private void DrawTexture(MaterialProperty p, MacaronProp prop)
        {
            MaterialProperty extra = Find(prop.TextureExtra);
            if(extra == null) m_MaterialEditor.TexturePropertySingleLine(prop.Content, p);
            else m_MaterialEditor.TexturePropertySingleLine(prop.Content, p, extra);
            if(!prop.ScaleOffset || p.textureValue == null) return;
            EditorGUI.indentLevel++;
            m_MaterialEditor.TextureScaleOffsetProperty(p);
            EditorGUI.indentLevel--;
        }

        // Popup over a list of float values. A stored value that is not in the list shows as mixed and is kept
        // until the user picks an entry.
        private static void DrawChoice(MaterialProperty p, MacaronProp prop, Func<string, float> get)
        {
            float[] values;
            GUIContent[] labels = prop.Choices(get, out values);
            float shown = prop.ShownValue(p.floatValue, get);
            int current = Array.FindIndex(values, x => Mathf.Approximately(x, shown));
            EditorGUI.showMixedValue = p.hasMixedValue || current < 0;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(prop.Content, Mathf.Max(current, 0), labels);
            if(EditorGUI.EndChangeCheck())
            {
                float value = values[Mathf.Clamp(picked, 0, values.Length - 1)];
                if(prop.Name == MacaronCore.PropOccMethod)
                {
                    // Each method keeps its own shared settings (first switch to SSAO: SSAO starting values).
                    Undo.RecordObjects(p.targets, "Macaron Occlusion Method");
                    foreach(UnityEngine.Object o in p.targets)
                    {
                        Material m = o as Material;
                        if(m == null) continue;
                        MacaronCore.SwitchOccMethod(m, value);
                        EditorUtility.SetDirty(m);
                    }
                    EditorGUI.showMixedValue = false;
                    GUIUtility.ExitGUI();      // the other rows hold the old values for this frame
                }
                p.floatValue = value;
            }
            EditorGUI.showMixedValue = false;
        }

        // Vector property holding two values (.x / .y): a heading and one slider per value.
        private static void DrawPair(MaterialProperty p, MacaronProp prop)
        {
            EditorGUILayout.LabelField(prop.Content);
            EditorGUI.indentLevel++;
            Vector4 v = p.vectorValue;
            EditorGUI.showMixedValue = p.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.Slider(ML.T(prop.PairLabels[0]), v.x, prop.PairMin, prop.PairMax);
            float y = EditorGUILayout.Slider(ML.T(prop.PairLabels[1]), v.y, prop.PairMin, prop.PairMax);
            if(EditorGUI.EndChangeCheck()) p.vectorValue = new Vector4(x, y, v.z, v.w);
            EditorGUI.showMixedValue = false;
            EditorGUI.indentLevel--;
        }

        private void DrawDirection(MaterialProperty p, MacaronProp prop)
        {
            int space = Mathf.RoundToInt(ValueOf(prop.SpaceProp ?? MacaronCore.PropShadowSpace));
            EditorGUILayout.LabelField(new GUIContent(ML.T("光源方向"), ML.T("點擊或拖曳圓盤設定光從哪裡照過來，影子會落在相反方向。")));
            Vector3 dir = p.vectorValue;
            Vector3 reset = MacaronCore.DefaultDirectionFor(activeMaterial, space);
            if(MacaronGUI.DirectionPad(ref dir, space, p.hasMixedValue, reset)) p.vectorValue = new Vector4(dir.x, dir.y, dir.z, 0f);
            if(space == 0)
            {
                EditorGUILayout.LabelField(ML.T("角色的正面方向由轉換工具自動寫入。改變了角色或骨架的方向時，請從右上角選單執行「重新偵測角色方向」。"), EditorStyles.wordWrappedMiniLabel);
            }
        }

        //------------------------------------------------------------------------------------------
        // Editing (all selected materials, with Undo)

        private Material[] Targets()
        {
            Material[] selected = m_MaterialEditor != null && m_MaterialEditor.targets != null
                ? m_MaterialEditor.targets.OfType<Material>().ToArray()
                : new Material[0];
            if(selected.Length == 0 && activeMaterial != null) selected = new[] { activeMaterial };
            return selected;
        }

        private void EditTargets(string undoName, Action<Material> edit)
        {
            Material[] targets = Targets();
            if(targets.Length == 0) return;
            Undo.RecordObjects(targets, undoName);
            foreach(Material m in targets)
            {
                edit(m);
                EditorUtility.SetDirty(m);
            }
        }

        private void ResetSection(MacaronSection section)
        {
            EditTargets("Reset Macaron Section", m =>
            {
                if(section.EnableProp != null) MacaronCore.ResetPropertyToDefault(m, section.EnableProp);
                foreach(MacaronProp prop in section.Props) MacaronCore.ResetPropertyToDefault(m, prop.Name);
            });
        }

        // "⋮" options button placed inside the right end of the Macaron foldout header. The icon is looked up once;
        // the menu is only built when the button is pressed and drops down under the button.
        private static GUIContent menuIcon;
        private static GUIStyle menuButtonStyle;

        private static GUIContent MenuIcon
        {
            get
            {
                if(menuIcon == null)
                {
                    GUIContent builtin = EditorGUIUtility.IconContent("_Menu");
                    menuIcon = builtin != null && builtin.image != null ? new GUIContent(builtin.image) : new GUIContent("⋮");
                    menuIcon.tooltip = "Macaron";
                }
                return menuIcon;
            }
        }

        private void MenuButton(Rect header)
        {
            if(Event.current.type == EventType.Layout || header.height < 1f) return;
            float size = Mathf.Min(header.height, EditorGUIUtility.singleLineHeight + 2f);
            Rect button = new Rect(header.xMax - size - 2f, header.center.y - size * 0.5f, size, size);
            if(menuButtonStyle == null) menuButtonStyle = GUI.skin.FindStyle("IconButton") ?? EditorStyles.label;
            if(!EditorGUI.DropdownButton(button, MenuIcon, FocusType.Passive, menuButtonStyle)) return;
            BuildMenu().DropDown(button);
        }

        private GenericMenu BuildMenu()
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent(ML.T("全部重置為預設值")), false, () => EditTargets("Reset Macaron", m => MacaronCore.ResetToDefaults(m, false)));
            menu.AddItem(new GUIContent(ML.T("全部重置為預設值（連遮罩貼圖一起）")), false, () => EditTargets("Reset Macaron", m => MacaronCore.ResetToDefaults(m, true)));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(ML.T("複製設定")), false, CopySettings);
            GUIContent paste = new GUIContent(ML.T("貼上設定"));
            if(copiedSettings != null) menu.AddItem(paste, false, PasteSettings);
            else menu.AddDisabledItem(paste);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(ML.T("重新偵測角色方向（從場景）")), false, DetectAvatarAxis);
            menu.AddItem(new GUIContent(ML.T("開啟批次設定視窗")), false, MacaronBatchEditor.Open);
            menu.AddSeparator(string.Empty);
            ML.AddLanguageMenu(menu, "Language/");
            return menu;
        }

        private void CopySettings()
        {
            if(activeMaterial == null) return;
            if(copiedSettings != null) UnityEngine.Object.DestroyImmediate(copiedSettings);
            copiedSettings = new Material(activeMaterial) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void PasteSettings()
        {
            Material source = copiedSettings;
            if(source == null || source.shader == null) return;
            string[] names = MacaronCore.McrPropertyIndices(source.shader)
                .Select(i => source.shader.GetPropertyName(i))
                .Where(n => !MacaronCore.IsAvatarDataProp(n))      // the avatar axis belongs to each renderer
                .Where(n => !MacaronCore.IsPerMeshProp(n))         // Uzumore Layer Offset belongs to each mesh
                .ToArray();
            EditTargets("Paste Macaron Settings", m =>
            {
                foreach(string n in names) MacaronCore.CopyProperty(source, m, n);
            });
        }

        private void DetectAvatarAxis()
        {
            Material[] targets = Targets();
            if(targets.Length == 0) return;
            Undo.RecordObjects(targets, "Refresh Macaron Avatar Axis");
            if(MacaronCore.WriteAvatarBasisFromScene(new HashSet<Material>(targets)) == 0)
            {
                EditorUtility.DisplayDialog("Macaron", ML.T("在目前開啟的場景中找不到使用這個材質的角色。\n請先把角色放進場景再執行。"), "OK");
            }
        }

        //------------------------------------------------------------------------------------------
        // lilToon custom shader template: the shader variants of this custom shader.

        protected override void ReplaceToCustomShaders()
        {
            lts         = Shader.Find(shaderName + "/lilToon");
            ltsc        = Shader.Find("Hidden/" + shaderName + "/Cutout");
            ltst        = Shader.Find("Hidden/" + shaderName + "/Transparent");
            ltsot       = Shader.Find("Hidden/" + shaderName + "/OnePassTransparent");
            ltstt       = Shader.Find("Hidden/" + shaderName + "/TwoPassTransparent");
            ltso        = Shader.Find("Hidden/" + shaderName + "/OpaqueOutline");
            ltsco       = Shader.Find("Hidden/" + shaderName + "/CutoutOutline");
            ltsto       = Shader.Find("Hidden/" + shaderName + "/TransparentOutline");
            ltsoto      = Shader.Find("Hidden/" + shaderName + "/OnePassTransparentOutline");
            ltstto      = Shader.Find("Hidden/" + shaderName + "/TwoPassTransparentOutline");
            ltsoo       = Shader.Find(shaderName + "/[Optional] OutlineOnly/Opaque");
            ltscoo      = Shader.Find(shaderName + "/[Optional] OutlineOnly/Cutout");
            ltstoo      = Shader.Find(shaderName + "/[Optional] OutlineOnly/Transparent");
            ltstess     = Shader.Find("Hidden/" + shaderName + "/Tessellation/Opaque");
            ltstessc    = Shader.Find("Hidden/" + shaderName + "/Tessellation/Cutout");
            ltstesst    = Shader.Find("Hidden/" + shaderName + "/Tessellation/Transparent");
            ltstessot   = Shader.Find("Hidden/" + shaderName + "/Tessellation/OnePassTransparent");
            ltstesstt   = Shader.Find("Hidden/" + shaderName + "/Tessellation/TwoPassTransparent");
            ltstesso    = Shader.Find("Hidden/" + shaderName + "/Tessellation/OpaqueOutline");
            ltstessco   = Shader.Find("Hidden/" + shaderName + "/Tessellation/CutoutOutline");
            ltstessto   = Shader.Find("Hidden/" + shaderName + "/Tessellation/TransparentOutline");
            ltstessoto  = Shader.Find("Hidden/" + shaderName + "/Tessellation/OnePassTransparentOutline");
            ltstesstto  = Shader.Find("Hidden/" + shaderName + "/Tessellation/TwoPassTransparentOutline");
            ltsl        = Shader.Find(shaderName + "/lilToonLite");
            ltslc       = Shader.Find("Hidden/" + shaderName + "/Lite/Cutout");
            ltslt       = Shader.Find("Hidden/" + shaderName + "/Lite/Transparent");
            ltslot      = Shader.Find("Hidden/" + shaderName + "/Lite/OnePassTransparent");
            ltsltt      = Shader.Find("Hidden/" + shaderName + "/Lite/TwoPassTransparent");
            ltslo       = Shader.Find("Hidden/" + shaderName + "/Lite/OpaqueOutline");
            ltslco      = Shader.Find("Hidden/" + shaderName + "/Lite/CutoutOutline");
            ltslto      = Shader.Find("Hidden/" + shaderName + "/Lite/TransparentOutline");
            ltsloto     = Shader.Find("Hidden/" + shaderName + "/Lite/OnePassTransparentOutline");
            ltsltto     = Shader.Find("Hidden/" + shaderName + "/Lite/TwoPassTransparentOutline");
            ltsref      = Shader.Find("Hidden/" + shaderName + "/Refraction");
            ltsrefb     = Shader.Find("Hidden/" + shaderName + "/RefractionBlur");
            ltsfur      = Shader.Find("Hidden/" + shaderName + "/Fur");
            ltsfurc     = Shader.Find("Hidden/" + shaderName + "/FurCutout");
            ltsfurtwo   = Shader.Find("Hidden/" + shaderName + "/FurTwoPass");
            ltsfuro     = Shader.Find(shaderName + "/[Optional] FurOnly/Transparent");
            ltsfuroc    = Shader.Find(shaderName + "/[Optional] FurOnly/Cutout");
            ltsfurotwo  = Shader.Find(shaderName + "/[Optional] FurOnly/TwoPass");
            ltsgem      = Shader.Find("Hidden/" + shaderName + "/Gem");
            ltsfs       = Shader.Find(shaderName + "/[Optional] FakeShadow");
            ltsover     = Shader.Find(shaderName + "/[Optional] Overlay");
            ltsoover    = Shader.Find(shaderName + "/[Optional] OverlayOnePass");
            ltslover    = Shader.Find(shaderName + "/[Optional] LiteOverlay");
            ltsloover   = Shader.Find(shaderName + "/[Optional] LiteOverlayOnePass");
            ltsm        = Shader.Find(shaderName + "/lilToonMulti");
            ltsmo       = Shader.Find("Hidden/" + shaderName + "/MultiOutline");
            ltsmref     = Shader.Find("Hidden/" + shaderName + "/MultiRefraction");
            ltsmfur     = Shader.Find("Hidden/" + shaderName + "/MultiFur");
            ltsmgem     = Shader.Find("Hidden/" + shaderName + "/MultiGem");
        }
    }
}
#endif
