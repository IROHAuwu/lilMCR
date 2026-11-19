#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace liltoon_MCR
{
    public static class MacaronWorldShadowReceivers
    {
        const int Bit = 128;
        static readonly string[] Groups = { "_Stencil", "_OutlineStencil" };

        const string CopySuffix = "_WSR";
        public const string MaterialFolderName = "WorldShadowMaterials";

        /// <summary>The fixed folder of an avatar's receiver materials, created when missing.</summary>
        public static string MaterialFolder(string avatarFolder)
        {
            string folder = avatarFolder.Replace('\\', '/').TrimEnd('/') + "/" + MaterialFolderName;
            MacaronCore.EnsureFolder(folder);
            return folder;
        }

        // Receiver copies of the avatar's materials.
        //   - Materials that do not test the stencil (Always / Disabled, Keep / Replace) write bit 128: the projection
        //     skips every pixel that carries it. A material that already writes a value keeps it in the lower bits.
        //   - Materials that test the stencil (Equal / NotEqual...: eyebrows through hair, FakeShadow) cannot write the
        //     bit and are reported. Their test must not see it either: the writers they depend on now store value | 128,
        //     so the copy reads and writes the lower 7 bits only (ReadMask / WriteMask & 127). Without that every such
        //     effect of the avatar stopped working once World Shadow was created.
        //   - A copy made by an earlier run that only this avatar uses is updated in place (with Undo): Create / Update
        //     no longer leaves one more set of materials behind each time, and material-swap animations that point
        //     at the copies keep the current capture key.
        //   - Every copy is kept in "folder" (the avatar's fixed WorldShadowMaterials folder). A copy an earlier
        //     version left in a per-generation "WorldShadow N" folder is moved there (same GUID, references stay).
        public static string Apply(GameObject avatar, string folder, Vector4? capture = null)
        {
            var copies = new Dictionary<Material, Material>();
            var skipped = new HashSet<string>();
            Transform module = avatar.transform.Find(MacaronWorldShadow.ObjectName);
            HashSet<Material> shared = null;    // materials also used outside this avatar (built on first need)
            Dictionary<string, Material> stored = null;     // copies already in the folder, by their source
            HashSet<Material> worn = null;                  // materials on this avatar's renderers right now
            // An avatar that already has its receiver folder keeps it (a renamed avatar, Create / Update and Repair
            // would otherwise point at different folders and move the copies back and forth).
            folder = HomeFolder(avatar, module, folder);
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(module != null && r.transform.IsChildOf(module)) continue;
                var materials = r.sharedMaterials; bool changed = false;
                for(int i=0;i<materials.Length;i++)
                {
                    Material source=materials[i]; if(source == null) continue;
                    bool canMark = CanMark(source);
                    bool keyed = capture.HasValue && source.HasProperty("_MCR_WorldCapture");
                    if(!canMark) skipped.Add(source.name);
                    bool own = false;
                    if(IsOwnCopy(source))
                    {
                        if(shared == null) shared = UsedOutside(avatar);
                        own = !shared.Contains(source);
                    }
                    // Nothing to change on this material.
                    if(canMark ? IsMarked(source) && !capture.HasValue : !keyed && !ReadsMarkBit(source))
                    {
                        if(own) Relocate(source, folder);
                        continue;
                    }
                    Material copy;
                    if(!copies.TryGetValue(source,out copy))
                    {
                        string sourceKey = own ? null : SourceKey(source);
                        bool refresh = false;
                        if(own) copy = source;
                        else if(sourceKey != null)
                        {
                            // The copy an earlier run made of this material is still in the folder: it is used
                            // again, not doubled as "X_WSR 1".
                            if(stored == null) stored = StoredCopies(folder);
                            Material found;
                            if(stored.TryGetValue(sourceKey, out found) && found != null)
                            {
                                if(shared == null) shared = UsedOutside(avatar);
                                if(!shared.Contains(found))
                                {
                                    copy = found;
                                    // Still worn by the avatar (a part added later was given the source instead of
                                    // the copy): the copy is the one in use and holds every adjustment made since
                                    // (Batch Settings, quality tier, masks, the capture key), so it is taken as it
                                    // is. Not worn (the renderers went back to the source: Undo after an earlier
                                    // Create): it is brought up to date from the source.
                                    if(worn == null) worn = Worn(avatar, module);
                                    refresh = !worn.Contains(found);
                                }
                            }
                        }
                        bool inPlace = copy != null;
                        if(inPlace) Undo.RecordObject(copy, "Macaron World Shadow Receiver");
                        else copy = new Material(source) { name = CopyName(source.name) };
                        if(refresh)
                        {
                            if(copy.shader != source.shader) copy.shader = source.shader;
                            copy.CopyPropertiesFromMaterial(source);
                        }
                        // Marks the material as a receiver copy (its name may end in " 1" when the file name was
                        // taken) and remembers which material it was made from.
                        copy.SetOverrideTag(CopyTag, "1");
                        if(sourceKey != null) copy.SetOverrideTag(SourceTag, sourceKey);
                        if(canMark) Mark(copy);
                        else Unmask(copy);
                        if(keyed) copy.SetVector("_MCR_WorldCapture", capture.Value);
                        // Capture key set -> capture pass on; copies without a key keep it off.
                        MacaronCore.SyncCapturePass(copy);
                        if(inPlace)
                        {
                            EditorUtility.SetDirty(copy);
                            Relocate(copy, folder);
                        }
                        else
                        {
                            string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+MacaronCore.SafeFileName(copy.name)+".mat");
                            AssetDatabase.CreateAsset(copy,path);
                        }
                        RecordOrigin(copy, source);
                        copies.Add(source,copy);
                    }
                    if(materials[i] != copy) { materials[i]=copy; changed=true; }
                }
                if(changed)
                {
                    Undo.RecordObject(r,"Macaron Exclude Avatar Receiver"); r.sharedMaterials=materials;
                    EditorUtility.SetDirty(r);
                    if(PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                }
            }
            return skipped.Count == 0 ? ML.T(" 已排除角色接收世界落影。") :
                ML.F(" 以下材質未能自動排除（不支援或已有自訂 Stencil）：{0}。這些部位仍可能收到投影；動畫切換材質亦須使用已排除版本。", string.Join(", ",skipped));
        }

        const string CopyTag = "MCR_WSR";               // override tag "1" on every receiver copy
        const string SourceTag = "MCR_WSRSource";       // override tag: "guid:localId" of the material it was made from
        // "_WSR", or "_WSR 1" (Unity's numbering when the file name was taken), at the end of a name.
        static readonly System.Text.RegularExpressions.Regex CopySuffixPattern = new System.Text.RegularExpressions.Regex(@"_WSR( \d+)?$");

        static string CopyName(string name)
        {
            const string legacySuffix = " (World Shadow Receiver Excluded)";
            while(true)
            {
                if(name.EndsWith(legacySuffix, System.StringComparison.Ordinal)) { name = name.Substring(0, name.Length - legacySuffix.Length); continue; }
                var match = CopySuffixPattern.Match(name);
                if(!match.Success) break;
                name = name.Substring(0, match.Index);
            }
            return name + CopySuffix;
        }

        // "guid:localId" of a material asset, null for a material that is not an asset.
        static string SourceKey(Material m)
        {
            string guid; long id;
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m, out guid, out id) && !string.IsNullOrEmpty(guid) ? guid + ":" + id : null;
        }

        // Receiver copies stored in the folder, by the material they were made from.
        static Dictionary<string, Material> StoredCopies(string folder)
        {
            var result = new Dictionary<string, Material>();
            if(!AssetDatabase.IsValidFolder(folder)) return result;
            foreach(string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(m == null) continue;
                string key = m.GetTag(SourceTag, false, "");
                if(key.Length > 0 && !result.ContainsKey(key)) result.Add(key, m);
            }
            return result;
        }

        // The receiver folder the avatar already uses (where one of its own copies is), else the given one.
        static string HomeFolder(GameObject avatar, Transform module, string folder)
        {
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(module != null && r.transform.IsChildOf(module)) continue;
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || !IsOwnCopy(m)) continue;
                    string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(m)).Replace('\\','/');
                    if(dir != folder && System.IO.Path.GetFileName(dir) == MaterialFolderName) return dir;
                }
            }
            return folder;
        }

        /// <summary>True for a World Shadow receiver copy made by this tool.</summary>
        public static bool IsReceiverCopy(Material m) { return m != null && IsOwnCopy(m); }

        // What "Restore the original materials" of the Converter puts back for this copy: the converter's record of
        // the source when it has one (a converted material: straight back to the avatar's original), else the source
        // itself (FakeShadow, Overlay, FurOnly and other materials that were never converted). Without a record such
        // a copy stayed on the avatar after Restore: deleting the generated folder then left the part without a
        // material, and a copy that writes value | 128 broke the stencil effects of the restored materials.
        static void RecordOrigin(Material copy, Material source)
        {
            if(copy == source) return;
            string path = AssetDatabase.GetAssetPath(copy);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path);
            if(importer == null) return;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            var origin = string.IsNullOrEmpty(sourcePath) ? null : AssetImporter.GetAtPath(sourcePath);
            string data = origin != null ? origin.userData : null;
            if(string.IsNullOrEmpty(data) || !data.StartsWith(MacaronConverter.SourceTag, System.StringComparison.Ordinal))
            {
                string key = SourceKey(source);
                if(key == null) return;
                data = MacaronConverter.SourceTag + key;
            }
            if(importer.userData == data) return;
            importer.userData = data;
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        // Moves a copy into the avatar's receiver folder (same GUID: every reference stays).
        static void Relocate(Material m, string folder)
        {
            string current = AssetDatabase.GetAssetPath(m);
            if(string.IsNullOrEmpty(current) || !AssetDatabase.IsMainAsset(m)) return;
            if(System.IO.Path.GetDirectoryName(current).Replace('\\','/') == folder) return;
            string error = AssetDatabase.MoveAsset(current, AssetDatabase.GenerateUniqueAssetPath(folder+"/"+System.IO.Path.GetFileName(current)));
            if(!string.IsNullOrEmpty(error)) Debug.LogWarning("[Macaron World Shadow] " + error);
        }

        // A material asset this tool created earlier.
        static bool IsOwnCopy(Material m)
        {
            if(!AssetDatabase.Contains(m)) return false;
            return m.GetTag(CopyTag, false, "") == "1" || CopySuffixPattern.IsMatch(m.name);
        }

        // Materials on the avatar's own renderers (not the World Shadow module's).
        static HashSet<Material> Worn(GameObject avatar, Transform module)
        {
            var worn = new HashSet<Material>();
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(module != null && r.transform.IsChildOf(module)) continue;
                foreach(Material m in r.sharedMaterials) if(m != null) worn.Add(m);
            }
            return worn;
        }

        // Materials of the loaded scenes that a renderer outside the avatar uses (a duplicate of the avatar, another
        // avatar sharing the copies): those get their own copy instead of an in-place edit.
        static HashSet<Material> UsedOutside(GameObject avatar)
        {
            var used = new HashSet<Material>();
            foreach(Renderer r in Object.FindObjectsOfType<Renderer>(true))
            {
                if(r.transform.IsChildOf(avatar.transform)) continue;
                foreach(Material m in r.sharedMaterials) if(m != null) used.Add(m);
            }
            return used;
        }

        // Writes bit 128, keeping a value the material already writes (Always + Replace) in the lower bits.
        static void Mark(Material m)
        {
            foreach(string group in Groups)
            {
                if(!m.HasProperty(group+"Ref")) continue;
                bool writes = m.GetInt(group+"Comp") == (int)CompareFunction.Always && m.GetInt(group+"Pass") == (int)StencilOp.Replace;
                m.SetInt(group+"Ref", writes ? m.GetInt(group+"Ref") | Bit : Bit);
                m.SetInt(group+"WriteMask", writes ? m.GetInt(group+"WriteMask") | Bit : Bit);
                m.SetInt(group+"Comp", (int)CompareFunction.Always);
                m.SetInt(group+"Pass", (int)StencilOp.Replace);
            }
        }

        // A group that tests the stencil against a value below 128.
        static bool GroupReads(Material m,string g)
        {
            if(!m.HasProperty(g+"Ref") || !m.HasProperty(g+"Comp") || !m.HasProperty(g+"ReadMask")) return false;
            int comp = m.GetInt(g+"Comp");
            return comp != (int)CompareFunction.Always && comp != (int)CompareFunction.Disabled && (m.GetInt(g+"Ref") & Bit) == 0;
        }

        // True when a stencil test of the material still sees bit 128 (or could clear it).
        static bool ReadsMarkBit(Material m)
        {
            foreach(string g in Groups)
            {
                if(!GroupReads(m,g)) continue;
                if((m.GetInt(g+"ReadMask") & Bit) != 0) return true;
                if(m.HasProperty(g+"WriteMask") && (m.GetInt(g+"WriteMask") & Bit) != 0) return true;
            }
            return false;
        }

        // Stencil tests ignore bit 128 and leave it as the materials underneath wrote it.
        static void Unmask(Material m)
        {
            foreach(string g in Groups)
            {
                if(!GroupReads(m,g)) continue;
                m.SetInt(g+"ReadMask", m.GetInt(g+"ReadMask") & ~Bit & 255);
                if(m.HasProperty(g+"WriteMask")) m.SetInt(g+"WriteMask", m.GetInt(g+"WriteMask") & ~Bit & 255);
            }
        }

        static bool GroupMarked(Material m,string g)
        {
            return (m.GetInt(g+"Ref") & Bit)!=0 && (m.GetInt(g+"WriteMask") & Bit)!=0 &&
                m.GetInt(g+"Comp")== (int)CompareFunction.Always && m.GetInt(g+"Pass")== (int)StencilOp.Replace &&
                m.GetInt(g+"Fail")== (int)StencilOp.Keep && m.GetInt(g+"ZFail")== (int)StencilOp.Keep;
        }
        static bool IsMarked(Material m)
        {
            foreach(string g in Groups) if(m.HasProperty(g+"Ref") && !GroupMarked(m,g)) return false;
            return true;
        }
        static bool CanMark(Material m)
        {
            if(!m.HasProperty("_StencilRef")) return false;
            foreach(string g in Groups)
            {
                if(!m.HasProperty(g+"Ref")) continue;
                foreach(string suffix in new[]{"WriteMask","Comp","Pass","Fail","ZFail"})
                    if(!m.HasProperty(g+suffix)) return false;
                if(GroupMarked(m,g)) continue;
                bool supportedPass = m.GetInt(g+"Pass")==(int)StencilOp.Keep ||
                    m.GetInt(g+"Pass")==(int)StencilOp.Replace;
                if((m.GetInt(g+"Comp")!=(int)CompareFunction.Always && m.GetInt(g+"Comp")!=(int)CompareFunction.Disabled) || !supportedPass ||
                    m.GetInt(g+"Fail")!=(int)StencilOp.Keep || m.GetInt(g+"ZFail")!=(int)StencilOp.Keep) return false;
            }
            return true;
        }
    }
}
#endif
