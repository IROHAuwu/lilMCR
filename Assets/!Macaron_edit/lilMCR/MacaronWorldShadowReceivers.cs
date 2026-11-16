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

        // Clone source materials. Keep/Replace with Always/Disabled comparisons can
        // reserve bit 128; conditional stencil effects are reported without modification.
        public static string Apply(GameObject avatar, string folder, Vector4? capture = null)
        {
            var copies = new Dictionary<Material, Material>();
            var skipped = new HashSet<string>();
            Transform module = avatar.transform.Find(MacaronWorldShadow.ObjectName);
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(module != null && r.transform.IsChildOf(module)) continue;
                var materials = r.sharedMaterials; bool changed = false;
                for(int i=0;i<materials.Length;i++)
                {
                    Material source=materials[i]; if(source == null) continue;
                    bool canMark = CanMark(source);
                    if(!canMark)
                    {
                        skipped.Add(source.name);
                        if(!capture.HasValue || !source.HasProperty("_MCR_WorldCapture")) continue;
                    }
                    if(IsMarked(source) && !capture.HasValue) continue;
                    Material copy;
                    if(!copies.TryGetValue(source,out copy))
                    {
                        string materialName = source.name;
                        const string legacySuffix = " (World Shadow Receiver Excluded)";
                        while(materialName.EndsWith(legacySuffix, System.StringComparison.Ordinal) ||
                              materialName.EndsWith("_WSR", System.StringComparison.Ordinal))
                        {
                            int suffixLength = materialName.EndsWith("_WSR", System.StringComparison.Ordinal) ? 4 : legacySuffix.Length;
                            materialName = materialName.Substring(0, materialName.Length - suffixLength);
                        }
                        copy = new Material(source) { name=materialName + "_WSR" };
                        if(canMark) foreach(string group in Groups)
                        {
                            if(!source.HasProperty(group+"Ref")) continue;
                            copy.SetInt(group+"Ref", source.GetInt(group+"Comp") == (int)CompareFunction.Always && source.GetInt(group+"Pass") == (int)StencilOp.Replace ? source.GetInt(group+"Ref") | Bit : Bit);
                            copy.SetInt(group+"Comp", (int)CompareFunction.Always);
                            // Preserve existing lower-bit writes only when stencil was enabled.
                            int write = source.GetInt(group+"Comp") == (int)CompareFunction.Always && source.GetInt(group+"Pass") == (int)StencilOp.Replace
                                ? source.GetInt(group+"WriteMask") | Bit : Bit;
                            copy.SetInt(group+"WriteMask", write);
                            copy.SetInt(group+"Pass", (int)StencilOp.Replace);
                        }
                        if(capture.HasValue && copy.HasProperty("_MCR_WorldCapture")) copy.SetVector("_MCR_WorldCapture", capture.Value);
                        // Capture key set -> capture pass on; copies without a key keep it off.
                        MacaronCore.SyncCapturePass(copy);
                        string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+MacaronCore.SafeFileName(copy.name)+".mat");
                        AssetDatabase.CreateAsset(copy,path);
                        // Keep the converter's original-material provenance for Restore.
                        var origin=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source));
                        var importer=AssetImporter.GetAtPath(path);
                        if(origin != null && importer != null) { importer.userData=origin.userData; AssetDatabase.WriteImportSettingsIfDirty(path); }
                        copies.Add(source,copy);
                    }
                    materials[i]=copy; changed=true;
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
