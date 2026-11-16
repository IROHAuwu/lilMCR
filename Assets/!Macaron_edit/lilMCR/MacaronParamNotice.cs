#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
#endif

namespace liltoon_MCR
{
    /// <summary>
    /// Upload failed because the avatar has too many expression parameters (VRChat SDK log: "... too many parameters
    /// defined"): shows a short notice for avatars that use lilMCR, with "Add parameters compression" / "Close".
    ///
    /// The compression itself lives in the optional lilMCR_ParamCompressor folder (needs NDMF + the VRChat Avatars SDK).
    /// That assembly fills in AddCompressor / HasCompressor when it is compiled; without it only "Close" is offered.
    /// </summary>
    [InitializeOnLoad]
    public static class MacaronParamNotice
    {
        /// <summary>Text of the VRChat SDK error that opens the notice (matched case-insensitively).</summary>
        public const string Trigger = "too many parameters defined";
        public const int SyncedBitLimit = 256;

        /// <summary>Possible problems of the compression, shown before it is added (translated through ML).</summary>
        public const string IssuesText = "parameters 壓縮會把一部分同步參數改成「輪流同步」。加入前請先了解可能發生的問題：\n\n"
            + "・其他玩家看到的開關、數值會比較慢才同步（約 0.1～0.2 秒 × 輪流的次數），快速切換時可能看起來延遲或跳過中間的變化。\n"
            + "・Float 參數以 -1～1 的範圍同步，超出這個範圍的值會被截斷。\n"
            + "・在 FX 動畫控制器中加入一個圖層；Write Defaults 依角色多數的設定決定。\n"
            + "・與 VRCFury 的參數壓縮或其他參數壓縮工具一起使用時，可能互相衝突。\n"
            + "・臉部追蹤（VRCFT）與 VRChat 內建參數不會被壓縮。\n\n"
            + "只會在上傳時處理，不會修改原本的動畫或參數檔案；原本就沒有超過上限的角色不會被壓縮。刪除加入的物件即可還原。";

        /// <summary>Adds the compressor prefab to an avatar and returns the new object (set by lilMCR_ParamCompressor).</summary>
        public static Func<GameObject, GameObject> AddCompressor;
        /// <summary>True when the avatar already has the compressor (set by lilMCR_ParamCompressor).</summary>
        public static Func<GameObject, bool> HasCompressor;
        /// <summary>Draws the before / after parameter chart of an avatar (set by lilMCR_ParamCompressor).</summary>
        public static Action<GameObject> DrawEstimate;

        private static bool pending;
        private static double lastShown = -1000.0;

        static MacaronParamNotice()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if(pending || string.IsNullOrEmpty(message)) return;
            if(message.IndexOf(Trigger, StringComparison.OrdinalIgnoreCase) < 0) return;
            if(Application.isBatchMode) return;
            pending = true;
            // Not from inside the log callback (it can run in the middle of the SDK's build).
            EditorApplication.delayCall += ShowDeferred;
        }

        private static void ShowDeferred()
        {
            pending = false;
            double now = EditorApplication.timeSinceStartup;
            if(now - lastShown < 3.0 && now >= lastShown) return;          // one build can log the error more than once
            List<GameObject> avatars = AvatarsUsingLilMCR();
            if(avatars.Count == 0) return;                                  // not a lilMCR avatar: leave it to the SDK
            lastShown = now;
            MacaronParamNoticeWindow.Open(avatars);
        }

        //------------------------------------------------------------------------------------------

