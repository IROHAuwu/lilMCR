using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace liltoon_MCR
{
    /// <summary>
    /// Adds the lilMCR Param Compressor to an avatar as a prefab instance (non-destructive: nothing on the avatar or its
    /// assets is changed; delete the object to undo). Registers itself with the upload-failure notice of lilMCR.
    /// </summary>
    [InitializeOnLoad]
    public static class MacaronParamCompressorSetup
    {
        public const string ObjectName = "lilMCR Param Compressor";
        public const string PrefabFolder = "Assets/liltoon_MCR_Generated";
        public const string PrefabPath = PrefabFolder + "/" + ObjectName + ".prefab";
        private const string MenuPath = MacaronCore.MenuRoot + "Param Compressor パラメータ圧縮/Add 追加 (Selected Avatar)";

        static MacaronParamCompressorSetup()
        {
            MacaronParamNotice.AddCompressor = Add;
            MacaronParamNotice.HasCompressor = Has;
            MacaronParamNotice.DrawEstimate = DrawEstimate;
        }

        // Chart in the upload-failure notice (default settings, or the avatar's own compressor settings).
        private static void DrawEstimate(GameObject avatar)
        {
            if(avatar == null) return;
            MacaronParamCompressor settings = avatar.GetComponentInChildren<MacaronParamCompressor>(true);
            MacaronParamEstimate.Result r = MacaronParamEstimate.Cached(avatar, settings, false);
            if(MacaronParamEstimate.DrawChart(avatar, r)) MacaronParamEstimate.Cached(avatar, settings, true);
        }

        public static bool Has(GameObject avatar)
        {
            return avatar != null && avatar.GetComponentInChildren<MacaronParamCompressor>(true) != null;
        }

        /// <summary>Adds the prefab under the avatar root (or returns the existing one). Undo-able.</summary>
        public static GameObject Add(GameObject avatar)
        {
            if(avatar == null) return null;
            MacaronParamCompressor existing = avatar.GetComponentInChildren<MacaronParamCompressor>(true);
            if(existing != null) return existing.gameObject;

            GameObject prefab = LoadOrCreatePrefab();
            if(prefab == null) return null;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatar.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add lilMCR Param Compressor");
            Undo.SetTransformParent(instance.transform, avatar.transform, "Add lilMCR Param Compressor");
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.transform.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(avatar.scene);
            return instance;
        }

        private static GameObject LoadOrCreatePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if(prefab != null && prefab.GetComponent<MacaronParamCompressor>() != null) return prefab;

            MacaronCore.EnsureFolder(PrefabFolder);
            var temp = new GameObject(ObjectName);
            try
            {
                temp.AddComponent<MacaronParamCompressor>();
                return PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        //------------------------------------------------------------------------------------------
        // Menu (the same as the notice's "Add" button, for adding it before an upload fails)

        [MenuItem(MenuPath, false, 80)]
        private static void AddToSelected()
        {
            Transform root = MacaronCore.FindAvatarRoot(Selection.activeGameObject.transform);
            if(root == null) return;
            if(Has(root.gameObject))
            {
                EditorUtility.DisplayDialog("lilMCR", ML.T("這個角色已經加入 parameters 壓縮。"), "OK");
                return;
            }
            if(!EditorUtility.DisplayDialog(ML.T("添加 parameters 壓縮"), ML.T(MacaronParamNotice.IssuesText), ML.T("添加"), ML.T("取消"))) return;
            GameObject added = Add(root.gameObject);
            if(added != null)
            {
                Selection.activeGameObject = added;
                EditorGUIUtility.PingObject(added);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool AddToSelectedValidate()
        {
            return Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject);
        }
    }

    //==============================================================================================

    [CustomEditor(typeof(MacaronParamCompressor))]
    public class MacaronParamCompressorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            ML.LanguagePopup();
            EditorGUILayout.HelpBox(ML.T("上傳時，只有同步參數超過 VRChat 上限（256 bit）才會壓縮；沒有超過時不做任何改變。不會修改原本的動畫或參數檔案，刪除這個物件即可還原。"), MessageType.Info);

            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSyncSteps"), new GUIContent(ML.T("最多輪流次數"), ML.T("會使用能低於上限的最少次數；次數越多，其他玩家看到的延遲越長。")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeBools"), new GUIContent(ML.T("不壓縮 Bool")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeInts"), new GUIContent(ML.T("不壓縮 Int")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeFloats"), new GUIContent(ML.T("不壓縮 Float")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedPrefixes"), new GUIContent(ML.T("不壓縮的名稱開頭")), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedSuffixes"), new GUIContent(ML.T("不壓縮的名稱結尾")), true);
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            // Before / after chart of the avatar this object belongs to.
            var compressor = (MacaronParamCompressor)target;
            Transform root = compressor != null ? MacaronCore.FindAvatarRoot(compressor.transform) : null;
            if(root != null && !EditorUtility.IsPersistent(compressor))
            {
                GUILayout.Space(6f);
                MacaronParamEstimate.Result r = MacaronParamEstimate.Cached(root.gameObject, compressor, changed);
                if(MacaronParamEstimate.DrawChart(root.gameObject, r)) MacaronParamEstimate.Cached(root.gameObject, compressor, true);
            }

            GUILayout.Space(6f);
            EditorGUILayout.HelpBox(ML.T(MacaronParamNotice.IssuesText), MessageType.None);
        }
    }
}
