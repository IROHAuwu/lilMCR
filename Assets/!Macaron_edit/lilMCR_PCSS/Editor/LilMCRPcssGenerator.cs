#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace lilMCR.PCSS
{
    /// <summary>
    /// Builds the PCSS variants of Macaron's shaders without touching Macaron.
    ///
    ///   Macaron folder (any version)  --copy-->  lilMCR_PCSS/Generated  + 3 small patches:
    ///     1. lilCustomShaderDatas.lilblock : ShaderName "Macaron" -> "Macaron/PCSS", EditorName -> lilToon.LilMCRPcssInspector,
    ///        + InsertPassPost / InsertUsePassPost (lilToon's pass insertion) for the pass "LILMCR_PCSS_SHADOW"
    ///     2. property blocks used by the containers (lilProperties "....lilblock") : + Templates/pcss_properties.lilblock
    ///   and the add-on's pass blocks + lilmcr_pcss.hlsl are copied next to them. Generated copies also receive a depth-packing hook for the dedicated ShadowCaster.
    ///   Source Macaron files are never changed.
    ///
    /// Everything else (Macaron's hlsl, its containers, new files of a future version) is copied as it is, so a new
    /// Macaron version is picked up by copying again. This runs automatically:
    ///   - after Macaron's files (or the add-on's Templates) are imported, changed, moved or deleted,
    ///   - after every script reload (cheap: only files whose content differs are written),
    ///   - from the Setup window ("Regenerate shaders").
    /// When Macaron cannot be found the last generated shaders are kept.
    /// </summary>
    public static class LilMCRPcssGenerator
    {
        public const string SourceShaderName = "Macaron";                 // ShaderName in Macaron's lilCustomShaderDatas.lilblock
        public const string PcssSegment = "PCSS";
        public const string PcssShaderName = SourceShaderName + "/" + PcssSegment;
        public const string InspectorName = "lilToon.LilMCRPcssInspector";
        public const string GeneratedFolderName = "Generated";
        public const string TemplatesFolderName = "Templates";
        public const string ModuleFile = "lilmcr_pcss.hlsl";
        public const string PropertiesTemplateFile = "pcss_properties.lilblock";
        public const string PassFile = "lilmcr_pcss_pass.lilblock";
        public const string MasterSwitchProperty = "_MCR_Active";
        public const string MenuSwitchProperty = "_MCR_AdjActive";
        public const string LocalOnlyProperty = "_MCR_LocalOnly";      // Macaron's Local Only (mcr_local_only.hlsl)
        public const string IsLocalProperty = "_MCR_UzuIsLocal";
        public const string GlobalMaskProperty = "_MCR_GlobalMask";
        public const string GlobalMaskAmountProperty = "_MCR_GlobalMaskAmount";
        public const string UsePassFile = "lilmcr_pcss_usepass.lilblock";

        private const string DataFile = "lilCustomShaderDatas.lilblock";
        private const string AsmdefName = "lilMCR.PCSS.Editor";
        private static readonly string[] CopiedExtensions = { ".lilcontainer", ".lilblock", ".hlsl", ".hlslinc", ".cginc" };
        private static readonly string[] PipelineSuffixes = { "BRP", "LWRP", "URP", "HDRP" };

        /// <summary>Message of the last run (shown in the Setup window), translated when read so that it follows
        /// a later language change. Messages that are not in the table (e.g. errors from BuildPlan) are shown as they are.</summary>
        public static string LastMessage { get { return string.IsNullOrEmpty(lastKey) ? "" : (lastArgs.Length == 0 ? PcssText.S(lastKey) : PcssText.F(lastKey, lastArgs)); } }
        private static string lastKey = "";
        private static object[] lastArgs = new object[0];
        private static void SetMessage(string key, params object[] args) { lastKey = key ?? ""; lastArgs = args ?? new object[0]; }
        public static bool LastOk;

        //------------------------------------------------------------------------------------------
        // Locations

        private static string addonRoot;

        /// <summary>Assets path of the add-on folder (the parent of Editor/), e.g. "Assets/lilMCR_PCSS".</summary>
        public static string AddonRoot
        {
            get
            {
                if(addonRoot != null && AssetDatabase.IsValidFolder(addonRoot)) return addonRoot;
                addonRoot = null;
                foreach(string guid in AssetDatabase.FindAssets(AsmdefName))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if(Path.GetFileName(p) != AsmdefName + ".asmdef") continue;
                    string editorDir = Path.GetDirectoryName(p).Replace('\\', '/');
                    addonRoot = Path.GetDirectoryName(editorDir).Replace('\\', '/');
                    break;
                }
                return addonRoot;
            }
        }

        public static string GeneratedFolder { get { return AddonRoot == null ? null : AddonRoot + "/" + GeneratedFolderName; } }
        public static string TemplatesFolder { get { return AddonRoot == null ? null : AddonRoot + "/" + TemplatesFolderName; } }

        private static string sourceFolder;

        /// <summary>Assets path of Macaron's shader folder (where lilCustomShaderDatas.lilblock says ShaderName "Macaron").</summary>
        public static string SourceFolder
        {
            get
            {
                if(sourceFolder != null && File.Exists(sourceFolder + "/" + DataFile) && ReadShaderName(sourceFolder + "/" + DataFile) == SourceShaderName)
                {
                    return sourceFolder;
                }
                sourceFolder = null;
                string generated = GeneratedFolder;
                List<string> found = new List<string>();
                foreach(string guid in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(DataFile)))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if(Path.GetFileName(p) != DataFile) continue;
                    if(generated != null && p.StartsWith(generated + "/", StringComparison.Ordinal)) continue;
                    if(ReadShaderName(p) != SourceShaderName) continue;
                    found.Add(Path.GetDirectoryName(p).Replace('\\', '/'));
                }
                if(found.Count == 0) return null;
                found.Sort(StringComparer.Ordinal);
                if(found.Count > 1) Debug.LogWarning("[lilMCR PCSS] " + PcssText.F("找到多個 Macaron 資料夾，使用：{0}", found[0]));
                sourceFolder = found[0];
                return sourceFolder;
            }
        }

        private static string ReadShaderName(string dataPath)
        {
            try
            {
                foreach(string line in File.ReadAllLines(dataPath))
                {
                    if(line.TrimStart().StartsWith("ShaderName", StringComparison.Ordinal)) return FirstQuoted(line);
                }
            }
            catch(Exception) { }
            return null;
        }

        //------------------------------------------------------------------------------------------
        // Shader names

        /// <summary>"Macaron/X" -> "Macaron/PCSS/X", "Hidden/Macaron/X" -> "Hidden/Macaron/PCSS/X"; null when not a Macaron name.</summary>
        public static string ToPcssName(string name)
        {
            if(string.IsNullOrEmpty(name) || IsPcssName(name)) return null;
            if(name.StartsWith(SourceShaderName + "/", StringComparison.Ordinal))
                return PcssShaderName + name.Substring(SourceShaderName.Length);
            if(name.StartsWith("Hidden/" + SourceShaderName + "/", StringComparison.Ordinal))
                return "Hidden/" + PcssShaderName + name.Substring(("Hidden/" + SourceShaderName).Length);
            return null;
        }

        /// <summary>Inverse of ToPcssName; null when not a PCSS name.</summary>
        public static string ToSourceName(string name)
        {
            if(string.IsNullOrEmpty(name)) return null;
            if(name.StartsWith(PcssShaderName + "/", StringComparison.Ordinal))
                return SourceShaderName + name.Substring(PcssShaderName.Length);
            if(name.StartsWith("Hidden/" + PcssShaderName + "/", StringComparison.Ordinal))
                return "Hidden/" + SourceShaderName + name.Substring(("Hidden/" + PcssShaderName).Length);
            return null;
        }

        public static bool IsPcssName(string name)
        {
            return !string.IsNullOrEmpty(name)
                && (name.StartsWith(PcssShaderName + "/", StringComparison.Ordinal) || name.StartsWith("Hidden/" + PcssShaderName + "/", StringComparison.Ordinal));
        }

        public static bool IsSourceName(string name)
        {
            return ToPcssName(name) != null;
        }

        //------------------------------------------------------------------------------------------
        // Generate

        /// <summary>Copies Macaron and applies the patches. Returns true when the generated shaders are usable.</summary>
        public static bool Regenerate(bool logWhenUnchanged)
        {
            string root = AddonRoot;
            string templates = TemplatesFolder;
            if(root == null || !File.Exists(templates + "/" + ModuleFile) || !File.Exists(templates + "/" + PropertiesTemplateFile)
            || !File.Exists(templates + "/" + PassFile) || !File.Exists(templates + "/" + UsePassFile))
            {
                return Fail("找不到外掛的 Templates 資料夾。請保持 lilMCR_PCSS 資料夾的結構。");
            }
            string src = SourceFolder;
            if(src == null)
            {
                return Fail("找不到 Macaron（lilCustomShaderDatas.lilblock 中 ShaderName 為「{0}」的資料夾）。請先匯入 Macaron。", SourceShaderName);
            }

            Dictionary<string, string> plan;
            string error;
            if(!BuildPlan(src, templates, out plan, out error)) return Fail(error);

            string outDir = GeneratedFolder;
            int written = 0, removed = 0;
            List<string> changedPaths = new List<string>();
            Directory.CreateDirectory(outDir);

            foreach(KeyValuePair<string, string> kv in plan)
            {
                string path = outDir + "/" + kv.Key;
                string dir = Path.GetDirectoryName(path);
                if(!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if(File.Exists(path) && File.ReadAllText(path) == kv.Value) continue;
                File.WriteAllText(path, kv.Value, new UTF8Encoding(false));
                // A new file gets a .meta with a fixed GUID (Unity would give it a random one): deleting the add-on
                // folder and importing it again then generates the same shaders with the same GUIDs, and the PCSS
                // materials keep their shader. An existing .meta is never touched.
                if(!File.Exists(path + ".meta"))
                    File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: " + StableGuid(kv.Key) + "\n", new UTF8Encoding(false));
                changedPaths.Add(path);
                written++;
            }

            // Generated files whose source no longer exists.
            foreach(string file in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Replace('\\', '/').Substring(outDir.Length + 1);
                if(rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if(plan.ContainsKey(rel)) continue;
                AssetDatabase.DeleteAsset(outDir + "/" + rel);
                removed++;
            }

            if(written > 0 || removed > 0)
            {
                // The .lilcontainer importer depends on every file of its folder, so importing the changed files
                // re-imports the shaders. Containers of a new folder are picked up by the refresh.
                AssetDatabase.Refresh();
                foreach(string p in changedPaths) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                SetMessage("已從 {0} 產生 lilMCR PCSS 著色器（更新 {1} 個檔案，刪除 {2} 個）。", src, written, removed);
                Debug.Log("[lilMCR PCSS] " + LastMessage);
            }
            else
            {
                SetMessage("lilMCR PCSS 著色器已是最新（來源：{0}）。", src);
                if(logWhenUnchanged) Debug.Log("[lilMCR PCSS] " + LastMessage);
            }
            LastOk = true;
            return true;
        }

        // GUID of a generated file: the same for the same path inside the Generated folder, in every project.
        private static string StableGuid(string relativePath)
        {
            string key = "lilMCR_PCSS/" + GeneratedFolderName + "/" + relativePath.Replace('\\', '/').ToLowerInvariant();
            using(var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                StringBuilder sb = new StringBuilder(32);
                foreach(byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool Fail(string key, params object[] args)
        {
            SetMessage(key, args);
            string message = LastMessage;
            LastOk = false;
            Debug.LogWarning("[lilMCR PCSS] " + message);
            return false;
        }

        /// <summary>The generated folder's content: relative path -> text. Pure (reads files, writes nothing).</summary>
        public static bool BuildPlan(string src, string templates, out Dictionary<string, string> plan, out string error)
        {
            plan = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;

            foreach(string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if(Array.IndexOf(CopiedExtensions, ext) < 0) continue;
                string rel = file.Replace('\\', '/').Substring(src.Length + 1);
                plan[rel] = File.ReadAllText(file);
            }

            // Property blocks the containers use.
            HashSet<string> propertyBlocks = new HashSet<string>(StringComparer.Ordinal);
            foreach(KeyValuePair<string, string> kv in plan)
            {
                if(!kv.Key.EndsWith(".lilcontainer", StringComparison.OrdinalIgnoreCase)) continue;
                foreach(string raw in kv.Value.Split('\n'))
                {
                    string line = raw.Trim();
                    if(!line.StartsWith("lilProperties", StringComparison.Ordinal)) continue;
                    string name = LastQuoted(line);
                    if(name != null && plan.ContainsKey(name)) propertyBlocks.Add(name);   // built-in blocks ("Default") are lilToon's
                }
            }

            if(!plan.ContainsKey(DataFile)) { error = PcssText.F("找不到 Macaron（lilCustomShaderDatas.lilblock 中 ShaderName 為「{0}」的資料夾）。請先匯入 Macaron。", SourceShaderName); return false; }

            // Pass blocks of the add-on (BRP content + empty variants for the other pipelines).
            Dictionary<string, string> passFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach(string f in new[] { PassFile, UsePassFile })
            {
                string stem = Path.GetFileNameWithoutExtension(f);
                passFiles[f] = File.ReadAllText(templates + "/" + f);
                foreach(string rp in PipelineSuffixes)
                {
                    string v = stem + rp + ".lilblock";
                    if(rp != "BRP" && File.Exists(templates + "/" + v)) passFiles[v] = File.ReadAllText(templates + "/" + v);
                }
            }

            // Links to Macaron's own controls, only when Macaron has those properties:
            //   master switch (_MCR_Active) and the menu's "lilMCR on/off" (_MCR_AdjActive) -> on/off, global mask (_MCR_GlobalMask + amount) -> where the shadow appears.
            string links = "";
            if(HasProperty(plan, propertyBlocks, "_MCR_WorldCapture"))
                links += "\n            #define LMP_WORLD_CAPTURE _MCR_WorldCapture";
            if(HasProperty(plan, propertyBlocks, MasterSwitchProperty))
                links += "\n            #define LMP_MASTER_SWITCH " + MasterSwitchProperty;
            if(HasProperty(plan, propertyBlocks, MenuSwitchProperty))
                links += "\n            #define LMP_MENU_SWITCH " + MenuSwitchProperty;
            if(HasProperty(plan, propertyBlocks, LocalOnlyProperty) && HasProperty(plan, propertyBlocks, IsLocalProperty) && plan.ContainsKey("mcr_local_only.hlsl"))
                links += "\n            #define LMP_LOCAL_ONLY";
            if(HasProperty(plan, propertyBlocks, GlobalMaskProperty) && HasProperty(plan, propertyBlocks, GlobalMaskAmountProperty))
                links += "\n            #define LMP_GLOBAL_MASK " + GlobalMaskProperty
                       + "\n            #define LMP_GLOBAL_MASK_ST " + GlobalMaskProperty + "_ST"
                       + "\n            #define LMP_GLOBAL_MASK_AMOUNT " + GlobalMaskAmountProperty;
            if(links.Length > 0) passFiles[PassFile] = passFiles[PassFile].Replace("#define LILMCR_PCSS_PASS", "#define LILMCR_PCSS_PASS" + links);

            // 1. Shader / inspector names, pass insertion.
            StringBuilder sb = new StringBuilder();
            bool hasPassPost = false, hasUsePassPost = false;
            foreach(string raw in plan[DataFile].Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.TrimStart();
                if(t.StartsWith("ShaderName", StringComparison.Ordinal)) { sb.Append("ShaderName \"" + PcssShaderName + "\"\n"); continue; }
                if(t.StartsWith("EditorName", StringComparison.Ordinal)) { sb.Append("EditorName \"" + InspectorName + "\"\n"); continue; }
                // Macaron already inserts passes there (lilToon reads one file per tag): add ours to its file.
                if(t.StartsWith("InsertPassPost", StringComparison.Ordinal)) { hasPassPost = true; AppendToInsert(plan, LastQuoted(t), passFiles[PassFile]); }
                if(t.StartsWith("InsertUsePassPost", StringComparison.Ordinal)) { hasUsePassPost = true; AppendToInsert(plan, LastQuoted(t), passFiles[UsePassFile]); }
                sb.Append(raw).Append('\n');
            }
            string data = sb.ToString().TrimEnd('\n');
            if(!hasPassPost)
            {
                data += "\nInsertPassPost \"!FakeShadow\" \"" + PassFile + "\"";
                foreach(KeyValuePair<string, string> kv in passFiles) if(kv.Key.StartsWith(Path.GetFileNameWithoutExtension(PassFile), StringComparison.Ordinal)) plan[kv.Key] = kv.Value;
            }
            if(!hasUsePassPost)
            {
                data += "\nInsertUsePassPost \"!Overlay\" \"" + UsePassFile + "\"";
                foreach(KeyValuePair<string, string> kv in passFiles) if(kv.Key.StartsWith(Path.GetFileNameWithoutExtension(UsePassFile), StringComparison.Ordinal)) plan[kv.Key] = kv.Value;
            }
            plan[DataFile] = data;

            // 2. Properties
            string props = File.ReadAllText(templates + "/" + PropertiesTemplateFile).Replace("\r\n", "\n").TrimEnd('\n');
            foreach(string name in propertyBlocks) plan[name] = plan[name].Replace("\r\n", "\n").TrimEnd('\n') + "\n" + props + "\n";

            // Self-shadow depth support is added only to generated copies.
            // Fail explicitly if a future source layout cannot support the insertion.
            if(!plan.ContainsKey("custom.hlsl") || !plan.ContainsKey("custom_insert.hlsl"))
            {
                error = "PCSS 自身陰影需要 custom.hlsl 與 custom_insert.hlsl。";
                return false;
            }
            // The add-on's material variables join Macaron's LIL_CUSTOM_PROPERTIES (lilToon's per-material CBUFFER).
            Regex customProps = new Regex(@"^([ \t]*#define[ \t]+LIL_CUSTOM_PROPERTIES)\b", RegexOptions.Multiline);
            if(customProps.Matches(plan["custom.hlsl"]).Count != 1)
            {
                error = "PCSS 需要 custom.hlsl 中恰好有一個「#define LIL_CUSTOM_PROPERTIES」。";
                return false;
            }
            plan["custom.hlsl"] = customProps.Replace(plan["custom.hlsl"], "$1 LMP_PCSS_PROPERTIES", 1);
            plan["custom.hlsl"] += "\n#include \"lilmcr_pcss_depth.hlsl\"\n";
            plan["custom_insert.hlsl"] += "\n#include \"lilmcr_pcss_caster.hlsl\"\n";
            foreach(string support in new[] { "lilmcr_pcss_depth.hlsl", "lilmcr_pcss_depth_math.hlsl", "lilmcr_pcss_caster.hlsl" })
                plan[support] = File.ReadAllText(templates + "/" + support);

            plan[ModuleFile] = File.ReadAllText(templates + "/" + ModuleFile);
            return true;
        }

        private static bool HasProperty(Dictionary<string, string> plan, IEnumerable<string> blocks, string property)
        {
            foreach(string name in blocks)
            {
                if(Regex.IsMatch(plan[name], "(^|[\\s\\]])" + Regex.Escape(property) + "\\s*\\(", RegexOptions.Multiline)) return true;
            }
            return false;
        }

        // Appends the add-on's pass to Macaron's own insert file (the BRP variant when Macaron has one).
        private static void AppendToInsert(Dictionary<string, string> plan, string name, string content)
        {
            if(name == null) return;
            string brp = Path.GetFileNameWithoutExtension(name) + "BRP" + Path.GetExtension(name);
            string target = plan.ContainsKey(brp) ? brp : name;
            if(!plan.ContainsKey(target)) return;
            plan[target] = plan[target].Replace("\r\n", "\n").TrimEnd('\n') + "\n" + content.Replace("\r\n", "\n");
        }

        private static string FirstQuoted(string line)
        {
            int a = line.IndexOf('"');
            int b = a >= 0 ? line.IndexOf('"', a + 1) : -1;
            return b > a ? line.Substring(a + 1, b - a - 1) : null;
        }

        private static string LastQuoted(string line)
        {
            int b = line.LastIndexOf('"');
            int a = b > 0 ? line.LastIndexOf('"', b - 1) : -1;
            return a >= 0 ? line.Substring(a + 1, b - a - 1) : null;
        }

        //------------------------------------------------------------------------------------------
        // Automatic regeneration

        private static bool scheduled;

        public static void Schedule()
        {
            if(scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    Schedule();
                    return;
                }
                Regenerate(false);
            };
        }

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            Schedule();
        }

        private class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                string generated = GeneratedFolder;
                string templates = TemplatesFolder;
                string src = sourceFolder;   // last known location (the property re-searches after a move)
                if(Touches(imported, src, templates, generated) || Touches(deleted, src, templates, generated)
                || Touches(moved, src, templates, generated) || Touches(movedFrom, src, templates, generated))
                {
                    Schedule();
                }
            }

            private static bool Touches(string[] paths, string src, string templates, string generated)
            {
                foreach(string raw in paths)
                {
                    string p = raw.Replace('\\', '/');
                    if(generated != null && (p == generated || p.StartsWith(generated + "/", StringComparison.Ordinal))) continue;
                    if(src != null && p.StartsWith(src + "/", StringComparison.Ordinal)) return true;
                    if(templates != null && p.StartsWith(templates + "/", StringComparison.Ordinal)) return true;
                    if(Path.GetFileName(p) == DataFile) return true;   // Macaron installed or moved
                }
                return false;
            }
        }
    }
}
#endif