        /// <summary>Avatars (VRC Avatar Descriptor) of the open scenes that have at least one lilMCR material or lilMCR menu.
        /// The selected avatar comes first.</summary>
        public static List<GameObject> AvatarsUsingLilMCR()
        {
            var list = new List<GameObject>();
#if VRC_SDK_VRCSDK3
            GameObject selected = null;
            if(Selection.activeGameObject != null)
            {
                Transform root = MacaronCore.FindAvatarRoot(Selection.activeGameObject.transform);
                if(root != null) selected = root.gameObject;
            }
            foreach(VRCAvatarDescriptor d in UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true))
            {
                if(d == null || EditorUtility.IsPersistent(d)) continue;
                GameObject go = d.gameObject;
                if((go.hideFlags & HideFlags.DontSave) != 0) continue;
                if(go.name.EndsWith("(Clone)", StringComparison.Ordinal)) continue;     // the SDK's build copy
                if(!UsesLilMCR(go)) continue;
                if(go == selected) list.Insert(0, go);
                else list.Add(go);
            }
#endif
            return list;
        }

        public static bool UsesLilMCR(GameObject avatar)
        {
            if(avatar == null) return false;
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach(Material m in r.sharedMaterials) if(MacaronCore.IsMacaronMaterial(m)) return true;
            }
            foreach(Transform t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if(t.name == MacaronLightAdjuster.ObjectName) return true;
            }
            return false;
        }

        /// <summary>Synced cost (bits) of the avatar's own Expression Parameters asset, or -1. Parameters that
        /// Modular Avatar and other tools add while uploading are not in this asset.</summary>
        public static int OwnParameterCost(GameObject avatar)
        {
#if VRC_SDK_VRCSDK3
            if(avatar == null) return -1;
            VRCAvatarDescriptor d = avatar.GetComponent<VRCAvatarDescriptor>();
            VRCExpressionParameters p = d != null ? d.expressionParameters : null;
            return p != null ? p.CalcTotalCost() : -1;
#else
            return -1;
#endif
        }
    }

    //==============================================================================================

    public class MacaronParamNoticeWindow : EditorWindow
    {
        private readonly List<GameObject> avatars = new List<GameObject>();
        private int index;
        private Vector2 scroll;

        public static void Open(List<GameObject> found)
        {
            MacaronParamNoticeWindow w = GetWindow<MacaronParamNoticeWindow>(true, "lilMCR", true);
            w.avatars.Clear();
            w.avatars.AddRange(found);
            w.index = 0;
            w.minSize = new Vector2(420f, 420f);
            w.maxSize = new Vector2(640f, 720f);
            w.Focus();
        }

        private GameObject Avatar
        {
            get
            {
                avatars.RemoveAll(a => a == null);
                if(avatars.Count == 0) return null;
                index = Mathf.Clamp(index, 0, avatars.Count - 1);
                return avatars[index];
            }
        }

        private void OnGUI()
        {
            titleContent.text = ML.T("溫馨提示");
            MacaronGUI.Banner(ML.T("溫馨提示"), ML.T("上傳失敗：選單的參數太多了"));

            GameObject avatar = Avatar;
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField(ML.F("VRChat 同步參數（Expression Parameters）的上限是 {0} bit。這個角色的選單、開關太多，超過了上限，所以無法上傳。", MacaronParamNotice.SyncedBitLimit), EditorStyles.wordWrappedLabel);
            GUILayout.Space(4f);
            EditorGUILayout.LabelField(ML.T("請刪減一些不需要的選單或開關，或是加入「parameters 壓縮」後再上傳一次。"), EditorStyles.wordWrappedLabel);
            GUILayout.Space(6f);

            if(avatars.Count > 1)
            {
                string[] names = new string[avatars.Count];
                for(int i = 0; i < avatars.Count; i++) names[i] = avatars[i] != null ? avatars[i].name : "-";
                index = EditorGUILayout.Popup(ML.T("角色"), index, names);
                avatar = Avatar;
            }
            else if(avatar != null)
            {
                using(new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(ML.T("角色"), avatar, typeof(GameObject), true);
                }
            }

            bool available = MacaronParamNotice.AddCompressor != null;
            bool already = avatar != null && MacaronParamNotice.HasCompressor != null && MacaronParamNotice.HasCompressor(avatar);
            int own = MacaronParamNotice.OwnParameterCost(avatar);

            if(!available)
            {
                EditorGUILayout.HelpBox(ML.T("parameters 壓縮需要 NDMF（Modular Avatar）與 VRChat Avatars SDK。"), MessageType.Info);
            }
            else if(already)
            {
                EditorGUILayout.HelpBox(ML.T("這個角色已經加入 parameters 壓縮，但參數仍然超過上限。請刪減一些選單或開關。"), MessageType.Warning);
            }
            if(own > MacaronParamNotice.SyncedBitLimit)
            {
                EditorGUILayout.HelpBox(ML.F("這個角色的 Expression Parameters 檔案本身就已經使用 {0} / {1} bit。上傳前的檢查會直接看這個檔案，壓縮無法讓它通過，請先在這個檔案中刪減參數。", own, MacaronParamNotice.SyncedBitLimit), MessageType.Warning);
            }
            if(avatar != null && MacaronParamNotice.DrawEstimate != null)
            {
                GUILayout.Space(4f);
                MacaronParamNotice.DrawEstimate(avatar);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(!available || already || avatar == null))
                {
                    if(MacaronGUI.BigButton(ML.T("添加 parameters 壓縮"), 30f)) AddCompression(avatar);
                }
                if(GUILayout.Button(ML.T("關閉視窗"), GUILayout.Height(30f))) Close();
            }
            GUILayout.Space(6f);
        }

        private void AddCompression(GameObject avatar)
        {
            if(avatar == null || MacaronParamNotice.AddCompressor == null) return;
            string issues = ML.T(MacaronParamNotice.IssuesText);
            if(!EditorUtility.DisplayDialog(ML.T("添加 parameters 壓縮"), issues, ML.T("添加"), ML.T("取消"))) return;

            GameObject added = MacaronParamNotice.AddCompressor(avatar);
            if(added == null) return;
            Selection.activeGameObject = added;
            EditorGUIUtility.PingObject(added);
            EditorUtility.DisplayDialog(ML.T("添加 parameters 壓縮"), ML.F("已在「{0}」底下加入「{1}」。請再上傳一次。", avatar.name, added.name), "OK");
            Close();
        }
    }
}
#endif
