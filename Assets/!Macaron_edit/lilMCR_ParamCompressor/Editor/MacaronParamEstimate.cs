using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace liltoon_MCR
{
    /// <summary>
    /// Estimate of the synced parameter cost before / after the lilMCR Param Compressor, and a bar chart of it.
    /// The parameters are the avatar's Expression Parameters plus what the build tools report for the avatar through
    /// NDMF's parameter info (Modular Avatar and other NDMF plugins). The plan is the same one the build pass makes.
    /// Tools that only add parameters while building without reporting them (for example VRCFury) are not included,
    /// so the result of the last upload is shown as well.
    /// </summary>
    public static class MacaronParamEstimate
    {
        public sealed class Result
        {
            public int Limit;
            public int Before, After;
            public int NumberBits, BoolBits;                    // before
            public int CompressedNumbers, CompressedBools;      // parameters that take turns
            public int Steps, AddedBits;
            public bool Over { get { return Before > Limit; } }
            public bool Compresses { get { return Steps > 0; } }
            public bool FromTools;                              // NDMF parameter info was read
        }

        //------------------------------------------------------------------------------------------
        // Estimate

        public static Result Estimate(GameObject avatar, MacaronParamCompressor settings)
        {
            var r = new Result { Limit = VRCExpressionParameters.MAX_PARAMETER_COST };
            if(avatar == null) return r;

            // name -> type of every synced parameter (Expression Parameters first, then the tools).
            var synced = new Dictionary<string, VRCExpressionParameters.ValueType>();
            VRCAvatarDescriptor descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            VRCExpressionParameters own = descriptor != null ? descriptor.expressionParameters : null;
            if(own != null && own.parameters != null)
            {
                foreach(VRCExpressionParameters.Parameter p in own.parameters)
                {
                    if(p != null && !string.IsNullOrEmpty(p.name) && p.networkSynced) synced[p.name] = p.valueType;
                }
            }
            r.FromTools = AddToolParameters(avatar, synced);

            var numbers = new List<VRCExpressionParameters.Parameter>();
            var bools = new List<VRCExpressionParameters.Parameter>();
            foreach(KeyValuePair<string, VRCExpressionParameters.ValueType> kv in synced)
            {
                if(kv.Value == VRCExpressionParameters.ValueType.Bool) r.BoolBits += 1;
                else r.NumberBits += 8;
                if(MacaronParamCompressorPass.IsExcluded(kv.Key, kv.Value, settings)) continue;
                var p = new VRCExpressionParameters.Parameter { name = kv.Key, valueType = kv.Value, networkSynced = true };
                if(kv.Value == VRCExpressionParameters.ValueType.Bool) bools.Add(p);
                else numbers.Add(p);
            }
            r.Before = r.NumberBits + r.BoolBits;
            r.After = r.Before;
            if(r.Before <= r.Limit) return r;

            int maxSteps = settings != null ? Mathf.Clamp(settings.maxSyncSteps, 2, 32) : 8;
            MacaronParamCompressorPass.Plan plan = MacaronParamCompressorPass.ChoosePlan(numbers, bools, r.Before, r.Limit, maxSteps);
            if(plan == null) return r;
            r.CompressedNumbers = plan.Numbers.Count;
            r.CompressedBools = plan.Bools.Count;
            r.Steps = plan.StateCount;
            r.AddedBits = plan.Added;
            r.After = r.Before - plan.Saved;
            return r;
        }

        // NDMF's parameter info (what the parameter UI of Modular Avatar shows), read through reflection so that NDMF
        // versions without it still compile. Returns false when it is not available.
        private static bool infoLookedUp;
        private static object infoForUI;
        private static MethodInfo infoForObject;

        private static bool AddToolParameters(GameObject avatar, Dictionary<string, VRCExpressionParameters.ValueType> synced)
        {
            if(!infoLookedUp)
            {
                infoLookedUp = true;
                try
                {
                    Type t = null;
                    foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = a.GetType("nadena.dev.ndmf.ParameterInfo", false);
                        if(t != null) break;
                    }
                    if(t != null)
                    {
                        // ForUI is a static field in NDMF (a property in some versions): accept both.
                        FieldInfo forUIField = t.GetField("ForUI", BindingFlags.Public | BindingFlags.Static);
                        PropertyInfo forUIProp = forUIField == null ? t.GetProperty("ForUI", BindingFlags.Public | BindingFlags.Static) : null;
                        infoForUI = forUIField != null ? forUIField.GetValue(null) : forUIProp != null ? forUIProp.GetValue(null) : null;
                        // GetParametersForObject(GameObject obj, ConflictHandler onConflict = null): the optional
                        // argument is part of the signature, so match by name and first parameter.
                        foreach(MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if(m.Name != "GetParametersForObject") continue;
                            System.Reflection.ParameterInfo[] ps = m.GetParameters();
                            if(ps.Length >= 1 && ps[0].ParameterType == typeof(GameObject)) { infoForObject = m; break; }
                        }
                    }
                }
                catch(Exception)
                {
                    infoForUI = null;
                    infoForObject = null;
                }
            }
            if(infoForUI == null || infoForObject == null) return false;

            try
            {
                object[] args = new object[infoForObject.GetParameters().Length];
                args[0] = avatar;          // optional arguments stay null (NDMF uses its defaults)
                IEnumerable list = infoForObject.Invoke(infoForUI, args) as IEnumerable;
                if(list == null) return false;
                foreach(object p in list)
                {
                    if(p == null) continue;
                    object ns = Member(p, "Namespace");
                    if(ns != null && ns.ToString() != "Animator") continue;          // PhysBone prefixes etc.
                    if(Member(p, "IsPrefix") is bool prefix && prefix) continue;
                    if(Member(p, "IsAnimatorOnly") is bool animatorOnly && animatorOnly) continue;
                    if(!(Member(p, "WantSynced") is bool want) || !want) continue;
                    string name = Member(p, "EffectiveName") as string;
                    if(string.IsNullOrEmpty(name)) continue;
                    object type = Member(p, "ParameterType");
                    if(type == null) continue;
                    VRCExpressionParameters.ValueType vt;
                    switch(type.ToString())
                    {
                        case "Bool": vt = VRCExpressionParameters.ValueType.Bool; break;
                        case "Int": vt = VRCExpressionParameters.ValueType.Int; break;
                        case "Float": vt = VRCExpressionParameters.ValueType.Float; break;
                        default: continue;
                    }
                    if(!synced.ContainsKey(name)) synced[name] = vt;
                }
                return true;
            }
            catch(Exception)
            {
                return false;
            }
        }

        private static object Member(object o, string name)
        {
            Type t = o.GetType();
            PropertyInfo prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if(prop != null) return prop.GetValue(o);
            FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return field != null ? field.GetValue(o) : null;
        }

        //------------------------------------------------------------------------------------------
        // Result of the last upload (written by the build pass, per avatar name)

        private const string LastKey = "lilMCR.ParamCompressor.Last/";

        public static void RecordLast(string avatarName, int before, int after)
        {
            if(string.IsNullOrEmpty(avatarName)) return;
            EditorPrefs.SetString(LastKey + CleanName(avatarName), before + "," + after);
        }

        public static bool TryGetLast(GameObject avatar, out int before, out int after)
        {
            before = after = 0;
            if(avatar == null) return false;
            string s = EditorPrefs.GetString(LastKey + CleanName(avatar.name), "");
            string[] parts = s.Split(',');
            return parts.Length == 2 && int.TryParse(parts[0], out before) && int.TryParse(parts[1], out after);
        }

        // The build works on a copy of the avatar ("Name(Clone)").
        private static string CleanName(string name)
        {
            const string clone = "(Clone)";
            name = name.Trim();
            while(name.EndsWith(clone, StringComparison.Ordinal)) name = name.Substring(0, name.Length - clone.Length).TrimEnd();
            return name;
        }

        //------------------------------------------------------------------------------------------
        // Chart

        private static readonly Color NumberColor = new Color(0.33f, 0.60f, 0.90f);
        private static readonly Color BoolColor = new Color(0.98f, 0.70f, 0.30f);
        private static readonly Color SyncColor = new Color(0.62f, 0.48f, 0.86f);
        private static readonly Color LimitColor = new Color(0.90f, 0.25f, 0.25f);

        // One cached estimate per avatar: the parameter info walks the whole hierarchy, so it is not recomputed on
        // every repaint, only on request (settings changed, "Recalculate", other avatar).
        private static GameObject cachedAvatar;
        private static Result cached;

        public static Result Cached(GameObject avatar, MacaronParamCompressor settings, bool refresh)
        {
            if(refresh || cached == null || cachedAvatar != avatar)
            {
                cachedAvatar = avatar;
                cached = Estimate(avatar, settings);
            }
            return cached;
        }

        /// <summary>Draws the estimate with a recalculate button. Returns true when the user asked to recalculate.</summary>
        public static bool DrawChart(GameObject avatar, Result r)
        {
            bool refresh = false;
            using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(ML.T("參數使用量（預估）"), EditorStyles.boldLabel);
                    if(GUILayout.Button(ML.T("重新計算"), EditorStyles.miniButton, GUILayout.Width(90f))) refresh = true;
                }

                float scale = Mathf.Max(Mathf.Max(r.Before, r.After), r.Limit) * 1.08f;
                int afterNumbers = r.NumberBits - 8 * r.CompressedNumbers;
                int afterBools = r.BoolBits - r.CompressedBools;
                Bar(ML.T("壓縮前"), r.Before, r.Limit, scale, r.NumberBits, r.BoolBits, 0);
                Bar(ML.T("壓縮後"), r.After, r.Limit, scale, afterNumbers, afterBools, r.Compresses ? r.AddedBits : 0);

                using(new EditorGUILayout.HorizontalScope())
                {
                    Legend(NumberColor, "Int / Float");
                    Legend(BoolColor, "Bool");
                    Legend(SyncColor, ML.T("輪流同步用"));
                    Legend(LimitColor, ML.T("上限") + " " + r.Limit);
                    GUILayout.FlexibleSpace();
                }

                string message;
                if(!r.Over) message = ML.T("沒有超過上限：上傳時不會壓縮。");
                else if(r.After > r.Limit) message = ML.T("壓縮後仍超過上限，請刪減一些選單或開關，或調高「最多輪流次數」。");
                else message = ML.F("{0} 個 Int/Float、{1} 個 Bool 會分 {2} 次輪流同步。", r.CompressedNumbers, r.CompressedBools, r.Steps);
                EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);

                int lastBefore, lastAfter;
                if(TryGetLast(avatar, out lastBefore, out lastAfter))
                    EditorGUILayout.LabelField(ML.F("上次上傳：{0} → {1} bit", lastBefore, lastAfter), EditorStyles.miniLabel);
                EditorGUILayout.LabelField(ML.T(r.FromTools
                    ? "預估值：依角色的 Expression Parameters 與 Modular Avatar 等工具提供的參數計算，實際以上傳時為準。"
                    : "預估值：只計算角色的 Expression Parameters 檔案（無法讀取其他工具的參數），實際以上傳時為準。"), EditorStyles.wordWrappedMiniLabel);
            }
            return refresh;
        }

        private static void Bar(string label, int total, int limit, float scale, int numbers, int bools, int sync)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 20f);
            Rect labelRect = new Rect(row.x, row.y, 70f, row.height);
            Rect bar = new Rect(row.x + 74f, row.y + 2f, Mathf.Max(row.width - 74f, 10f), row.height - 4f);
            GUI.Label(labelRect, label);

            bool dark = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(bar, dark ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.78f, 0.78f, 0.78f));
            float x = bar.x;
            x = Segment(bar, x, numbers, scale, NumberColor);
            x = Segment(bar, x, bools, scale, BoolColor);
            Segment(bar, x, sync, scale, SyncColor);

            float limitX = bar.x + bar.width * Mathf.Clamp01(limit / scale);
            EditorGUI.DrawRect(new Rect(limitX - 1f, row.y, 2f, row.height), LimitColor);

            var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
            style.normal.textColor = total > limit ? LimitColor : (dark ? Color.white : Color.black);
            GUI.Label(new Rect(bar.x, bar.y - 1f, bar.width - 4f, bar.height + 2f), total + " / " + limit + " bit", style);
        }

        private static float Segment(Rect bar, float x, int bits, float scale, Color color)
        {
            if(bits <= 0) return x;
            float w = bar.width * bits / scale;
            EditorGUI.DrawRect(new Rect(x, bar.y, Mathf.Min(w, bar.xMax - x), bar.height), color);
            return x + w;
        }

        private static void Legend(Color color, string text)
        {
            Rect r = GUILayoutUtility.GetRect(10f, 14f, GUILayout.Width(10f));
            r.y += 3f;
            r.height = 8f;
            EditorGUI.DrawRect(r, color);
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            GUILayout.Space(6f);
        }
    }
}
