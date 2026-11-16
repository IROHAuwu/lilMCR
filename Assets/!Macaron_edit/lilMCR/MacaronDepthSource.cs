#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace liltoon_MCR
{
    // Shared by AO/contact shadow and world projection. PCSS's ForcePixel spot light
    // serves a different pass and must not be converted into this directional trigger.
    public static class MacaronDepthSource
    {
        public static bool Ensure(GameObject avatar)
        {
            Transform holder = avatar.transform.Find(MacaronCore.DepthSourceName);
            bool created = holder == null;
            if(created)
            {
                var go = new GameObject(MacaronCore.DepthSourceName);
                go.transform.SetParent(avatar.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Macaron Shared Depth Source");
                holder = go.transform;
            }
            Light light = holder.GetComponentInChildren<Light>(true);
            // Reuse a depth light left under the World Shadow object. Only this exact owned path is touched;
            // other avatar / world lights are never collected or changed.
            var legacy = avatar.transform.Find(MacaronWorldShadow.ObjectName + "/Depth Source");
            if(legacy != null && legacy.GetComponent<Light>() != null)
            {
                if(light == null)
                {
                    Undo.SetTransformParent(legacy, holder, "Macaron Share Depth Light");
                    Undo.RecordObject(legacy.gameObject, "Macaron Share Depth Light");
                    legacy.name = "Depth Light";
                    light = legacy.GetComponent<Light>();
                }
                else Undo.DestroyObjectImmediate(legacy.gameObject);
            }
            if(light == null)
            {
                var go = new GameObject("Depth Light"); go.transform.SetParent(holder, false);
                Undo.RegisterCreatedObjectUndo(go, "Macaron Shared Depth Light");
                light = Undo.AddComponent<Light>(go);
            }
            Undo.RecordObject(holder.gameObject, "Macaron Shared Depth Light");
            Undo.RecordObject(light.gameObject, "Macaron Shared Depth Light");
            Undo.RecordObject(light, "Macaron Shared Depth Light");
            holder.gameObject.SetActive(true); light.gameObject.SetActive(true); light.enabled = true;
            light.type = LightType.Directional; light.color = Color.black; light.intensity = 0.001f;
            light.shadows = LightShadows.Hard; light.shadowStrength = 1;
            light.renderMode = LightRenderMode.ForceVertex;
            light.cullingMask = (1 << Layer("StereoLeft", 15)) | (1 << Layer("MirrorReflection", 18));
            light.lightmapBakeType = LightmapBakeType.Realtime;
            EditorUtility.SetDirty(light);
            return created;
        }
        static int Layer(string name, int fallback) { int n = LayerMask.NameToLayer(name); return n < 0 ? fallback : n; }
    }
}
#endif
