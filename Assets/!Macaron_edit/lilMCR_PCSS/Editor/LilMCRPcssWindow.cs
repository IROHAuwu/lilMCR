#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using liltoon_MCR;

namespace lilMCR.PCSS
{
    /// <summary>Macaron > lilMCR PCSS > Setup</summary>
    public class LilMCRPcssWindow : EditorWindow
    {
        private GameObject avatar;
        private bool deleteFiles;
        private bool selfOnly = true;
        private string message = "";
        private MessageType messageType = MessageType.Info;
        private Vector2 scroll;

        [MenuItem("Macaron/lilMCR PCSS/Setup 設定", false, 40)]
        public static void Open()
        {
            LilMCRPcssWindow w = GetWindow<LilMCRPcssWindow>();
            w.titleContent = new GUIContent("lilMCR PCSS");
            w.minSize = new Vector2(360f, 380f);
            w.PickSelection();
            w.Show();
        }

        private void OnSelectionChange()
        {
            if(avatar == null) { PickSelection(); Repaint(); }
        }

        private void PickSelection()
        {
            GameObject a = LilMCRPcssSetup.SelectedAvatar();
            if(a != null) SetAvatar(a);
        }

        // Closing the window turns the editor preview off (the light must not reach the Default layer when uploading).
        private void OnDestroy() { PreviewOff(avatar); }

        private static void PreviewOff(GameObject a)
        {
            if(a != null && LilMCRPcssSetup.IsEditorPreview(a)) LilMCRPcssSetup.SetEditorPreview(a, false);
        }

        private void SetAvatar(GameObject a)
        {
            if(avatar != null && avatar != a) PreviewOff(avatar);
            avatar = a;
            selfOnly = LilMCRPcssSharedShadow.Get(a);
            message = "";
        }

        private void OnGUI()
        {
            titleContent.text = "lilMCR PCSS";
            MacaronGUI.Banner("lilMCR PCSS", PcssText.S("專用光源的柔和落影。選擇角色，按一下就完成。"));
            PcssText.LanguagePopup();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            // 1. Avatar
            MacaronGUI.Step(1, PcssText.S("選擇角色"));
            EditorGUI.BeginChangeCheck();
            GameObject a = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true, GUILayout.Height(20f));
            if(EditorGUI.EndChangeCheck())
            {
                if(a != null && EditorUtility.IsPersistent(a)) a = null;
                if(a != null) a = LilMCRPcssSetup.FindAvatarRoot(a.transform).gameObject;
                SetAvatar(a);
            }
            if(avatar == null)
            {
                EditorGUILayout.LabelField(PcssText.S("請選擇場景中的角色。"), EditorStyles.wordWrappedMiniLabel);
                DrawShaderInfo();
                EditorGUILayout.EndScrollView();
                return;
            }

