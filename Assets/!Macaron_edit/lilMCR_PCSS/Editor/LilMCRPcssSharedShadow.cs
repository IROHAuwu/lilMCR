#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace lilMCR.PCSS
{
    // Editor-only synchronization; shader values remain serialized on each material.
    public static class LilMCRPcssSharedShadow
    {
        public const string Property = "_lilMCRPCSS_SelfOnly";
        public static List<Material> Materials(GameObject avatar)
        {
            return LilMCRPcssSetup.AvatarMaterials(avatar)
                .Where(m => LilMCRPcssSetup.IsPcss(m) && m.HasProperty(Property)).ToList();
        }
        public static bool Get(GameObject avatar)
        {
            var materials = Materials(avatar);
            return materials.Count == 0 || materials[0].GetFloat(Property) > 0.5f;
        }
        public static void Set(IEnumerable<Material> seeds, bool enabled)
        {
            var targets = new HashSet<Material>(seeds.Where(m => m != null && m.HasProperty(Property)));
            if(targets.Count == 0) return;
            var roots = new HashSet<GameObject>();
            foreach(var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if(EditorUtility.IsPersistent(renderer) || !renderer.gameObject.scene.IsValid()) continue;
                if(!renderer.sharedMaterials.Any(LilMCRPcssSetup.IsPcss)) continue;
                // The dedicated rig identifies the configured root even without the SDK.
                Transform root = null;
                for(var t = renderer.transform; t != null; t = t.parent)
                    if(LilMCRPcssSetup.FindLight(t.gameObject) != null) { root = t; break; }
                if(root == null) root = LilMCRPcssSetup.FindAvatarRoot(renderer.transform);
                if(root != null) roots.Add(root.gameObject);
            }
            // Index material -> owner groups once, then visit each connected owner once.
            var owners = new Dictionary<Material, List<int>>();
            var groups = roots.Select(Materials).Where(ms => ms.Count > 0).ToList();
            for(int i = 0; i < groups.Count; i++)
                foreach(var material in groups[i])
                {
                    List<int> ids;
                    if(!owners.TryGetValue(material, out ids)) owners[material] = ids = new List<int>();
                    ids.Add(i);
                }
            var pending = new Queue<Material>(targets);
            var visited = new HashSet<int>();
            while(pending.Count > 0)
            {
                List<int> ids;
                if(!owners.TryGetValue(pending.Dequeue(), out ids)) continue;
                foreach(int id in ids)
                {
                    if(!visited.Add(id)) continue;
                    foreach(var material in groups[id])
                        if(targets.Add(material)) pending.Enqueue(material);
                }
            }
            var edits = targets.Where(m => m.GetFloat(Property) != (enabled ? 1f : 0f)).ToArray();
            if(edits.Length == 0) return;
            Undo.RecordObjects(edits, "lilMCR PCSS Shared Self Shadow");
            foreach(var material in edits)
            {
                material.SetFloat(Property, enabled ? 1f : 0f);
                EditorUtility.SetDirty(material);
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        public static bool Draw(GameObject avatar, bool fallback)
        {
            var materials = Materials(avatar);
            bool value = materials.Count == 0 ? fallback : materials[0].GetFloat(Property) > 0.5f;
            EditorGUI.showMixedValue = materials.Any(m => (m.GetFloat(Property) > 0.5f) != value);
            EditorGUI.BeginChangeCheck();
            bool next = EditorGUILayout.Toggle(PcssText.S("僅自身陰影"), value);
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = false;
            if(changed) Set(materials, next);
            EditorGUILayout.LabelField(PcssText.S("角色共用：修改此項會同步所有 PCSS 材質。"), EditorStyles.wordWrappedMiniLabel);
            return next;
        }
    }
}
#endif
