#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace liltoon_MCR
{
    // Optional add-on: no assembly dependency, so removing the mask tool is safe.
    public static class MacaronMaskLauncher
    {
        static bool lookedUp;
        static System.Reflection.MethodInfo openFor;

        // Looked up once per script reload (importing or removing the tool also reloads scripts).
        static System.Reflection.MethodInfo OpenFor
        {
            get
            {
                if(!lookedUp)
                {
                    lookedUp = true;
                    var type = Type.GetType("LilMCR.MaskGenerator.UvMaskWindow, lilMCR.MaskGenerator.Editor");
                    openFor = type == null ? null : type.GetMethod("OpenFor", new[] { typeof(GameObject) });
                }
                return openFor;
            }
        }

        public static void Draw(GameObject avatar)
        {
            var method = OpenFor;
            bool available = method != null;
            EditorGUILayout.LabelField(ML.T("轉換後可選取 UV 網格，生成遮罩並自動套用。"), EditorStyles.wordWrappedMiniLabel);
            using(new EditorGUI.DisabledScope(avatar == null || !available))
            {
                if(GUILayout.Button(ML.T("開啟遮罩生成器"), GUILayout.Height(28)))
                {
                    method.Invoke(null, new object[] { avatar });
                }
            }
            if(!available) EditorGUILayout.HelpBox(ML.T("請匯入獨立的 lilMCR_MaskGenerator 資料夾以使用此功能。"), MessageType.Info);
        }
    }
}
#endif