            List<Material> mats = LilMCRPcssSetup.AvatarMaterials(avatar);
            int pcssCount = 0;
            foreach(Material m in mats) if(LilMCRPcssSetup.IsPcss(m)) pcssCount++;
            using(new EditorGUILayout.HorizontalScope())
            {
                MacaronGUI.Chip(PcssText.F("Macaron {0}", mats.Count), MacaronGUI.Neutral, 96f);
                MacaronGUI.Chip(PcssText.F("PCSS {0}", pcssCount), pcssCount > 0 ? MacaronGUI.Mint : MacaronGUI.Neutral, 84f);
                GUILayout.FlexibleSpace();
            }
            if(mats.Count == 0)
            {
                EditorGUILayout.HelpBox(PcssText.S("這個角色沒有 Macaron 材質。請先用 Macaron 的轉換工具轉換。"), MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }
            GUILayout.Space(4f);
            LilMCRPcssSetup.PreviewButton(avatar);

            GUILayout.Space(10f);

            // 2. Apply
            MacaronGUI.Step(2, PcssText.S("套用"));
            EditorGUILayout.LabelField(PcssText.S("加入專用聚光燈（極弱的光，對其他物體和玩家看不出來）並把材質切換成 PCSS 版本。增加 1 盞燈（Performance Rank）。"), EditorStyles.wordWrappedMiniLabel);
            selfOnly = LilMCRPcssSharedShadow.Draw(avatar, selfOnly);
            if(MacaronGUI.BigButton(PcssText.S("套用"), 36f)) RunApply();
            if(LilMCRPcssSetup.FindLight(avatar) != null && GUILayout.Button(PcssText.S("開啟統一調整"))) LilMCRPcssBatch.Open(avatar);
            if(!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
            EditorGUILayout.LabelField(PcssText.S("注意：重新執行 Macaron 的轉換後，材質會回到原本的 Macaron 著色器，請再按一次套用。"), EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(10f);

            // 3. Remove (back to plain Macaron)
            MacaronGUI.Step(3, PcssText.S("移除（還原）"));
            bool installed = pcssCount > 0 || LilMCRPcssSetup.FindLight(avatar) != null;
            EditorGUILayout.LabelField(PcssText.S("材質切回原本的 Macaron，並刪除專用光源。可用 Ctrl+Z 復原。"), EditorStyles.wordWrappedMiniLabel);
            deleteFiles = EditorGUILayout.ToggleLeft(PcssText.S("同時刪除這個角色產生的檔案（cookie 等，無法復原）"), deleteFiles);
            using(new EditorGUI.DisabledScope(!installed))
            {
                if(GUILayout.Button(PcssText.S("移除 lilMCR PCSS"), GUILayout.Height(26f))
                && EditorUtility.DisplayDialog("lilMCR PCSS", PcssText.F("從「{0}」移除 lilMCR PCSS？", avatar.name), PcssText.S("移除"), PcssText.S("取消")))
                {
                    RunRemove();
                }
            }

            GUILayout.Space(10f);
            DrawShaderInfo();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawShaderInfo()
        {
            MacaronGUI.Step(4, PcssText.S("著色器"));
            string src = LilMCRPcssGenerator.SourceFolder;
            EditorGUILayout.LabelField(PcssText.F("Macaron 來源：{0}", src ?? "-"), EditorStyles.wordWrappedMiniLabel);
            if(!string.IsNullOrEmpty(LilMCRPcssGenerator.LastMessage))
                EditorGUILayout.LabelField(LilMCRPcssGenerator.LastMessage, EditorStyles.wordWrappedMiniLabel);
            if(GUILayout.Button(PcssText.S("重新產生著色器"), EditorStyles.miniButton)) LilMCRPcssGenerator.Regenerate(true);
            EditorGUILayout.LabelField(PcssText.S("通常不需要按：匯入新版 lilMCR 時會自動重新產生 PCSS 版著色器。只有更新後影子消失、材質變成粉紅色，或 Console 出現 lilMCR PCSS 的警告時才需要手動按一次。"), EditorStyles.wordWrappedMiniLabel);
        }

        private void RunApply()
        {
            if(!LilMCRPcssGenerator.LastOk) LilMCRPcssGenerator.Regenerate(false);
            LilMCRPcssSetup.Result r = LilMCRPcssSetup.Apply(avatar, selfOnly);
            message = PcssText.F("已套用 lilMCR PCSS：{0} 個材質，光源 ID {1}。", r.Materials, r.LightId);
            messageType = MessageType.Info;
            if(r.Missing.Count > 0)
            {
                message += "\n" + PcssText.F("找不到 PCSS 版本的著色器：{0}。請按「重新產生著色器」。", string.Join(", ", r.Missing.ToArray()));
                messageType = MessageType.Warning;
            }
            if(r.SharedWith.Count > 0)
            {
                message += "\n" + PcssText.F("這些材質也被其他物件使用，它們會一起變成 PCSS 版本：{0}。若只想改這個角色，請先讓它使用自己的材質副本。", string.Join(", ", r.SharedWith.ToArray()));
                messageType = MessageType.Warning;
            }
        }

        private void RunRemove()
        {
            int n = LilMCRPcssSetup.Remove(avatar, deleteFiles);
            message = PcssText.F("已移除 lilMCR PCSS：{0} 個材質切回 Macaron。", n);
            messageType = MessageType.Info;
        }
    }
}
#endif
