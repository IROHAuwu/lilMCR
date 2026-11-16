#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace lilMCR.PCSS
{
    // In-memory copy of the per-avatar coverage (EditorPrefs), so Undo / Redo can restore it.
    internal sealed class LilMCRPcssCoverageStore : ScriptableObject
    {
        public List<string> Keys = new List<string>();
        public List<float> Values = new List<float>();

        private static LilMCRPcssCoverageStore instance;
        public static LilMCRPcssCoverageStore Instance
        {
            get
            {
                if(instance == null)
                {
                    instance = CreateInstance<LilMCRPcssCoverageStore>();
                    instance.hideFlags = HideFlags.HideAndDontSave;
                }
                return instance;
            }
        }

        public bool TryGet(string key, out float value)
        {
            int i = Keys.IndexOf(key);
            value = i >= 0 ? Values[i] : 0f;
            return i >= 0;
        }

        public void Set(string key, float value)
        {
            int i = Keys.IndexOf(key);
            if(i >= 0) Values[i] = value; else { Keys.Add(key); Values.Add(value); }
        }

        public void Remove(string key)
        {
            int i = Keys.IndexOf(key);
            if(i >= 0) { Keys.RemoveAt(i); Values.RemoveAt(i); }
        }
    }
}
#endif
