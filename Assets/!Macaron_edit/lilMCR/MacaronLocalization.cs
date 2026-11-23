#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace liltoon_MCR
{
    //==============================================================================================
    // UI language: Traditional Chinese (source text) / English / Japanese / Korean.
    //
    // Every UI string in the tools is written in Traditional Chinese and passed through ML.T / ML.F;
    // the table below maps it to English, Japanese and Korean. A string missing from the table is shown in
    // Chinese (nothing breaks).
    //
    // "Auto" (the default) follows lilToon's language setting (the language selector at the top of
    // the lilToon Inspector): ja -> Japanese, ko -> Korean, zh -> Traditional Chinese, everything else -> English.
    // The choice is stored per user in EditorPrefs ("Macaron.Language").
    //==============================================================================================
    public static class ML
    {
        public enum Language { Auto = 0, ChineseTraditional = 1, English = 2, Japanese = 3, Korean = 4 }

        private const string PrefKey = "Macaron.Language";

        public static Language Setting
        {
            get
            {
                int v = EditorPrefs.GetInt(PrefKey, 0);
                return v >= 0 && v <= 4 ? (Language)v : Language.Auto;
            }
            set { EditorPrefs.SetInt(PrefKey, (int)value); }
        }

        /// <summary>The language the UI is drawn in now (never Auto).</summary>
        public static Language Current
        {
            get
            {
                Language s = Setting;
                return s == Language.Auto ? FromLilToon() : s;
            }
        }

        // lilToon's setting is read at most twice a second (it is a ScriptableSingleton, cheap, but called per label).
        private static double lilCacheTime = -1.0;
        private static Language lilCacheLang = Language.English;

        public static Language FromLilToon()
        {
            double now = EditorApplication.timeSinceStartup;
            if(lilCacheTime >= 0.0 && now - lilCacheTime < 0.5 && now >= lilCacheTime) return lilCacheLang;
            string code = null;
            try
            {
                code = global::lilToon.lilLanguageManager.langSet.languageName;
            }
            catch(Exception)
            {
                code = null;
            }
            lilCacheLang = FromCode(code);
            lilCacheTime = now;
            return lilCacheLang;
        }

        /// <summary>"ja-JP" -> Japanese, "ko-KR" -> Korean, "zh-Hant" / "zh-Hans" / "zh-TW" ... -> Traditional Chinese, others -> English.</summary>
        public static Language FromCode(string code)
        {
            if(string.IsNullOrEmpty(code)) return Language.English;
            string c = code.Trim().ToLowerInvariant();
            if(c.StartsWith("ja", StringComparison.Ordinal)) return Language.Japanese;
            if(c.StartsWith("ko", StringComparison.Ordinal)) return Language.Korean;
            if(c.StartsWith("zh", StringComparison.Ordinal)) return Language.ChineseTraditional;
            return Language.English;
        }

        //------------------------------------------------------------------------------------------

        /// <summary>Translates a Traditional Chinese UI string. null stays null.</summary>
        public static string T(string zh)
        {
            if(zh == null) return null;
            Language lang = Current;
            if(lang == Language.ChineseTraditional) return zh;
            string[] e;
            if(!Map.TryGetValue(zh, out e)) return zh;
            string s = lang == Language.Japanese ? e[2] : (lang == Language.Korean ? (e.Length > 3 ? e[3] : e[1]) : e[1]);
            return string.IsNullOrEmpty(s) ? zh : s;
        }

        /// <summary>T + string.Format. The key contains {0}, {1}...</summary>
        public static string F(string zhFormat, params object[] args)
        {
            string format = T(zhFormat);
            try { return string.Format(format, args); }
            catch(FormatException) { return string.Format(zhFormat, args); }
        }

        public static string[] TA(string[] zh)
        {
            if(zh == null) return null;
            string[] r = new string[zh.Length];
            for(int i = 0; i < zh.Length; i++) r[i] = T(zh[i]);
            return r;
        }

        //------------------------------------------------------------------------------------------
        // Selectors

        private static readonly string[] FixedNames = { null, "繁體中文", "English", "日本語", "한국어" };

        private static string AutoName(Language shown)
        {
            switch(shown)
            {
                case Language.Japanese: return "自動（lilToon に合わせる）";
                case Language.English: return "Auto (follow lilToon)";
                case Language.Korean: return "자동 (lilToon 설정 따름)";
                default: return "自動（跟隨 lilToon）";
            }
        }

        private static string[] PopupNames()
        {
            return new[] { AutoName(Current), FixedNames[1], FixedNames[2], FixedNames[3], FixedNames[4] };
        }

        /// <summary>A small right-aligned "Language" popup. Returns true when the user changed it.</summary>
        public static bool LanguagePopup()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Language", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            int old = (int)Setting;
            int sel = EditorGUILayout.Popup(old, PopupNames(), GUILayout.Width(190f));
            EditorGUILayout.EndHorizontal();
            if(sel == old) return false;
            SetAndRepaint((Language)sel);
            return true;
        }

        /// <summary>Adds "prefix + language" items (with a check mark) to a context menu.</summary>
        public static void AddLanguageMenu(GenericMenu menu, string prefix)
        {
            string[] names = PopupNames();
            Language current = Setting;
            for(int i = 0; i < names.Length; i++)
            {
                Language l = (Language)i;
                menu.AddItem(new GUIContent(prefix + names[i]), current == l, () => SetAndRepaint(l));
            }
        }

        private static void SetAndRepaint(Language l)
        {
            Setting = l;
            lilCacheTime = -1.0;
            InternalEditorUtility.RepaintAllViews();
        }

        //------------------------------------------------------------------------------------------
        // Table: { Traditional Chinese (key), English, Japanese, Korean }

        private static Dictionary<string, string[]> map;
        private static Dictionary<string, string[]> Map
        {
            get
            {
                if(map != null) return map;
                Dictionary<string, string[]> m = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach(string[] e in Table)
                {
                    if(e == null || e.Length < 3) continue;
                    m[e[0]] = e;
                }
                map = m;
                return map;
            }
        }

        private static readonly string[][] Table =
        {
            new[] { "方向來源", "Direction source", "方向の基準", "방향 소스" },
            new[] { "lilMCR 預設光向（45°）", "lilMCR default light direction (45°)", "lilMCR の初期ライト方向（45°）", "lilMCR 기본 광원 방향 (45°)" },
            new[] { "沿用材質接觸陰影方向", "Use material contact-shadow direction", "マテリアルのコンタクトシャドウ方向を使用", "머티리얼의 접촉 그림자 방향 사용" },
            new[] { "建立 / 更新", "Create / update", "作成・更新", "생성 / 업데이트" },
            new[] { "世界落影已建立，使用選定方向。", "World shadow created using the selected direction.", "選択した方向でワールド影を作成しました。", "선택한 방향으로 월드 그림자를 생성했습니다." },
            new[] { "預設使用 lilMCR 光照方向，可改為沿用材質方向。PC 支援本地與遠端投影。", "Uses the lilMCR default light direction, with an option to inherit material direction. PC local and remote projection supported.", "初期設定は lilMCR のライト方向です。マテリアルの方向にも切り替えられます。PC のローカル・リモート投影に対応します。", "lilMCR 기본 광원 방향을 사용하며, 머티리얼 방향을 따르도록 바꿀 수 있습니다. PC에서 로컬 및 원격 투영을 지원합니다." },
            new[] { " 舊版模組請執行建立 / 更新，以換成支援遠端顯示的角色識別捕捉與選單。", " For older modules, use Create / update to install owner-tagged capture and menus that support remote display.", " 旧版のモジュールは「作成・更新」でリモート表示に対応した識別付き撮影とメニューに更新してください。", " 이전 버전 모듈은 생성 / 업데이트를 실행해 원격 표시를 지원하는 아바타 식별 캡처와 메뉴로 교체하세요." },
            new[] { "遠端顯示需要對方保留你的攝影機（好友或 Show Avatar）。請使用更新後的 lilMCR／PCSS；其他角色擋住捕捉視線時，影子可能有缺口。", "Remote display requires your camera to be retained on the viewer's client (friends or Show Avatar). Use updated lilMCR/PCSS; another avatar blocking the capture view can leave gaps in your shadow.", "リモート表示には、相手側でカメラが保持される必要があります（フレンドまたは Show Avatar）。更新済みの lilMCR／PCSS を使用してください。他のアバターが撮影を遮ると、影が一部欠ける場合があります。", "원격 표시는 상대가 당신의 카메라를 유지하고 있어야 합니다 (친구 또는 Show Avatar). 업데이트된 lilMCR/PCSS를 사용하세요. 다른 아바타가 캡처 시야를 가리면 그림자에 빈 곳이 생길 수 있습니다." },
            new[] { "遠端世界陰影需要更新後的 lilMCR 或 lilMCR PCSS 材質，請先轉換角色材質。", "Remote world shadows require updated lilMCR or lilMCR PCSS materials. Convert the avatar materials first.", "リモートのワールド影には更新済みの lilMCR または lilMCR PCSS マテリアルが必要です。先にアバターのマテリアルを変換してください。", "원격 월드 그림자에는 업데이트된 lilMCR 또는 lilMCR PCSS 머티리얼이 필요합니다. 먼저 아바타 머티리얼을 변환하세요." },
            new[] { "世界落影需要 lilMCR PCSS。請先匯入 lilMCR_PCSS 資料夾。", "World Shadow requires lilMCR PCSS. Import the lilMCR_PCSS folder first.", "ワールド影には lilMCR PCSS が必要です。先に lilMCR_PCSS フォルダーをインポートしてください。", "월드 그림자에는 lilMCR PCSS가 필요합니다. 먼저 lilMCR_PCSS 폴더를 임포트하세요." },
            new[] { "此角色尚未套用 lilMCR PCSS。請先用 lilMCR PCSS 設定套用後，再建立世界落影。", "lilMCR PCSS is not applied to this avatar. Apply it in lilMCR PCSS Setup before creating World Shadow.", "このアバターには lilMCR PCSS が適用されていません。lilMCR PCSS の設定で適用してから、ワールド影を作成してください。", "이 아바타에는 아직 lilMCR PCSS가 적용되지 않았습니다. lilMCR PCSS 설정에서 적용한 뒤 월드 그림자를 생성하세요." },
            new[] { "開啟 lilMCR PCSS 設定", "Open lilMCR PCSS Setup", "lilMCR PCSS の設定を開く", "lilMCR PCSS 설정 열기" },
            new[] { "目前裝置不支援世界陰影所需的浮點數貼圖。", "This device does not support the floating-point texture format required for world shadows.", "このデバイスはワールド影に必要な浮動小数点テクスチャ形式に対応していません。", "현재 기기는 월드 그림자에 필요한 부동소수점 텍스처를 지원하지 않습니다." },
            new[] { " 以下材質不支援角色識別，不會作為世界陰影來源：{0}。", " These materials do not support owner identification and will not cast world shadows: {0}.", " 次のマテリアルはアバター識別に非対応のため、ワールド影の生成元にはなりません：{0}。", " 다음 머티리얼은 아바타 식별을 지원하지 않아 월드 그림자를 드리우지 않습니다: {0}." },
            new[] { "不產生陰影遮罩", "No-shadow casting mask", "影を生成しないマスク", "그림자 생성 제외 마스크" },
            new[] { "不產生陰影遮罩強度", "No-shadow mask strength", "影を生成しないマスクの強度", "그림자 생성 제외 마스크 강도" },
            new[] { "黑色不產生陰影；白色正常投影。保留角色本身外觀，與接收遮罩分開。世界陰影須建立 / 更新一次。", "Black areas do not cast shadows; white areas cast normally. Avatar appearance is preserved. This is separate from the receiving mask. Create/update World Shadow once to enable it.", "黒い部分は影を生成せず、白い部分は通常どおり投影します。アバターの見た目は変わりません。影を受けるマスクとは別です。ワールド影を一度作成・更新してください。", "검은 부분은 그림자를 드리우지 않고, 흰 부분은 정상적으로 드리웁니다. 아바타 외형은 유지되며 받는 쪽 마스크와는 별개입니다. 월드 그림자를 한 번 생성 / 업데이트해야 적용됩니다." },
            new[] { "Macaron 世界落影", "Macaron World Shadow", "Macaron ワールド影", "Macaron 월드 그림자" },
            new[] { "世界落影", "World Shadow", "ワールド影", "월드 그림자" },
            new[] { "世界落影設定", "World shadow settings", "ワールド影の設定", "월드 그림자 설정" },
            new[] { "強度", "Strength", "強度", "강도" },
            new[] { "柔度", "Softness", "ぼかし", "부드러움" },
            new[] { "陰影透射色", "Shadow transmission color", "影の透過色", "그림자 투과색" },
            new[] { "輕量 512", "Low 512", "軽量 512", "가벼움 512" },
            new[] { "標準 1024", "Standard 1024", "標準 1024", "표준 1024" },
            new[] { "精細 2048", "High 2048", "高精細 2048", "정밀 2048" },
            new[] { "遊戲內選單可調整固定／跟隨角色、360° 方向與陰影長短。變更語言後請重新建立／更新，套用遊戲內選單文字。", "The in-game menu controls fixed/follow mode, 360° direction and shadow length. After changing language, create/update again to apply the in-game menu labels.", "ゲーム内メニューで固定・アバター追従、360° の方向、影の長さを調整できます。言語変更後は、再度作成・更新してゲーム内メニューの表示に反映してください。", "게임 내 메뉴에서 고정/아바타 추종, 360° 방향, 그림자 길이를 조절할 수 있습니다. 언어를 바꾼 뒤에는 다시 생성 / 업데이트해야 게임 내 메뉴 문구가 바뀝니다." },
            new[] { "建立與維護", "Create and maintain", "作成とメンテナンス", "생성 및 관리" },
            new[] { "修復角色接收 / 共用深度光（保留投影設定）", "Repair receivers / shared depth light (keep settings)", "影の受け取り・共通深度ライトを修復（設定を保持）", "아바타 수신 / 공유 깊이 라이트 복구 (투영 설정 유지)" },
            new[] { "移除世界落影", "Remove world shadow", "ワールド影を削除", "월드 그림자 제거" },
            new[] { "已移除場景物件，產生的資產保留。", "Scene object removed. Generated assets are retained.", "シーン内のオブジェクトを削除しました。生成済みのアセットは保持します。", "씬 오브젝트를 제거했습니다. 생성된 에셋은 남아 있습니다." },
            new[] { "角色尚未建立世界落影，請使用建立 / 更新。", "This avatar has no world shadow yet. Use Create / update.", "このアバターにはワールド影がありません。「作成・更新」を使用してください。", "이 아바타에는 아직 월드 그림자가 없습니다. 생성 / 업데이트를 사용하세요." },
            new[] { "已修復接收設定並統一深度方向光；保留原本的投影方向、品質與顏色。", "Receiver settings and the shared depth light were repaired. Projection direction, quality and color were preserved.", "影の受け取り設定と共通深度ライトを修復しました。投影方向・品質・色は保持しています。", "수신 설정을 복구하고 공유 깊이 라이트를 통일했습니다. 기존 투영 방향, 품질, 색상은 유지됩니다." },
            new[] { "角色材質的陰影方向不一致。請先用角色轉換器統一方向。", "Avatar materials have different shadow directions. Unify them in Avatar Converter first.", "アバターのマテリアル間で影の方向が異なります。先にアバター変換で方向を統一してください。", "아바타 머티리얼의 그림자 방향이 서로 다릅니다. 먼저 아바타 변환기에서 방향을 통일하세요." },
            new[] { "請選擇場景角色。", "Select an avatar in the scene.", "シーン内のアバターを選択してください。", "씬의 아바타를 선택하세요." },
            new[] { "世界落影不支援觀看相機座標。請在轉換器選擇跟隨角色或世界座標，再建立落影。", "World Shadow does not support view-camera space. Select avatar or world space in the converter before creating it.", "ワールド影はカメラ座標に対応していません。変換ツールでアバター追従またはワールド座標を選択してから作成してください。", "월드 그림자는 카메라 기준 좌표를 지원하지 않습니다. 변환기에서 아바타 추종 또는 월드 좌표를 선택한 뒤 생성하세요." },
            new[] { "找不到 WorldShadow Shader。", "WorldShadow shader was not found.", "WorldShadow シェーダーが見つかりません。", "WorldShadow 셰이더를 찾을 수 없습니다." },
            new[] { "世界落影需要角色根物件使用正值等比例縮放。", "World Shadow requires a positive, uniform scale on the avatar root.", "ワールド影には、アバターのルートを正の値で均等にスケールする必要があります。", "월드 그림자는 아바타 루트가 양수의 균일한 스케일이어야 합니다." },
            new[] { " 已排除角色接收世界落影。", " The avatar is excluded from receiving world shadows.", " アバターへのワールド影の重ね描きを除外しました。", " 아바타가 월드 그림자를 받지 않도록 제외했습니다." },
            new[] { " 以下材質未能自動排除（不支援或已有自訂 Stencil）：{0}。這些部位仍可能收到投影；動畫切換材質亦須使用已排除版本。", " These materials could not be excluded automatically (unsupported or custom stencil): {0}. They may still receive projection; material-swap animations must also use excluded copies.", " 次のマテリアルは自動除外できません（非対応または独自の Stencil）：{0}。これらの部位には影が投影される可能性があります。マテリアル切替アニメーションにも除外済みのコピーを使用してください。", " 다음 머티리얼은 자동으로 제외하지 못했습니다 (미지원 또는 사용자 지정 스텐실): {0}. 이 부분에는 여전히 투영이 생길 수 있으며, 머티리얼 교체 애니메이션도 제외된 복사본을 사용해야 합니다." },
            new[] { " 未安裝 Modular Avatar：僅建立投影，沒有遊戲內開關。", " Modular Avatar is not installed: projection only, without in-game controls.", " Modular Avatar が未導入のため、投影のみを作成しました。ゲーム内操作はありません。", " Modular Avatar가 설치되지 않음: 투영만 생성하며 게임 내 스위치는 없습니다." },
            new[] { "開關", "On / off", "オン・オフ", "켜기 / 끄기" },
            new[] { "方向（360°）", "Direction (360°)", "方向（360°）", "방향 (360°)" },
            new[] { "長短與傾角", "Length / tilt", "長さ・傾き", "길이 / 기울기" },
            new[] { "固定方向", "Fix direction", "方向を固定", "방향 고정" },
            new[] { " 已加入世界落影選單開關。", " World shadow menu controls added.", " ワールド影のメニュー操作を追加しました。", " 월드 그림자 메뉴 스위치를 추가했습니다." },
            new[] { " 未安裝 VRChat SDK：僅建立投影，沒有遊戲內開關。", " VRChat SDK is not installed: projection only, without in-game controls.", " VRChat SDK が未導入のため、投影のみを作成しました。ゲーム内操作はありません。", " VRChat SDK가 설치되지 않음: 투영만 생성하며 게임 내 스위치는 없습니다." },
            new[] { "轉換時統一最低亮度 0.06、最高亮度 1；Fur 材質關閉 AO 與接觸陰影。", "Conversion sets minimum brightness to 0.06 and maximum brightness to 1. AO and contact shadows are disabled for Fur materials.", "変換時に最低明るさを 0.06、最高明るさを 1 に統一します。Fur マテリアルでは AO とコンタクトシャドウを無効にします。", "변환 시 최소 밝기 0.06, 최대 밝기 1로 통일하고, Fur 머티리얼은 AO와 접촉 그림자를 끕니다." },
            new[] { "遮罩生成器（選用）", "Mask generator (optional)", "マスク生成（任意）", "마스크 생성기 (선택)" },
            new[] { "開啟遮罩生成器", "Open mask generator", "マスク生成ツールを開く", "마스크 생성기 열기" },
            new[] { "轉換後可選取 UV 網格，生成遮罩並自動套用。", "After conversion, select UV faces to generate and automatically apply a mask.", "変換後、UV 面を選択してマスクを生成し、自動適用できます。", "변환 후 UV 면을 선택해 마스크를 만들고 자동으로 적용할 수 있습니다." },
            new[] { "請匯入獨立的 lilMCR_MaskGenerator 資料夾以使用此功能。", "Import the separate lilMCR_MaskGenerator folder to use this feature.", "この機能を使うには、別フォルダーの lilMCR_MaskGenerator をインポートしてください。", "이 기능을 사용하려면 별도의 lilMCR_MaskGenerator 폴더를 임포트하세요." },
            new[] { "額外操作（選用）", "Additional actions (optional)", "その他の操作（任意）", "추가 작업 (선택)" },
            // ---- Shared -------------------------------------------------------------------------
            new[] { "重置", "Reset", "リセット", "초기화" },
            new[] { "進階設定", "Advanced", "詳細設定", "고급 설정" },
            new[] { "取消", "Cancel", "キャンセル", "취소" },
            new[] { "套用", "Apply", "適用", "적용" },
            new[] { "選擇角色", "Select the avatar", "アバターを選択", "아바타 선택" },
            new[] { "角色", "Avatar", "アバター", "아바타" },
            new[] { "資料夾", "Folder", "フォルダ", "폴더" },
            new[] { "整個專案", "Whole project", "プロジェクト全体", "프로젝트 전체" },
            new[] { "啟用", "Enable", "有効", "사용" },
            new[] { "請放入場景（Hierarchy）中的角色，而不是 Project 裡的 Prefab。", "Drop the avatar from the scene (Hierarchy), not a Prefab from the Project window.", "Project のプレハブではなく、シーン（Hierarchy）上のアバターを入れてください。", "Project 창의 Prefab이 아니라 씬(Hierarchy)에 있는 아바타를 넣으세요." },
            new[] { "找不到 Macaron 著色器（{0}）。", "Macaron shader not found ({0}).", "Macaron シェーダーが見つかりません（{0}）。", "Macaron 셰이더를 찾을 수 없습니다 ({0})." },

            // ---- Direction ----------------------------------------------------------------------
            new[] { "跟隨角色", "Follow avatar", "アバター基準", "아바타 추종" },
            new[] { "跟隨鏡頭", "Follow camera", "カメラ基準", "카메라 추종" },
            new[] { "世界座標", "World", "ワールド座標", "월드 좌표" },
            new[] { "從角色正面看的方向。角色轉身、走動時，影子方向跟著角色一起轉，不受世界光源影響。", "Direction as seen from the avatar's front. The shadow turns with the avatar when it turns or walks, and does not depend on the world's lights.", "アバターを正面から見た向きです。アバターが振り向いたり歩いたりしても影の向きは一緒に回り、ワールドのライトには影響されません。", "아바타 정면에서 본 방향입니다. 아바타가 돌거나 걸어도 그림자 방향이 함께 돌며, 월드 광원의 영향을 받지 않습니다." },
            new[] { "以畫面為基準：不論從哪個角度看，影子都落在畫面上的同一個方向。", "Screen based: the shadow always falls to the same side of the screen, from any viewing angle.", "画面基準：どの角度から見ても、影は画面上の同じ方向に落ちます。", "화면 기준: 어느 각도에서 보든 그림자가 화면의 같은 쪽으로 떨어집니다." },
            new[] { "以世界座標 X / Y / Z 為基準（前後 = Z 軸）。", "Based on the world X / Y / Z axes (front / back = Z axis).", "ワールド座標 X / Y / Z 基準（前後 = Z 軸）。", "월드 X / Y / Z 축 기준 (앞뒤 = Z 축)." },
            new[] { "光源方向", "Light direction", "光源の向き", "광원 방향" },
            new[] { "點擊或拖曳圓盤設定光從哪裡照過來，影子會落在相反方向。", "Click or drag on the disc to set where the light comes from. The shadow falls on the opposite side.", "円をクリック／ドラッグして光の来る方向を設定します。影は反対側に落ちます。", "원판을 클릭하거나 드래그해 빛이 오는 방향을 정합니다. 그림자는 반대쪽에 생깁니다." },
            new[] { "點擊或拖曳圓盤設定光從哪裡照過來。影子會落在相反方向。", "Click or drag on the disc to set where the light comes from. The shadow falls on the opposite side.", "円をクリック／ドラッグして光の来る方向を設定します。影は反対側に落ちます。", "원판을 클릭하거나 드래그해 빛이 오는 방향을 정합니다. 그림자는 반대쪽에 생깁니다." },
            new[] { "光在前方", "Light in front", "光は前方", "빛이 앞쪽" },
            new[] { "光在後方", "Light behind", "光は後方", "빛이 뒤쪽" },
            new[] { "● 光在前方", "● Light in front", "● 光は前方", "● 빛이 앞쪽" },
            new[] { "○ 光在後方", "○ Light behind", "○ 光は後方", "○ 빛이 뒤쪽" },
            new[] { "（多個材質的方向不同）", "(the selected materials have different directions)", "（複数のマテリアルで向きが異なります）", "(선택한 머티리얼들의 방향이 서로 다름)" },
            new[] { "以「從正面看角色」時的左右為準。", "Left / right as seen when looking at the avatar from the front.", "「アバターを正面から見たとき」の左右が基準です。", "아바타를 정면에서 볼 때의 좌우 기준입니다." },
            new[] { "以畫面的上下左右為準。", "Up / down / left / right of the screen.", "画面の上下左右が基準です。", "화면의 상하좌우 기준입니다." },
            new[] { "圓盤 = 世界 X / Y，前後 = 世界 Z。", "Disc = world X / Y, front / back = world Z.", "円 = ワールド X / Y、前後 = ワールド Z。", "원판 = 월드 X / Y, 앞뒤 = 월드 Z." },
            new[] { "恢復預設方向", "Default direction", "既定の向きに戻す", "기본 방향으로" },
            new[] { "角色的正面方向由轉換工具自動寫入。改變了角色或骨架的方向時，請從右上角選單執行「重新偵測角色方向」。", "The avatar's front axis is written by the converter. After rotating the avatar or its armature, run \"Re-detect avatar direction\" from the menu at the top right.", "アバターの正面方向は変換ツールが自動で書き込みます。アバターやアーマチュアの向きを変えたときは、右上のメニューから「アバターの向きを再検出」を実行してください。", "아바타의 정면 방향은 변환 도구가 자동으로 기록합니다. 아바타나 아마추어의 방향을 바꿨다면 오른쪽 위 메뉴에서 \"아바타 방향 다시 감지\"를 실행하세요." },

            // ---- Sections: basic ----------------------------------------------------------------
            new[] { "基本設定", "Basic", "基本設定", "기본 설정" },
            new[] { "整體強度", "Overall strength", "全体の強さ", "전체 강도" },
            new[] { "僅自己可見", "Local only", "自分だけに表示", "나에게만 표시" },
            new[] { "開啟後，lilMCR 的所有效果（遮蔽、接觸陰影、埋入、光照調節、世界陰影、PCSS）只在你自己的畫面顯示；其他玩家看到的是沒有 lilMCR 效果的原版 lilToon。需要光照調節器（判斷是不是自己）；沒有光照調節器時連自己也看不到效果。你自己的鏡子與相機會拍到效果。也可以在光照調節器勾選「僅自己可見」加入選單，在遊戲中切換（預設不加入）。", "On: every lilMCR effect (occlusion, contact shadow, Uzumore, Light Adjuster, World Shadow, PCSS) is shown only on your own screen; other players see plain lilToon without lilMCR. Needs the Light Adjuster (it tells whether the viewer is you); without it you do not see the effect either. Your own mirrors and cameras show it. You can also tick \"Local only\" in the Light Adjuster items to switch it from the avatar menu in game (not added by default).", "オンにすると、lilMCR のすべての効果（遮蔽、接触影、埋もれ、ライト調整、ワールド影、PCSS）が自分の画面にだけ表示され、他のプレイヤーには lilMCR の効果がない通常の lilToon として表示されます。ライト調整が必要です（自分かどうかを判定）。ライト調整がない場合は自分にも効果が表示されません。自分のミラーやカメラには効果が写ります。ライト調整の項目で「自分だけに表示」にチェックすると、ゲーム内のメニューから切り替えられます（既定では追加しません）。", "켜면 lilMCR의 모든 효과(차폐, 접촉 그림자, 파묻힘, 라이트 조절, 월드 그림자, PCSS)가 내 화면에만 표시되고, 다른 플레이어에게는 lilMCR 효과가 없는 일반 lilToon으로 보입니다. 라이트 조절기가 필요합니다 (나인지 판단). 없으면 나에게도 효과가 보이지 않습니다. 내 거울과 카메라에는 효과가 찍힙니다. 라이트 조절기 항목에서 \"나에게만 표시\"를 체크하면 게임 안 메뉴에서 전환할 수 있습니다 (기본으로는 추가하지 않음)." },
            new[] { "以平滑法線判斷遮蔽，消除沿著多邊形出現的一格一格陰影。通常保持 1。", "Uses smoothed normals for occlusion, removing faceted shading along polygons. Usually keep at 1.", "平滑化した法線で遮蔽を判定し、ポリゴンに沿ったカクカクした影を消します。通常は 1 のままにします。", "매끄러운 노멀로 차폐를 판정해 폴리곤을 따라 생기는 계단 모양 그림자를 없앱니다. 보통 1로 둡니다." },
            new[] { "整體遮罩", "Global mask", "全体マスク", "전체 마스크" },
            new[] { "白 = 有效果、黑 = 無效果（R 通道）。", "White = effect, black = no effect (R channel).", "白 = 効果あり、黒 = 効果なし（R チャンネル）。", "흰색 = 효과 있음, 검은색 = 효과 없음 (R 채널)." },
            new[] { "整體遮罩強度", "Global mask strength", "全体マスクの強さ", "전체 마스크 강도" },

            // ---- Sections: AO -------------------------------------------------------------------
            new[] { "方式", "Method", "方式", "방식" },
            new[] { "SSAO（輕量）", "SSAO (light)", "SSAO（軽量）", "SSAO (가벼움)" },
            new[] { "光線步進 Ray March（高負擔）", "Ray March (heavy)", "レイマーチ Ray March（高負荷）", "레이 마칭 Ray March (부하 높음)" },
            new[] { "Ray March（DX11）：標準 = 相鄰 2×2 像素各打約一半光線再合併，輕量 = 各約 1/4；輪廓、深度落差、折角處自動完整採樣。標準（無 2×2）= 標準的採樣數但每個像素自己完整計算，不會因合併而模糊。精細、最高不分攤。", "Ray March (DX11): Standard = neighboring 2x2 pixels each march about half the rays and combine them, Light = about 1/4 each; silhouettes, depth steps and creases use full sampling automatically. Standard (no 2x2) = the Standard sample count, but every pixel marches all of it itself, so nothing is blurred by the combining. Fine and Best do not share.", "Ray March（DX11）：標準 = 隣接する 2×2 ピクセルがそれぞれ約半分のレイを処理して合成、軽量 = 約 1/4。輪郭・深度の段差・折れ目では自動的に通常のサンプリング。標準（2×2 なし）= 標準と同じサンプル数を各ピクセルがすべて自分で処理し、合成によるぼけがありません。精細・最高は分担しません。", "Ray March (DX11): 표준 = 인접한 2×2 픽셀이 각각 약 절반의 광선을 처리해 합성, 가벼움 = 각각 약 1/4. 윤곽, 깊이 차이, 꺾인 부분은 자동으로 전체 샘플링합니다. 표준 (2×2 없음) = 표준과 같은 샘플 수를 각 픽셀이 모두 직접 처리해 합성으로 인한 흐림이 없습니다. 정밀, 최고는 나누지 않습니다." },
            new[] { "高品質", "High", "高品質", "고품질" },
            new[] { "最輕量", "Lightest", "最軽量", "최경량" },
            new[] { "預估成本：角色像素 +40～70%（對比 lilMCR 全關）。", "Estimated cost: +40-70% on the avatar's pixels (vs. lilMCR all off).", "推定コスト：アバターのピクセル +40-70%（lilMCR 全オフ比）。", "예상 비용: 아바타 픽셀 +40-70% (lilMCR 전부 끔 대비)." },
            new[] { "預估成本：角色像素 +60～100%（對比 lilMCR 全關）。", "Estimated cost: +60-100% on the avatar's pixels (vs. lilMCR all off).", "推定コスト：アバターのピクセル +60-100%（lilMCR 全オフ比）。", "예상 비용: 아바타 픽셀 +60-100% (lilMCR 전부 끔 대비)." },
            new[] { "預估成本：角色像素 +80～140%（對比 lilMCR 全關）。", "Estimated cost: +80-140% on the avatar's pixels (vs. lilMCR all off).", "推定コスト：アバターのピクセル +80-140%（lilMCR 全オフ比）。", "예상 비용: 아바타 픽셀 +80-140% (lilMCR 전부 끔 대비)." },
            new[] { "預估成本：角色像素 +90～150%（對比 lilMCR 全關）。", "Estimated cost: +90-150% on the avatar's pixels (vs. lilMCR all off).", "推定コスト：アバターのピクセル +90-150%（lilMCR 全オフ比）。", "예상 비용: 아바타 픽셀 +90-150% (lilMCR 전부 끔 대비)." },
            new[] { "Overkill", "Overkill", "Overkill", "Overkill" },
            new[] { "預估成本：角色像素 +280～500%（對比 lilMCR 全關），PCSS 陰影貼圖另佔約 256 MB 顯示記憶體。", "Estimated cost: +280-500% on the avatar's pixels (vs. lilMCR all off); the PCSS shadow map also takes about 256 MB of video memory.", "推定コスト：アバターのピクセル +280-500%（lilMCR 全オフ比）、PCSS シャドウマップがさらに約 256 MB のビデオメモリを使用。", "예상 비용: 아바타 픽셀 +280-500% (lilMCR 전부 끔 대비), PCSS 그림자 맵이 추가로 약 256 MB의 비디오 메모리 사용." },
            new[] { "品質檔次", "Quality tier", "品質プリセット", "품질 프리셋" },
            new[] { "一鍵套用品質檔次", "One-click quality tier", "品質プリセットを一括適用", "품질 프리셋 한 번에 적용" },
            new[] { "依畫面大小調整品質", "Adapt quality to screen size", "画面サイズに応じて品質を調整", "화면 크기에 맞춰 품질 조정" },
            new[] { "減少看不出差別的採樣：Ray March 範圍在畫面上很小時減少採樣、近看時限制範圍在畫面上的大小。關閉可保留原本的品質與範圍。", "Skips samples you cannot see: fewer Ray March samples when the reach is small on screen, and a limited on-screen reach up close. Turn off to keep the full quality and reach.", "見た目に出ないサンプルを減らします：Ray March の範囲が画面上で小さいときのサンプル削減、近くで見るときの画面上の範囲制限。オフにすると元の品質と範囲のままになります。", "보이지 않는 샘플을 줄입니다: Ray March 범위가 화면에서 작을 때 샘플 감소, 가까이서 볼 때 화면상 범위 제한. 끄면 원래 품질과 범위를 유지합니다." },
            new[] { "近看時限制影子在畫面上的長度以降低負擔。關閉可保留原本的長度。", "Limits the on-screen shadow length up close to keep the cost down. Turn off to keep the full length.", "近くで見るときは影の画面上の長さを制限して負荷を抑えます。オフにすると元の長さのままになります。", "가까이서 볼 때 화면상의 그림자 길이를 제한해 부담을 줄입니다. 끄면 원래 길이를 유지합니다." },
            new[] { "濃度", "Intensity", "濃さ", "농도" },
            new[] { "範圍 (px)", "Radius (px)", "範囲 (px)", "범위 (px)" },
            new[] { "隨機抖動", "Jitter", "ジッター", "지터" },
            new[] { "品質", "Quality", "品質", "품질" },
            new[] { "低", "Low", "低", "낮음" },
            new[] { "中", "Medium", "中", "중간" },
            new[] { "高", "High", "高", "높음" },
            new[] { "極高", "Very high", "最高", "매우 높음" },
            new[] { "最低", "Lowest", "最低", "최저" },
            new[] { "最低：1 條光線、5 步，影子長時會出現分層，適合短影子或遠景。", "Lowest: 1 ray, 5 steps. Long shadows can show layers; suited to short shadows or far views.", "最低：レイ 1 本・5 ステップ。長い影では段が出ることがあり、短い影や遠景向けです。", "최저: 광선 1개, 5단계. 그림자가 길면 층이 보일 수 있어 짧은 그림자나 원경에 적합합니다." },
            new[] { "輕量", "Light", "軽量", "가벼움" },
            new[] { "標準", "Standard", "標準", "표준" },
            new[] { "標準（無 2×2）", "Standard (no 2x2)", "標準（2×2 なし）", "표준 (2×2 없음)" },
            new[] { "精細", "Fine", "精細", "정밀" },
            new[] { "最高", "Best", "最高", "최고" },
            new[] { "顏色", "Color", "色", "색상" },
            new[] { "合成方式", "Blend mode", "合成モード", "합성 방식" },
            new[] { "乘算", "Multiply", "乗算", "곱하기" },
            new[] { "減算", "Subtract", "減算", "빼기" },

            // ---- Sections: contact shadow -------------------------------------------------------
            new[] { "接觸陰影（固定方向）", "Contact shadow (fixed direction)", "接触影（固定方向）", "접촉 그림자 (고정 방향)" },
            new[] { "使用自己設定的光源方向，不受世界光源影響。其他玩家或物體擋在前面時也不會變黑。", "Uses a light direction you set, independent of the world's lights. Other players or objects in front do not darken it.", "自分で設定した光の向きを使い、ワールドのライトに影響されません。他のプレイヤーや物が手前にあっても黒くなりません。", "직접 정한 광원 방향을 사용하며 월드 광원의 영향을 받지 않습니다. 다른 플레이어나 물체가 앞을 가려도 검게 변하지 않습니다." },
            new[] { "方向基準", "Direction space", "方向の基準", "방향 기준" },
            new[] { "影子長度 (m)", "Shadow length (m)", "影の長さ (m)", "그림자 길이 (m)" },
            new[] { "柔和度 (度)", "Softness (degrees)", "柔らかさ (度)", "부드러움 (도)" },
            new[] { "0 = 銳利，數值越大邊緣越柔和。", "0 = sharp; higher values give softer edges.", "0 = くっきり。値が大きいほど縁が柔らかくなります。", "0 = 선명, 값이 클수록 가장자리가 부드러워집니다." },
            new[] { "忽略前方物體距離 (m)", "Ignore objects closer than (m)", "手前の物体を無視する距離 (m)", "무시할 앞쪽 물체 거리 (m)" },
            new[] { "比這個距離更靠近鏡頭的物體（例如其他玩家）不會投下陰影。", "Objects closer to the camera than this (e.g. other players) do not cast the shadow.", "これよりカメラに近い物体（他のプレイヤーなど）は影を落としません。", "이 거리보다 카메라에 가까운 물체 (예: 다른 플레이어)는 그림자를 드리우지 않습니다." },
            new[] { "遮蔽物厚度 (m)", "Occluder thickness (m)", "遮蔽物の厚み (m)", "차폐물 두께 (m)" },
            new[] { "背光面排除", "Back-facing rejection", "裏向き面の除外", "역광면 제외" },
            new[] { "背光面陰影", "Back-facing shade", "裏向き面の影", "역광면 그림자" },
            new[] { "背對光源的面也加上陰影（像簡單的明暗）。", "Also shades faces turned away from the light (like simple shading).", "光源に背を向けた面にも影を付けます（簡単な陰影のように）。", "광원을 등진 면에도 그림자를 넣습니다 (간단한 명암처럼)." },
            new[] { "陰影遮罩", "Shadow mask", "影マスク", "그림자 마스크" },
            new[] { "白 = 有陰影、黑 = 無陰影（R 通道）。", "White = shadow, black = no shadow (R channel).", "白 = 影あり、黒 = 影なし（R チャンネル）。", "흰색 = 그림자 있음, 검은색 = 그림자 없음 (R 채널)." },
            new[] { "陰影遮罩強度", "Shadow mask strength", "影マスクの強さ", "그림자 마스크 강도" },

            // ---- Sections: depth rim light ------------------------------------------------------
            new[] { "深度邊緣光", "Depth rim light", "深度リムライト", "깊이 림 라이트" },
            new[] { "沿著角色外輪廓的等寬亮邊。以深度貼圖判斷輪廓，所以手臂擋在身體前面時不會亮；角色後方很近的地方有牆、椅子或其他玩家時，亮邊會變弱或消失。與 lilToon 的邊緣光、逆光可以同時使用。", "A rim of constant width along the avatar's outer silhouette. The silhouette is found in the depth texture, so an arm in front of the body does not light up; with a wall, a chair or another player close behind the avatar the rim gets weaker or disappears. Can be used together with lilToon's Rim Light and Backlight.", "アバターの外側の輪郭に沿った一定幅のリムライトです。深度テクスチャで輪郭を判定するため、体の前にある腕は光りません。アバターのすぐ後ろに壁・椅子・他のプレイヤーがあると、リムが弱くなるか消えます。lilToon のリムライト・逆光ライトと併用できます。", "아바타의 바깥 윤곽을 따라 일정한 너비로 빛나는 림 라이트입니다. 깊이 텍스처로 윤곽을 판정하므로 몸 앞에 있는 팔은 빛나지 않습니다. 아바타 바로 뒤에 벽, 의자, 다른 플레이어가 있으면 림이 약해지거나 사라집니다. lilToon의 림 라이트, 역광과 함께 사용할 수 있습니다." },
            new[] { "寬度 (m)", "Width (m)", "幅 (m)", "너비 (m)" },
            new[] { "亮邊在角色身上的寬度。0.004 = 4 公釐。", "Width of the rim on the avatar. 0.004 = 4 mm.", "アバター上でのリムの幅です。0.004 = 4 mm。", "아바타 위에서의 림 너비입니다. 0.004 = 4 mm." },
            new[] { "只亮受光側", "Lit side only", "光が当たる側のみ", "빛을 받는 쪽만" },
            new[] { "0 = 整圈輪廓都亮，1 = 只有朝向光源的那一側。", "0 = the whole outline, 1 = only the side facing the light.", "0 = 輪郭全体、1 = 光源を向いた側のみ。", "0 = 윤곽 전체, 1 = 광원을 향한 쪽만." },
            new[] { "光源", "Light", "光源", "광원" },
            new[] { "世界光源（與 lilToon 相同）", "World light (same as lilToon)", "ワールドの光源（lilToon と同じ）", "월드 광원 (lilToon과 동일)" },
            new[] { "lilMCR 的光源方向", "lilMCR light direction", "lilMCR の光源方向", "lilMCR 광원 방향" },
            new[] { "世界光源：跟隨 lilToon 計算出的光源方向，與 lilToon 的逆光亮在同一側。lilMCR 的光源方向：使用接觸陰影設定的固定方向。", "World light: follows the light direction lilToon computes, so it lights the same side as lilToon's Backlight. lilMCR light direction: the fixed direction set for the contact shadow.", "ワールドの光源：lilToon が計算した光源方向に従い、lilToon の逆光ライトと同じ側が光ります。lilMCR の光源方向：接触影で設定した固定方向を使います。", "월드 광원: lilToon이 계산한 광원 방향을 따르며 lilToon의 역광과 같은 쪽이 빛납니다. lilMCR 광원 방향: 접촉 그림자에서 설정한 고정 방향을 사용합니다." },
            new[] { "混合方式", "Blend mode", "合成方法", "합성 방식" },
            new[] { "濾色（不過曝）", "Screen (no burn-out)", "スクリーン（白飛びしない）", "스크린 (과다 노출 없음)" },
            new[] { "加算", "Add", "加算", "더하기" },
            new[] { "取代", "Replace", "置き換え", "대체" },
            new[] { "濾色：不會比亮邊顏色本身更亮，與 lilToon 的邊緣光、逆光疊在一起也不容易過曝。", "Screen: never brighter than the rim color itself, so it does not burn out on top of lilToon's Rim Light or Backlight.", "スクリーン：リムの色そのものより明るくならないため、lilToon のリムライトや逆光ライトと重なっても白飛びしにくくなります。", "스크린: 림 색상 자체보다 밝아지지 않으므로 lilToon의 림 라이트, 역광과 겹쳐도 과다 노출이 잘 생기지 않습니다." },
            new[] { "內側柔化", "Inner feather", "内側のぼかし", "안쪽 부드럽게" },
            new[] { "0 = 整條亮邊同樣亮度的硬邊，1 = 從輪廓往內側平滑淡出（像一般的邊緣光）。大於 0 時，有亮邊的像素會多做 2～5 次深度取樣來找出輪廓位置。", "0 = a hard band of one brightness, 1 = fades smoothly from the silhouette inward (like an ordinary rim light). Above 0, pixels that have a rim take 2 to 5 more depth reads to locate the silhouette.", "0 = 明るさが一定の硬い帯、1 = 輪郭から内側へ滑らかにフェード（通常のリムライトのように）。0 より大きい場合、リムのあるピクセルは輪郭の位置を求めるために深度をさらに 2〜5 回読み取ります。", "0 = 밝기가 일정한 딱딱한 띠, 1 = 윤곽에서 안쪽으로 부드럽게 페이드 (일반 림 라이트처럼). 0보다 크면 림이 있는 픽셀은 윤곽 위치를 찾기 위해 깊이를 2~5회 더 읽습니다." },
            new[] { "輪廓判定距離 (m)", "Silhouette distance (m)", "輪郭の判定距離 (m)", "윤곽 판정 거리 (m)" },
            new[] { "後方比這個距離更遠才算輪廓。調小：靠近牆壁時也會亮，但衣服皺褶等內側落差也可能亮起來。", "What lies behind must be farther than this to count as the silhouette. Lower: the rim also shows close to a wall, but inner steps such as folds of clothing may light up too.", "後ろにあるものがこの距離より遠い場合に輪郭とみなします。小さくすると壁の近くでも光りますが、服のしわなど内側の段差も光ることがあります。", "뒤에 있는 것이 이 거리보다 멀어야 윤곽으로 봅니다. 작게 하면 벽 가까이에서도 빛나지만 옷 주름 같은 안쪽 단차도 빛날 수 있습니다." },
            new[] { "沿用底色", "Use base color", "ベースカラーを使用", "베이스 색상 사용" },
            new[] { "0 = 使用上面的顏色，1 = 再乘上材質的底色。", "0 = the color above, 1 = also multiplied by the material's base color.", "0 = 上の色をそのまま使用、1 = さらにマテリアルのベースカラーを乗算。", "0 = 위의 색상 사용, 1 = 머티리얼의 베이스 색상을 추가로 곱함." },
            new[] { "跟隨光源顏色", "Follow light color", "光源の色に従う", "광원 색상 따르기" },
            new[] { "1 = 亮邊乘上光源顏色，暗的世界裡也會跟著變暗。0 = 固定亮度（暗處會像自發光）。", "1 = the rim is multiplied by the light color and gets darker in dark worlds. 0 = fixed brightness (looks emissive in the dark).", "1 = リムに光源の色を乗算し、暗いワールドでは一緒に暗くなります。0 = 明るさ固定（暗い場所では発光しているように見えます）。", "1 = 림에 광원 색상을 곱하여 어두운 월드에서는 함께 어두워집니다. 0 = 밝기 고정 (어두운 곳에서는 발광처럼 보임)." },
            new[] { "陰影中減弱", "Weaker in shadow", "影の中で弱める", "그림자 안에서 약하게" },
            new[] { "lilToon 的陰影範圍內減弱亮邊。", "Weakens the rim inside lilToon's shadow.", "lilToon の影の範囲でリムを弱めます。", "lilToon의 그림자 범위에서 림을 약하게 합니다." },
            new[] { "邊緣光遮罩", "Rim mask", "リムマスク", "림 마스크" },
            new[] { "白 = 有亮邊、黑 = 沒有亮邊（R 通道）。可用遮罩生成器製作。", "White = rim, black = no rim (R channel). Can be made with the Mask Generator.", "白 = リムあり、黒 = リムなし（R チャンネル）。マスク生成ツールで作成できます。", "흰색 = 림 있음, 검은색 = 림 없음 (R 채널). 마스크 생성기로 만들 수 있습니다." },
            new[] { "邊緣光遮罩強度", "Rim mask strength", "リムマスクの強さ", "림 마스크 강도" },

            // ---- Sections: occlusion and options ------------------------------------------------
            new[] { "同時會在角色底下建立 MCR_DepthSource（讓 Unity 產生深度貼圖用的光源，已經有就不會重複建立）。", "MCR_DepthSource (a light that makes Unity render the depth texture) is also created under the avatar (not twice).", "アバターの下に MCR_DepthSource（Unity に深度テクスチャを描画させるライト）も作成します（すでにある場合は作成しません）。", "아바타 아래에 MCR_DepthSource (Unity가 깊이 텍스처를 만들게 하는 라이트)도 함께 생성합니다 (이미 있으면 중복 생성하지 않음)." },
            new[] { "已把 MCR_DepthSource（產生深度貼圖用的光源）加到角色底下。", "Added MCR_DepthSource (the light that enables the depth texture) under the avatar.", "MCR_DepthSource（深度テクスチャ用のライト）をアバターの下に追加しました。", "MCR_DepthSource (깊이 텍스처용 라이트)를 아바타 아래에 추가했습니다." },
            new[] { "遮蔽與接觸陰影的總強度。", "Overall strength of occlusion and contact shadow.", "遮蔽と接触影の全体の強さ。", "차폐와 접촉 그림자의 전체 강도입니다." },
            new[] { "最低亮度", "Floor brightness", "最低の明るさ", "최저 밝기" },
            new[] { "不會比原本顏色的這個比例更暗（0.25 = 最暗到原本的 25%）。避免角色變全黑。", "Never darker than this fraction of the original color (0.25 = down to 25%). Keeps the avatar from turning black.", "元の色のこの割合より暗くなりません（0.25 = 元の 25% まで）。アバターが真っ黒になるのを防ぎます。", "원래 색의 이 비율보다 어두워지지 않습니다 (0.25 = 최대 원래의 25%까지). 아바타가 새까맣게 되는 것을 막습니다." },
            new[] { "法線平滑", "Normal smoothing", "法線の平滑化", "노멀 스무딩" },
            new[] { "距離淡出 (m)", "Distance fade (m)", "距離フェード (m)", "거리 페이드 (m)" },
            new[] { "開始", "Start", "開始", "시작" },
            new[] { "結束", "End", "終了", "끝" },
            new[] { "從「開始」到「結束」之間效果逐漸消失。結束 ≤ 開始 = 不淡出。", "The effect fades out between Start and End. End ≤ Start = no fade.", "「開始」から「終了」の間で効果が消えていきます。終了 ≤ 開始 = フェードなし。", "\"시작\"부터 \"끝\" 사이에서 효과가 점점 사라집니다. 끝 ≤ 시작 = 페이드 없음." },
            new[] { "半透明時避開被遮住的部分", "Transparent: skip covered parts", "半透明時は隠れた部分を除外", "반투명일 때 가려진 부분 제외" },
            new[] { "半透明材質：被不透明物體擋住的地方不加遮蔽與接觸陰影。", "Transparent materials: no occlusion or contact shadow where an opaque object covers the surface.", "半透明マテリアル：不透明な物体に隠れた部分には遮蔽と接触影を付けません。", "반투명 머티리얼: 불투명한 물체에 가려진 곳에는 차폐와 접촉 그림자를 넣지 않습니다." },
            new[] { "遮蔽（環境光遮蔽）", "Occlusion (ambient occlusion)", "遮蔽（アンビエントオクルージョン）", "차폐 (앰비언트 오클루전)" },
            new[] { "對比", "Contrast", "コントラスト", "대비" },
            new[] { "-1 = 淡的遮蔽也明顯、0 = 不變、1 = 淡的更淡、濃的更濃。", "-1 = faint occlusion stands out, 0 = unchanged, 1 = faint gets fainter and strong gets stronger.", "-1 = 薄い遮蔽も目立つ、0 = そのまま、1 = 薄い所はより薄く、濃い所はより濃く。", "-1 = 옅은 차폐도 뚜렷하게, 0 = 그대로, 1 = 옅은 것은 더 옅게, 진한 것은 더 진하게." },
            new[] { "範圍 (m)", "Reach (m)", "範囲 (m)", "범위 (m)" },
            new[] { "多遠的遮蔽物會影響這一點。", "How far away an occluder can still affect a point.", "どのくらい離れた遮蔽物まで影響するか。", "얼마나 떨어진 차폐물까지 이 지점에 영향을 주는지." },
            new[] { "集中度", "Focus", "集中度", "집중도" },
            new[] { "0 = 遠處的遮蔽物也有影響、1 = 只有很近的縫隙變暗。", "0 = far occluders count too, 1 = only the closest crevices darken.", "0 = 遠くの遮蔽物も影響、1 = ごく近い隙間だけが暗くなります。", "0 = 먼 차폐물도 영향, 1 = 아주 가까운 틈만 어두워짐." },
            new[] { "柔和度", "Softness", "柔らかさ", "부드러움" },
            new[] { "越大雜訊越少、邊緣越柔和；0 = 最銳利。", "Higher = less noise and softer edges; 0 = sharpest.", "大きいほどノイズが減り縁が柔らかくなります。0 = 最もくっきり。", "클수록 노이즈가 적고 가장자리가 부드러워집니다. 0 = 가장 선명." },
            new[] { "角度範圍 (度)", "Angle (degrees)", "角度範囲 (度)", "각도 범위 (도)" },
            new[] { "只看法線周圍這個角度內的遮蔽。", "Only looks for occluders within this angle around the normal.", "法線の周りのこの角度内の遮蔽だけを見ます。", "노멀 주변 이 각도 안의 차폐만 봅니다." },
            new[] { "範圍單位", "Reach unit", "範囲の単位", "범위 단위" },
            new[] { "公尺（固定大小）", "Meters (fixed size)", "メートル（固定サイズ）", "미터 (고정 크기)" },
            new[] { "畫面（跟隨縮放）", "Screen (follows zoom)", "画面（ズームに追従）", "화면 (줌 추종)" },
            new[] { "畫面：鏡頭拉近（FOV 變小）時範圍跟著放大，在畫面上看起來一樣大。", "Screen: when the camera zooms in (smaller FOV) the reach grows so it looks the same size on screen.", "画面：カメラをズーム（FOV を小さく）すると範囲も大きくなり、画面上で同じ大きさに見えます。", "화면: 카메라를 당겨 (FOV가 작아지면) 범위도 커져서 화면에서 같은 크기로 보입니다." },
            new[] { "近處顏色", "Near color", "近くの色", "가까운 곳 색상" },
            new[] { "遠處顏色", "Far color", "遠くの色", "먼 곳 색상" },
            new[] { "遮蔽物離得越遠，顏色越接近這個。", "The farther the occluder, the closer the color gets to this.", "遮蔽物が遠いほど、この色に近づきます。", "차폐물이 멀수록 색이 이 색에 가까워집니다." },
            new[] { "帶入底色", "Albedo tint", "ベースカラーを反映", "베이스 색 반영" },
            new[] { "遮蔽的顏色帶上材質本身的顏色（像縫隙裡的反光），避免變灰。", "Tints the occlusion with the material's own color (like light bouncing in a crease) so it does not turn gray.", "遮蔽の色にマテリアル自体の色を乗せ（隙間での照り返しのように）、灰色になるのを防ぎます。", "차폐 색에 머티리얼 자체의 색을 입혀 (틈 안의 반사광처럼) 회색이 되지 않게 합니다." },
            new[] { "遮蔽反射", "Occlude reflections", "反射を遮蔽", "반사 차폐" },
            new[] { "遮蔽處額外減弱反射、MatCap、邊緣光、亮片。", "Also reduces reflection, MatCap, rim light and glitter in occluded areas.", "遮蔽された部分の反射・MatCap・リムライト・ラメをさらに弱めます。", "차폐된 곳에서 반사, MatCap, 림 라이트, 반짝임을 추가로 줄입니다." },
            new[] { "遮蔽遮罩", "Occlusion mask", "遮蔽マスク", "차폐 마스크" },
            new[] { "白 = 有遮蔽、黑 = 無遮蔽（R 通道）。", "White = occlusion, black = no occlusion (R channel).", "白 = 遮蔽あり、黒 = 遮蔽なし（R チャンネル）。", "흰색 = 차폐 있음, 검은색 = 차폐 없음 (R 채널)." },
            new[] { "遮蔽遮罩強度", "Occlusion mask strength", "遮蔽マスクの強さ", "차폐 마스크 강도" },
            new[] { "反轉遮蔽遮罩", "Invert occlusion mask", "遮蔽マスクを反転", "차폐 마스크 반전" },
            new[] { "起點偏移 (m)", "Start offset (m)", "開始位置のオフセット (m)", "시작점 오프셋 (m)" },
            new[] { "法線", "Normal", "法線", "노멀" },
            new[] { "出現一格一格的自我陰影時稍微加大。", "Raise slightly if blocky self-shadowing appears.", "ブロック状の自己影が出るときは少し上げてください。", "계단 모양의 자기 그림자가 보이면 조금 키우세요." },

            new[] { "深度", "Depth", "深度", "깊이" },

            // ---- lilToon setting ----------------------------------------------------------------

            // ---- Material Inspector -------------------------------------------------------------
            new[] { "把這個區塊恢復成預設值", "Reset this section to defaults", "このセクションを既定値に戻す", "이 섹션을 기본값으로 되돌리기" },
            new[] { "全部重置為預設值", "Reset all to defaults", "すべて既定値に戻す", "모두 기본값으로 초기화" },
            new[] { "全部重置為預設值（連遮罩貼圖一起）", "Reset all to defaults (including mask textures)", "すべて既定値に戻す（マスクテクスチャも含む）", "모두 기본값으로 초기화 (마스크 텍스처 포함)" },
            new[] { "複製設定", "Copy settings", "設定をコピー", "설정 복사" },
            new[] { "貼上設定", "Paste settings", "設定を貼り付け", "설정 붙여넣기" },
            new[] { "重新偵測角色方向（從場景）", "Re-detect avatar direction (from the scene)", "アバターの向きを再検出（シーンから）", "아바타 방향 다시 감지 (씬에서)" },
            new[] { "開啟批次設定視窗", "Open Batch Settings", "一括設定ウィンドウを開く", "일괄 설정 창 열기" },
            new[] { "在目前開啟的場景中找不到使用這個材質的角色。\n請先把角色放進場景再執行。", "No avatar using this material was found in the open scenes.\nPut the avatar in the scene first.", "開いているシーンに、このマテリアルを使うアバターが見つかりません。\n先にアバターをシーンに置いてから実行してください。", "열려 있는 씬에서 이 머티리얼을 사용하는 아바타를 찾을 수 없습니다.\n먼저 아바타를 씬에 넣은 뒤 실행하세요." },

            // ---- Batch Settings -----------------------------------------------------------------
            new[] { "Macaron 批次設定", "Macaron Batch Settings", "Macaron 一括設定", "Macaron 일괄 설정" },
            new[] { "要修改哪些材質", "Which materials", "対象のマテリアル", "수정할 머티리얼" },
            new[] { "使用轉換資料夾（{0}）", "Use the converted folder ({0})", "変換フォルダを使う（{0}）", "변환 폴더 사용 ({0})" },
            new[] { "Assets 內所有使用 Macaron 著色器的材質。", "Every material in Assets that uses a Macaron shader.", "Assets 内で Macaron シェーダーを使うすべてのマテリアル。", "Assets 안에서 Macaron 셰이더를 사용하는 모든 머티리얼." },
            new[] { "找到 {0} 個 Macaron 材質（{1} 個會被修改）", "{0} Macaron material(s) found ({1} will be changed)", "Macaron マテリアル {0} 個（変更対象 {1} 個）", "Macaron 머티리얼 {0}개 발견 ({1}개 수정됨)" },
            new[] { "全部材質重置為預設值", "Reset all materials to defaults", "全マテリアルを既定値に戻す", "모든 머티리얼을 기본값으로 초기화" },
            new[] { "重新偵測角色方向", "Re-detect avatar direction", "アバターの向きを再検出", "아바타 방향 다시 감지" },
            new[] { "{0} 個材質的 Macaron 設定全部恢復成預設值？\n（影子方向的角色軸會保留，可用 Ctrl+Z 復原）", "Reset all Macaron settings of {0} material(s) to the defaults?\n(The avatar axis for the shadow direction is kept. Ctrl+Z to undo.)", "{0} 個のマテリアルの Macaron 設定をすべて既定値に戻しますか？\n（影の向き用のアバター軸は保持されます。Ctrl+Z で元に戻せます）", "머티리얼 {0}개의 Macaron 설정을 모두 기본값으로 되돌릴까요?\n(그림자 방향의 아바타 축은 유지되며 Ctrl+Z로 되돌릴 수 있습니다)" },
            new[] { "{0} 個材質已重置為預設值。", "{0} material(s) reset to the defaults.", "{0} 個のマテリアルを既定値に戻しました。", "머티리얼 {0}개를 기본값으로 초기화했습니다." },
            new[] { "已重新偵測 {0} 個材質的角色方向。", "Re-detected the avatar direction of {0} material(s).", "{0} 個のマテリアルのアバターの向きを再検出しました。", "머티리얼 {0}개의 아바타 방향을 다시 감지했습니다." },

            // ---- Avatar Converter ---------------------------------------------------------------
            new[] { "Macaron 角色轉換", "Macaron Avatar Converter", "Macaron アバター変換", "Macaron 아바타 변환" },
            new[] { "把角色拖進來，選好影子方向，按一下就完成。", "Drop the avatar in, pick the shadow direction, press one button.", "アバターを入れて、影の向きを選び、ボタンを押すだけ。", "아바타를 넣고 그림자 방향을 고른 뒤 버튼 한 번이면 끝납니다." },
            new[] { "找不到 Macaron 著色器（{0}）。\n請確認 lilToon 已安裝，且著色器資料夾裡的 .lilcontainer 檔案完整。", "Macaron shader not found ({0}).\nCheck that lilToon is installed and that the .lilcontainer files in the shader folder are complete.", "Macaron シェーダーが見つかりません（{0}）。\nlilToon がインストールされていること、シェーダーフォルダの .lilcontainer ファイルが揃っていることを確認してください。", "Macaron 셰이더를 찾을 수 없습니다 ({0}).\nlilToon이 설치되어 있고 셰이더 폴더의 .lilcontainer 파일이 모두 있는지 확인하세요." },
            new[] { "從 Hierarchy 拖曳角色到上方欄位。", "Drag the avatar from the Hierarchy into the field above.", "Hierarchy からアバターを上の欄にドラッグしてください。", "Hierarchy에서 아바타를 위 칸으로 드래그하세요." },
            new[] { "影子方向", "Shadow direction", "影の向き", "그림자 방향" },
            new[] { "轉換", "Convert", "変換", "변환" },
            new[] { "開始轉換（{0} 個材質）", "Convert ({0} material(s))", "変換する（{0} 個のマテリアル）", "변환 시작 (머티리얼 {0}개)" },
            new[] { "開始轉換", "Convert", "変換する", "변환 시작" },
            new[] { "轉換後的材質存放在 {0}/<角色名稱>，原本的材質不會被修改。可用 Ctrl+Z 復原。", "Converted materials are saved to {0}/<avatar name>. The original materials are not modified. Ctrl+Z to undo.", "変換後のマテリアルは {0}/<アバター名> に保存されます。元のマテリアルは変更されません。Ctrl+Z で元に戻せます。", "변환된 머티리얼은 {0}/<아바타 이름>에 저장되며 원래 머티리얼은 수정되지 않습니다. Ctrl+Z로 되돌릴 수 있습니다." },
            new[] { "還原", "Restore", "元に戻す", "복원" },
            new[] { "換回原本的材質（{0} 個）", "Restore the original materials ({0})", "元のマテリアルに戻す（{0} 個）", "원래 머티리얼로 되돌리기 ({0}개)" },
            new[] { "換回原本的材質", "Restore the original materials", "元のマテリアルに戻す", "원래 머티리얼로 되돌리기" },
            new[] { "同時移除 MCR_DepthSource、光照調節器和世界落影", "Also remove MCR_DepthSource, the Light Adjuster and World Shadow", "MCR_DepthSource・ライト調整・World Shadow も削除する", "MCR_DepthSource, 라이트 조절기, 월드 그림자도 함께 제거" },
            new[] { "只會換回角色上的材質，轉換產生的材質檔不會刪除。可用 Ctrl+Z 復原。", "Only the materials on the avatar are swapped back; the converted material files are not deleted. Ctrl+Z undoes it.", "アバターのマテリアルを戻すだけで、変換で作成したマテリアルファイルは削除されません。Ctrl+Z で元に戻せます。", "아바타의 머티리얼만 되돌리며, 변환으로 만든 머티리얼 파일은 삭제하지 않습니다. Ctrl+Z로 되돌릴 수 있습니다." },
            new[] { "有 {0} 個 Macaron 材質找不到原本的材質（原本的材質已被刪除或改名，或本來就是 Macaron 材質），會保持不變。", "{0} Macaron material(s) have no original to go back to (the original was deleted or renamed, or it was a Macaron material from the start) and stay as they are.", "{0} 個の Macaron マテリアルは元のマテリアルが見つからないため（削除・名前変更済み、または最初から Macaron マテリアル）、そのままになります。", "Macaron 머티리얼 {0}개는 원래 머티리얼을 찾을 수 없어 (원래 머티리얼이 삭제되었거나 이름이 바뀌었거나, 처음부터 Macaron 머티리얼) 그대로 둡니다." },
            new[] { "已換回 {0} 個原本的材質。", "Restored {0} original material(s).", "{0} 個のマテリアルを元に戻しました。", "원래 머티리얼 {0}개로 되돌렸습니다." },
            new[] { "已移除 MCR_DepthSource。", "MCR_DepthSource removed.", "MCR_DepthSource を削除しました。", "MCR_DepthSource를 제거했습니다." },
            new[] { "已移除光照調節器。", "Light Adjuster removed.", "ライト調整を削除しました。", "라이트 조절기를 제거했습니다." },
            new[] { "已移除世界落影。", "World Shadow removed.", "ワールド影を削除しました。", "월드 그림자를 제거했습니다." },
            new[] { "開啟批次設定（一次調整全部材質）", "Open Batch Settings (adjust all materials at once)", "一括設定を開く（全マテリアルをまとめて調整）", "일괄 설정 열기 (모든 머티리얼을 한 번에 조정)" },
            new[] { "轉換 {0}", "Convert {0}", "変換 {0}", "변환 {0}" },
            new[] { "重置 {0}", "Reset {0}", "リセット {0}", "초기화 {0}" },
            new[] { "不支援 {0}", "Unsupported {0}", "非対応 {0}", "미지원 {0}" },
            new[] { "「重置」= 已經是 Macaron 的材質，設定會恢復成預設值並套用下面的影子方向（遮罩貼圖會保留）。", "\"Reset\" = already Macaron: its settings go back to the defaults with the shadow direction below (mask textures are kept).", "「リセット」= すでに Macaron のマテリアル。設定を既定値に戻し、下の影の向きを適用します（マスクテクスチャは保持されます）。", "\"초기화\" = 이미 Macaron인 머티리얼은 설정을 기본값으로 되돌리고 아래의 그림자 방향을 적용합니다 (마스크 텍스처는 유지됩니다)." },
            new[] { "材質清單（{0}）", "Materials ({0})", "マテリアル一覧（{0}）", "머티리얼 목록 ({0})" },
            new[] { "略過", "Skip", "スキップ", "건너뜀" },
            new[] { "找不到轉換目標：{0}（.lilcontainer 可能被刪除）", "Conversion target not found: {0} (the .lilcontainer may have been deleted)", "変換先が見つかりません：{0}（.lilcontainer が削除された可能性があります）", "변환 대상을 찾을 수 없습니다: {0} (.lilcontainer가 삭제되었을 수 있음)" },
            new[] { "沒有對應版本的 lilToon 著色器（[Optional] 系列或其他客製著色器）：{0}", "lilToon shader without a matching variant ([Optional] series or another custom shader): {0}", "対応する版がない lilToon シェーダー（[Optional] 系や他のカスタムシェーダー）：{0}", "대응하는 버전이 없는 lilToon 셰이더 ([Optional] 계열 또는 다른 커스텀 셰이더): {0}" },
            new[] { "不支援的著色器：{0}", "Unsupported shader: {0}", "非対応のシェーダー：{0}", "지원하지 않는 셰이더: {0}" },
            new[] { "轉換  {0} : {1} → {2}", "Convert  {0} : {1} → {2}", "変換  {0} : {1} → {2}", "변환  {0} : {1} → {2}" },
            new[] { "重置  {0}", "Reset  {0}", "リセット  {0}", "초기화  {0}" },
            new[] { "略過  {0} : {1}", "Skip  {0} : {1}", "スキップ  {0} : {1}", "건너뜀  {0} : {1}" },
            new[] { "失敗  {0} : 無法複製這個材質（材質資產可能已損壞，例如找不到父材質的 Material Variant），保持原樣。", "Failed  {0} : this material could not be copied (the asset may be broken, e.g. a Material Variant whose parent is missing); left as it is.", "失敗  {0} : このマテリアルを複製できません（親マテリアルが見つからない Material Variant など、アセットが壊れている可能性があります）。そのままにします。", "실패  {0} : 이 머티리얼을 복제할 수 없습니다 (부모 머티리얼이 없는 Material Variant 등 에셋이 손상되었을 수 있음). 그대로 둡니다." },
            new[] { "有 {0} 個材質無法複製，已保持原樣（材質資產可能已損壞，例如找不到父材質的 Material Variant）。", "{0} material(s) could not be copied and were left as they are (the asset may be broken, e.g. a Material Variant whose parent is missing).", "{0} 個のマテリアルを複製できなかったため、そのままにしました（親マテリアルが見つからない Material Variant など、アセットが壊れている可能性があります）。", "머티리얼 {0}개를 복제할 수 없어 그대로 두었습니다 (부모 머티리얼이 없는 Material Variant 등 에셋이 손상되었을 수 있음)." },
            new[] { "完成！轉換 {0}、重置 {1}、略過 {2}。", "Done! Converted {0}, reset {1}, skipped {2}.", "完了！変換 {0}、リセット {1}、スキップ {2}。", "완료! 변환 {0}, 초기화 {1}, 건너뜀 {2}." },
            new[] { "詳細內容已輸出到 Console。", "Details were written to the Console.", "詳細は Console に出力しました。", "자세한 내용은 Console에 출력했습니다." },
            new[] { "注意  {0} 被多個不同骨骼空間的 Renderer 共用（{1} / {2}），「跟隨角色」方向以 {1} 為準。", "Note  {0} is shared by renderers in different bone spaces ({1} / {2}); the \"Follow avatar\" direction uses {1}.", "注意  {0} は異なるボーン空間の Renderer で共有されています（{1} / {2}）。「アバター基準」の向きは {1} が基準になります。", "주의  {0}은(는) 서로 다른 본 공간의 Renderer가 공유합니다 ({1} / {2}). \"아바타 추종\" 방향은 {1} 기준입니다." },
            new[] { "一次修改所有已轉換材質的設定。改動會立刻套用到全部目標材質。", "Change the settings of many converted materials at once. Every change is applied to all target materials immediately.", "変換済みマテリアルの設定をまとめて変更します。変更はすぐにすべての対象マテリアルへ適用されます。", "변환된 머티리얼의 설정을 한 번에 바꿉니다. 변경 사항은 모든 대상 머티리얼에 즉시 적용됩니다." },
            new[] { "修改設定（立即套用）", "Change settings (applied immediately)", "設定を変更（即時適用）", "설정 변경 (즉시 적용)" },
            new[] { "沒有要修改的材質。請先在上面選擇角色、資料夾或整個專案。", "No materials to change. Pick an avatar, a folder or the whole project above first.", "変更するマテリアルがありません。上でアバター、フォルダー、またはプロジェクト全体を選んでください。", "수정할 머티리얼이 없습니다. 먼저 위에서 아바타, 폴더 또는 프로젝트 전체를 선택하세요." },
            new[] { "顯示的是第一個材質的數值；顯示「—」的項目代表各材質的數值不同，修改後會統一。", "Values shown are from the first material. Rows shown as \"—\" differ between materials; changing them makes them all the same.", "表示されているのは最初のマテリアルの値です。「—」の項目はマテリアルごとに値が異なり、変更すると統一されます。", "첫 번째 머티리얼의 값이 표시됩니다. \"—\"로 표시된 항목은 머티리얼마다 값이 다르며, 수정하면 하나로 통일됩니다." },
            new[] { "套用這個材質的全部設定", "Apply all settings from this material", "このマテリアルの設定をすべて適用", "이 머티리얼의 설정을 모두 적용" },
            new[] { "「{0}」已套用到 {1} 個材質。（可用 Ctrl+Z 復原）", "\"{0}\" applied to {1} materials. (Ctrl+Z to undo)", "「{0}」を {1} 個のマテリアルに適用しました。（Ctrl+Z で元に戻せます）", "\"{0}\"을(를) 머티리얼 {1}개에 적용했습니다. (Ctrl+Z로 되돌리기)" },
            new[] { "把「{0}」的全部 Macaron 設定套用到 {1} 個材質？\n（影子方向的角色軸會保留，可用 Ctrl+Z 復原）", "Apply all Macaron settings of \"{0}\" to {1} materials?\n(The avatar axis used by the shadow direction is kept. Ctrl+Z to undo.)", "「{0}」の Macaron 設定をすべて {1} 個のマテリアルに適用しますか？\n（影の方向用のアバター軸は保持されます。Ctrl+Z で元に戻せます）", "\"{0}\"의 Macaron 설정을 모두 머티리얼 {1}개에 적용할까요?\n(그림자 방향의 아바타 축은 유지되며 Ctrl+Z로 되돌릴 수 있습니다)" },
            new[] { "已把「{0}」的設定套用到 {1} 個材質。（可用 Ctrl+Z 復原）", "Applied the settings of \"{0}\" to {1} materials. (Ctrl+Z to undo)", "「{0}」の設定を {1} 個のマテリアルに適用しました。（Ctrl+Z で元に戻せます）", "\"{0}\"의 설정을 머티리얼 {1}개에 적용했습니다. (Ctrl+Z로 되돌리기)" },
            new[] { "加入光照調節器（Modular Avatar 選單）", "Add the Light Adjuster (Modular Avatar menu)", "ライト調整（Modular Avatar メニュー）を追加", "라이트 조절기 추가 (Modular Avatar 메뉴)" },
            new[] { "需要 Modular Avatar 才能使用光照調節器。", "The Light Adjuster needs Modular Avatar.", "ライト調整には Modular Avatar が必要です。", "라이트 조절기를 사용하려면 Modular Avatar가 필요합니다." },
            new[] { "lilMCR 總開關", "lilMCR on / off", "lilMCR オン／オフ", "lilMCR 전체 스위치" },
            new[] { "亮度", "Brightness", "明るさ", "밝기" },
            new[] { "固定亮度", "Fixed Brightness", "明るさ固定", "밝기 고정" },
            new[] { "光源顏色影響", "Light Color Influence", "光の色の影響", "광원 색 영향" },
            new[] { "色溫", "Color Temperature", "色温度", "색온도" },
            new[] { "彩度", "Saturation", "彩度", "채도" },
            new[] { "固定方向強度", "Fixed Direction", "固定方向の強さ", "고정 방향 강도" },
            new[] { "左右", "Left / Right", "左右", "좌우" },
            new[] { "上下", "Up / Down", "上下", "상하" },
            new[] { "已加入光照調節器（Modular Avatar 選單：{0}）。", "Light Adjuster added (Modular Avatar menu: {0}).", "ライト調整を追加しました（Modular Avatar メニュー：{0}）。", "라이트 조절기를 추가했습니다 (Modular Avatar 메뉴: {0})." },
            new[] { "已更新光照調節器（Modular Avatar 選單：{0}）。", "Light Adjuster updated (Modular Avatar menu: {0}).", "ライト調整を更新しました（Modular Avatar メニュー：{0}）。", "라이트 조절기를 업데이트했습니다 (Modular Avatar 메뉴: {0})." },
            new[] { "Macaron 光照", "Macaron Light", "Macaron ライト", "Macaron 라이트" },
            new[] { "Macaron 光照調節器", "Macaron Light Adjuster", "Macaron ライト調整", "Macaron 라이트 조절기" },
            new[] { "專案沒有 Modular Avatar（或 VRChat Avatars SDK），沒有加入光照調節器。", "Modular Avatar (or the VRChat Avatars SDK) is not in the project: the Light Adjuster was not added.", "プロジェクトに Modular Avatar（または VRChat Avatars SDK）がないため、ライト調整は追加されませんでした。", "프로젝트에 Modular Avatar (또는 VRChat Avatars SDK)가 없어 라이트 조절기를 추가하지 않았습니다." },
            new[] { "沒有選擇任何光照項目，沒有加入光照調節器。", "No Light Adjuster item is selected: the Light Adjuster was not added.", "ライト調整の項目が選択されていないため、ライト調整は追加されませんでした。", "선택한 라이트 항목이 없어 라이트 조절기를 추가하지 않았습니다." },
            new[] { "這個角色沒有 Macaron 材質，沒有加入光照調節器。", "This avatar has no Macaron materials: the Light Adjuster was not added.", "このアバターには Macaron マテリアルがないため、ライト調整は追加されませんでした。", "이 아바타에는 Macaron 머티리얼이 없어 라이트 조절기를 추가하지 않았습니다." },
            new[] { "這個角色沒有光照調節器。", "This avatar has no Light Adjuster.", "このアバターにはライト調整がありません。", "이 아바타에는 라이트 조절기가 없습니다." },
            new[] { "從「{0}」移除光照調節器物件？（可用 Ctrl+Z 復原；產生的動畫檔不會刪除）", "Remove the Light Adjuster object from \"{0}\"? (Ctrl+Z to undo; the generated animation file is not deleted)", "「{0}」からライト調整オブジェクトを削除しますか？（Ctrl+Z で元に戻せます。生成したアニメーションファイルは削除されません）", "\"{0}\"에서 라이트 조절기 오브젝트를 제거할까요? (Ctrl+Z로 되돌릴 수 있으며, 생성된 애니메이션 파일은 삭제되지 않습니다)" },
            new[] { "移除", "Remove", "削除", "제거" },
            // GTAO, SSAO range
            new[] { "陰影傾向", "Shading style", "陰影の傾向", "음영 경향" },
            new[] { "0 = 強調輪廓與縫隙、1 = 像落影一樣在突出物下方大片變暗。", "0 = emphasize contours and gaps, 1 = broad darkening under overhangs, like a cast shadow.", "0 = 輪郭や隙間を強調、1 = 落ち影のように張り出しの下を広く暗くします。", "0 = 윤곽과 틈을 강조, 1 = 드리운 그림자처럼 튀어나온 부분 아래를 넓게 어둡게 합니다." },
            new[] { "輪廓抑制", "Edge rejection", "輪郭の抑制", "윤곽 억제" },
            new[] { "減弱幾乎側對鏡頭的面（角色外輪廓）上的遮蔽，消除黑色描邊。", "Weakens occlusion on surfaces seen nearly edge-on (the avatar's outline), removing dark rims.", "視線とほぼ平行な面（アバターの輪郭）の遮へいを弱め、黒い縁取りを抑えます。", "시선과 거의 평행한 면(아바타 외곽)의 차폐를 약하게 해 검은 테두리를 없앱니다." },
            new[] { "網格", "Meshes", "メッシュ", "메시" },
            new[] { "GTAO（中負擔）", "GTAO (medium cost)", "GTAO（中負荷）", "GTAO (중간 부하)" },
            new[] { "Ray March 效能負擔比 SSAO 高。GTAO 依遮蔽物的角度計算被遮住的比例，陰影柔和且遠近一致。", "Ray March costs more than SSAO. GTAO measures how much of the surroundings is covered from the angles of the occluders: soft and consistent near and far.", "Ray March は SSAO より負荷が高めです。GTAO は遮へい物の角度から覆われている割合を計算し、柔らかく遠近で一貫した陰影になります。", "Ray March는 SSAO보다 부하가 큽니다. GTAO는 차폐물의 각도로 가려진 비율을 계산해 부드럽고 거리와 관계없이 일관된 그림자를 만듭니다." },
            new[] { "自我遮蔽容許 (m)", "Self-occlusion tolerance (m)", "セルフオクルージョン許容 (m)", "자기 차폐 허용 (m)" },
            new[] { "高出平滑表面不到這個距離的部分視為表面本身。出現一格一格的陰影時稍微加大。", "Points less than this above the smooth surface count as the surface itself. Raise it slightly if blocky shading appears.", "なめらかな表面からこの距離未満の部分は表面そのものとみなします。ブロック状の影が出るときは少し大きくしてください。", "매끄러운 표면보다 이 거리 미만으로 높은 부분은 표면 자체로 봅니다. 계단 모양 그림자가 생기면 조금 늘리세요." },
            new[] { "前後距離 (m)", "Front distance (m)", "前後の距離 (m)", "앞뒤 거리 (m)" },
            new[] { "遮蔽物最遠可以離這一點多遠（例如稍微離開臉的頭髮、離開胸口的手）。只影響前後，影子往左右擴張的寬度由「範圍」決定。", "How far from the point an occluder may be (such as hair slightly off the face or a hand off the chest). It only reaches forward; how wide the shading spreads sideways is set by Reach.", "遮へい物がこの点からどこまで離れていてもよいか（顔から少し離れた髪、胸から離れた手など）。前後方向だけに効き、影が左右に広がる幅は「範囲」で決まります。", "차폐물이 이 지점에서 얼마나 떨어져 있어도 되는지 (얼굴에서 조금 떨어진 머리카락, 가슴에서 떨어진 손 등). 앞뒤 방향에만 적용되며, 그림자가 좌우로 퍼지는 폭은 \"범위\"가 정합니다." },
            new[] { "大範圍遮蔽強度", "Wide occlusion amount", "広範囲オクルージョンの強さ", "넓은 범위 차폐 강도" },
            new[] { "在「範圍」之外再加一圈較大的柔和遮蔽（例如手臂、頭髮在身體上的大片陰影）。0 = 關閉。", "Adds a larger, softer band of occlusion beyond Reach (such as broad shading from arms or hair on the body). 0 = off.", "「範囲」の外側に、より大きく柔らかい遮へいを追加します（腕や髪が体に落とす広い陰影など）。0 = オフ。", "\"범위\" 바깥에 더 크고 부드러운 차폐를 추가합니다 (팔이나 머리카락이 몸에 드리우는 넓은 음영 등). 0 = 끄기." },
            new[] { "大範圍遮蔽範圍 (m)", "Wide occlusion reach (m)", "広範囲オクルージョンの範囲 (m)", "넓은 범위 차폐 범위 (m)" },
            new[] { "邊緣漸層", "Edge gradient", "端のグラデーション", "가장자리 그라데이션" },
            new[] { "遮蔽往範圍邊緣淡出的柔和程度。0 = 在範圍邊緣明顯切斷、1 = 從中心開始逐漸變淡。", "How softly the occlusion fades out toward the edge of Reach. 0 = cut off sharply at the edge of Reach, 1 = fades gradually from the center.", "遮へいが範囲の端に向かってどれだけ柔らかく薄れるか。0 = 範囲の端ではっきり切れる、1 = 中心から徐々に薄くなる。", "차폐가 범위 끝으로 갈수록 얼마나 부드럽게 옅어지는지. 0 = 범위 끝에서 뚜렷하게 끊김, 1 = 중심부터 서서히 옅어짐." },
            // lilToon standard settings (Batch Settings), upload notice, parameters compression
            new[] { "lilToon 標準設定", "lilToon standard settings", "lilToon 標準設定", "lilToon 기본 설정" },
            new[] { "統一調整 lilToon 本身的設定（光照、陰影、邊緣光、背光、輪廓線、色調）。不會改動 Macaron 的設定。", "Adjust lilToon's own settings (lighting, shadow, rim light, backlight, outline, color) on all targets at once. Macaron settings are not changed.", "lilToon 本体の設定（ライティング・影・リムライト・逆光・輪郭線・色調）をまとめて調整します。Macaron の設定は変更しません。", "lilToon 자체 설정(라이팅, 그림자, 림 라이트, 역광, 윤곽선, 색조)을 한 번에 조정합니다. Macaron 설정은 바뀌지 않습니다." },
            new[] { "也套用到未轉換的 lilToon 材質（{0} 個）", "Also apply to unconverted lilToon materials ({0})", "未変換の lilToon マテリアルにも適用（{0} 個）", "변환하지 않은 lilToon 머티리얼에도 적용 ({0}개)" },
            new[] { "會修改 {0} 個 lilToon 材質。", "{0} lilToon materials will be changed.", "{0} 個の lilToon マテリアルを変更します。", "lilToon 머티리얼 {0}개를 수정합니다." },
            new[] { "沒有可修改的 lilToon 材質。請先在上面選擇角色、資料夾或整個專案。", "No lilToon materials to change. Pick an avatar, a folder or the whole project above first.", "変更できる lilToon マテリアルがありません。上でアバター、フォルダー、またはプロジェクト全体を選んでください。", "수정할 lilToon 머티리얼이 없습니다. 먼저 위에서 아바타, 폴더 또는 프로젝트 전체를 선택하세요." },
            new[] { "只會套用到有輪廓線的材質。", "Only applied to materials with an outline.", "輪郭線のあるマテリアルにだけ適用されます。", "윤곽선이 있는 머티리얼에만 적용됩니다." },
            new[] { "溫馨提示", "Heads-up", "お知らせ", "안내" },
            new[] { "上傳失敗：選單的參數太多了", "Upload failed: the menus use too many parameters", "アップロード失敗：メニューのパラメーターが多すぎます", "업로드 실패: 메뉴 파라미터가 너무 많습니다" },
            new[] { "VRChat 同步參數（Expression Parameters）的上限是 {0} bit。這個角色的選單、開關太多，超過了上限，所以無法上傳。", "VRChat allows {0} bits of synced parameters (Expression Parameters). This avatar has too many menus and toggles and goes over that limit, so it cannot be uploaded.", "VRChat の同期パラメーター（Expression Parameters）の上限は {0} bit です。このアバターはメニューやトグルが多すぎて上限を超えているため、アップロードできません。", "VRChat 동기화 파라미터(Expression Parameters)의 상한은 {0} bit입니다. 이 아바타는 메뉴와 토글이 너무 많아 상한을 넘었기 때문에 업로드할 수 없습니다." },
            new[] { "請刪減一些不需要的選單或開關，或是加入「parameters 壓縮」後再上傳一次。", "Remove some menus or toggles you don't need, or add \"parameters compression\" and upload again.", "不要なメニューやトグルを減らすか、「パラメーター圧縮」を追加してからもう一度アップロードしてください。", "필요 없는 메뉴나 토글을 줄이거나, \"파라미터 압축\"을 추가한 뒤 다시 업로드하세요." },
            new[] { "parameters 壓縮需要 NDMF（Modular Avatar）與 VRChat Avatars SDK。", "Parameters compression needs NDMF (Modular Avatar) and the VRChat Avatars SDK.", "パラメーター圧縮には NDMF（Modular Avatar）と VRChat Avatars SDK が必要です。", "파라미터 압축에는 NDMF(Modular Avatar)와 VRChat Avatars SDK가 필요합니다." },
            new[] { "這個角色已經加入 parameters 壓縮，但參數仍然超過上限。請刪減一些選單或開關。", "This avatar already has parameters compression, but it is still over the limit. Remove some menus or toggles.", "このアバターにはパラメーター圧縮が追加済みですが、まだ上限を超えています。メニューやトグルを減らしてください。", "이 아바타에는 이미 파라미터 압축이 추가되어 있지만 여전히 상한을 넘습니다. 메뉴나 토글을 줄이세요." },
            new[] { "這個角色已經加入 parameters 壓縮。", "This avatar already has parameters compression.", "このアバターにはパラメーター圧縮が追加済みです。", "이 아바타에는 이미 파라미터 압축이 추가되어 있습니다." },
            new[] { "這個角色的 Expression Parameters 檔案本身就已經使用 {0} / {1} bit。上傳前的檢查會直接看這個檔案，壓縮無法讓它通過，請先在這個檔案中刪減參數。", "The avatar's own Expression Parameters asset already uses {0} / {1} bits. The pre-upload check reads this asset directly and compression cannot get it through: remove parameters from this asset first.", "このアバターの Expression Parameters ファイル自体がすでに {0} / {1} bit を使っています。アップロード前のチェックはこのファイルを直接確認するため、圧縮では通過できません。先にこのファイルのパラメーターを減らしてください。", "이 아바타의 Expression Parameters 파일 자체가 이미 {0} / {1} bit를 사용합니다. 업로드 전 검사는 이 파일을 직접 확인하므로 압축으로는 통과할 수 없습니다. 먼저 이 파일에서 파라미터를 줄이세요." },
            new[] { "添加 parameters 壓縮", "Add parameters compression", "パラメーター圧縮を追加", "파라미터 압축 추가" },
            new[] { "關閉視窗", "Close", "ウィンドウを閉じる", "창 닫기" },
            new[] { "添加", "Add", "追加", "추가" },
            new[] { "已在「{0}」底下加入「{1}」。請再上傳一次。", "Added \"{1}\" under \"{0}\". Please upload again.", "「{0}」の下に「{1}」を追加しました。もう一度アップロードしてください。", "\"{0}\" 아래에 \"{1}\"을(를) 추가했습니다. 다시 업로드하세요." },
            new[] { "parameters 壓縮會把一部分同步參數改成「輪流同步」。加入前請先了解可能發生的問題：\n\n・其他玩家看到的開關、數值會比較慢才同步（約 0.1～0.2 秒 × 輪流的次數），快速切換時可能看起來延遲或跳過中間的變化。\n・Float 參數以 -1～1 的範圍同步，超出這個範圍的值會被截斷。\n・在 FX 動畫控制器中加入一個圖層；Write Defaults 依角色多數的設定決定。\n・與 VRCFury 的參數壓縮或其他參數壓縮工具一起使用時，可能互相衝突。\n・臉部追蹤（VRCFT）與 VRChat 內建參數不會被壓縮。\n\n只會在上傳時處理，不會修改原本的動畫或參數檔案；原本就沒有超過上限的角色不會被壓縮。刪除加入的物件即可還原。", "Parameters compression makes part of the synced parameters take turns on shared sync slots. Possible problems:\n\n・Other players see toggles and values update later (about 0.1–0.2 s × the number of turns); fast changes can look delayed or skip in-between values.\n・Float parameters are synced in the -1 to 1 range; values outside it are clipped.\n・One layer is added to the FX animator controller; Write Defaults follows most of the avatar's states.\n・It can conflict with VRCFury's parameter compressor or other compression tools.\n・Face tracking (VRCFT) and VRChat built-in parameters are never compressed.\n\nIt only runs while uploading and never changes your original animators or parameter assets. Avatars that are already within the limit are not compressed. Delete the added object to undo.", "パラメーター圧縮は、同期パラメーターの一部を「順番に同期」する方式に変えます。追加する前に、起こりうる問題を確認してください：\n\n・他のプレイヤーから見たトグルや値の反映が遅くなります（約 0.1～0.2 秒 × 順番の回数）。素早く切り替えると遅れて見えたり、途中の変化が飛ばされることがあります。\n・Float パラメーターは -1～1 の範囲で同期され、範囲外の値は切り捨てられます。\n・FX アニメーターコントローラーにレイヤーを 1 つ追加します。Write Defaults はアバターの多数派の設定に合わせます。\n・VRCFury のパラメーター圧縮や他の圧縮ツールと併用すると、競合することがあります。\n・フェイストラッキング（VRCFT）と VRChat の組み込みパラメーターは圧縮しません。\n\nアップロード時にだけ処理され、元のアニメーターやパラメーターファイルは変更しません。もともと上限を超えていないアバターは圧縮されません。追加したオブジェクトを削除すれば元に戻ります。", "파라미터 압축은 동기화 파라미터 일부를 \"번갈아 동기화\"하는 방식으로 바꿉니다. 추가하기 전에 발생할 수 있는 문제를 확인하세요:\n\n・다른 플레이어에게 보이는 토글과 값이 늦게 반영됩니다 (약 0.1~0.2초 × 순번 횟수). 빠르게 전환하면 지연되거나 중간 변화가 생략되어 보일 수 있습니다.\n・Float 파라미터는 -1~1 범위로 동기화되며, 범위를 벗어난 값은 잘립니다.\n・FX 애니메이터 컨트롤러에 레이어를 하나 추가합니다. Write Defaults는 아바타 다수의 설정을 따릅니다.\n・VRCFury의 파라미터 압축이나 다른 압축 도구와 함께 사용하면 충돌할 수 있습니다.\n・페이스 트래킹(VRCFT)과 VRChat 기본 파라미터는 압축하지 않습니다.\n\n업로드할 때만 처리되며 원래 애니메이터나 파라미터 파일은 수정하지 않습니다. 원래 상한을 넘지 않는 아바타는 압축하지 않습니다. 추가한 오브젝트를 삭제하면 되돌릴 수 있습니다." },
            new[] { "上傳時，只有同步參數超過 VRChat 上限（256 bit）才會壓縮；沒有超過時不做任何改變。不會修改原本的動畫或參數檔案，刪除這個物件即可還原。", "While uploading, parameters are compressed only when the synced parameters are over VRChat's limit (256 bits); otherwise nothing changes. Your original animators and parameter assets are never modified. Delete this object to undo.", "アップロード時、同期パラメーターが VRChat の上限（256 bit）を超えている場合だけ圧縮します。超えていなければ何も変更しません。元のアニメーターやパラメーターファイルは変更されません。このオブジェクトを削除すれば元に戻ります。", "업로드할 때 동기화 파라미터가 VRChat 상한(256 bit)을 넘을 때만 압축합니다. 넘지 않으면 아무것도 바꾸지 않습니다. 원래 애니메이터나 파라미터 파일은 수정하지 않으며, 이 오브젝트를 삭제하면 되돌릴 수 있습니다." },
            new[] { "最多輪流次數", "Max sync steps", "最大同期ステップ数", "최대 동기화 단계" },
            new[] { "會使用能低於上限的最少次數；次數越多，其他玩家看到的延遲越長。", "The fewest steps that fit within the limit are used. More steps = more delay for other players.", "上限内に収まる最少のステップ数を使います。多いほど他のプレイヤーから見た遅延が長くなります。", "상한 안에 들어가는 최소 단계 수를 사용합니다. 단계가 많을수록 다른 플레이어에게 보이는 지연이 길어집니다." },
            new[] { "不壓縮 Bool", "Don't compress Bool", "Bool を圧縮しない", "Bool 압축 안 함" },
            new[] { "不壓縮 Int", "Don't compress Int", "Int を圧縮しない", "Int 압축 안 함" },
            new[] { "不壓縮 Float", "Don't compress Float", "Float を圧縮しない", "Float 압축 안 함" },
            new[] { "不壓縮的名稱開頭", "Excluded name prefixes", "圧縮しない名前の先頭", "압축 제외 이름 접두사" },
            new[] { "不壓縮的名稱結尾", "Excluded name suffixes", "圧縮しない名前の末尾", "압축 제외 이름 접미사" },
            new[] { "參數使用量（預估）", "Parameter usage (estimate)", "パラメーター使用量（見積もり）", "파라미터 사용량 (예상)" },
            new[] { "重新計算", "Recalculate", "再計算", "다시 계산" },
            new[] { "壓縮前", "Before", "圧縮前", "압축 전" },
            new[] { "壓縮後", "After", "圧縮後", "압축 후" },
            new[] { "輪流同步用", "Turn-taking sync", "順番同期用", "교대 동기화용" },
            new[] { "上限", "Limit", "上限", "상한" },
            new[] { "沒有超過上限：上傳時不會壓縮。", "Within the limit: nothing is compressed while uploading.", "上限以内です：アップロード時に圧縮しません。", "상한 이내입니다: 업로드할 때 압축하지 않습니다." },
            new[] { "壓縮後仍超過上限，請刪減一些選單或開關，或調高「最多輪流次數」。", "Still over the limit after compression: remove some menu items or toggles, or raise \"Max sync steps\".", "圧縮後も上限を超えます。メニューやスイッチを減らすか、「最大同期ステップ数」を上げてください。", "압축 후에도 상한을 넘습니다. 메뉴나 스위치를 줄이거나 \"최대 동기화 단계\"를 올리세요." },
            new[] { "{0} 個 Int/Float、{1} 個 Bool 會分 {2} 次輪流同步。", "{0} Int/Float and {1} Bool parameters take turns in {2} steps.", "Int/Float {0} 個と Bool {1} 個を {2} 回に分けて順番に同期します。", "Int/Float {0}개와 Bool {1}개를 {2}번에 나눠 번갈아 동기화합니다." },
            new[] { "上次上傳：{0} → {1} bit", "Last upload: {0} → {1} bits", "前回のアップロード：{0} → {1} bit", "지난 업로드: {0} → {1} bit" },
            new[] { "預估值：依角色的 Expression Parameters 與 Modular Avatar 等工具提供的參數計算，實際以上傳時為準。", "Estimate from the avatar's Expression Parameters and the parameters reported by tools such as Modular Avatar; the upload decides the final result.", "アバターの Expression Parameters と、Modular Avatar などのツールが提供するパラメーターから計算した見積もりです。実際の結果はアップロード時に決まります。", "아바타의 Expression Parameters와 Modular Avatar 등 도구가 제공하는 파라미터로 계산한 예상치입니다. 실제 결과는 업로드할 때 정해집니다." },
            new[] { "預估值：只計算角色的 Expression Parameters 檔案（無法讀取其他工具的參數），實際以上傳時為準。", "Estimate from the avatar's Expression Parameters asset only (the parameters of other tools could not be read); the upload decides the final result.", "アバターの Expression Parameters ファイルだけから計算した見積もりです（他のツールのパラメーターは読み取れませんでした）。実際の結果はアップロード時に決まります。", "아바타의 Expression Parameters 파일만으로 계산한 예상치입니다 (다른 도구의 파라미터는 읽을 수 없었습니다). 실제 결과는 업로드할 때 정해집니다." },

            // Uzumore
            new[] { "埋入（鏡頭穿入）預設開啟，自己的視角預設不推回（需要光照調節器）。推回範圍依角色身高、圖層間距依網格自動設定。可在批次設定中開關。", "Uzumore (camera inside the mesh) is on by default and not applied to your own view (needs the Light Adjuster). The reach is set from the avatar's height and the layer offset per mesh. Switch it in Batch Settings.", "埋もれ（カメラのめり込み）は既定でオン、自分の視点には適用しません（ライト調整が必要）。押し戻す範囲はアバターの身長、レイヤー間隔はメッシュごとに自動設定します。一括設定でオン／オフできます。", "파묻힘(카메라 관통)은 기본으로 켜져 있고 내 시점에는 적용하지 않습니다 (라이트 조절기 필요). 범위는 아바타 키에 맞춰, 레이어 간격은 메시별로 자동 설정합니다. 일괄 설정에서 켜고 끌 수 있습니다." },
            new[] { "自己的視角也套用", "Also in your own view", "自分の視点にも適用", "내 시점에도 적용" },
            new[] { "預設關閉：只有別人的視角會推回。需要光照調節器（轉換時預設加入）；沒有光照調節器時自己的視角一律推回。", "Off by default: only other players' views are pushed back. Needs the Light Adjuster (added by the Converter by default); without it your own view is always pushed back.", "既定ではオフ：他のプレイヤーの視点だけ押し戻します。ライト調整が必要です（変換時に既定で追加）。ライト調整がない場合は自分の視点も常に押し戻します。", "기본은 꺼짐: 다른 플레이어의 시점만 밀어냅니다. 라이트 조절기가 필요합니다 (변환 시 기본으로 추가). 없으면 내 시점도 항상 밀어냅니다." },
            new[] { "沒有選擇任何光照項目：只加入埋入用的圖層（自己的視角不推回），不加入選單。", "No light item selected: only the Uzumore layer (own view not pushed back) is added, without a menu.", "ライト項目が選択されていません：埋もれ用のレイヤー（自分の視点は押し戻さない）だけを追加し、メニューは追加しません。", "선택한 라이트 항목이 없습니다: 파묻힘용 레이어(내 시점은 밀어내지 않음)만 추가하고 메뉴는 추가하지 않습니다." },
            new[] { "這個角色沒有 Macaron 材質，沒有設定埋入。", "This avatar has no Macaron materials: Uzumore was not set up.", "このアバターには Macaron マテリアルがないため、埋もれは設定されませんでした。", "이 아바타에는 Macaron 머티리얼이 없어 파묻힘을 설정하지 않았습니다." },
            new[] { "已設定 {0} 個材質的埋入（推回範圍 {1:0.00} m，可在批次設定中開關）。", "Uzumore set up on {0} materials (reach {1:0.00} m; switch it in Batch Settings).", "{0} 個のマテリアルに埋もれを設定しました（押し戻す範囲 {1:0.00} m、一括設定でオン／オフできます）。", "{0}개 머티리얼에 파묻힘을 설정했습니다 (범위 {1:0.00} m, 일괄 설정에서 켜고 끌 수 있습니다)." },
            new[] { "埋入（鏡頭穿入）", "Uzumore (camera inside the mesh)", "埋もれ（カメラのめり込み）", "파묻힘 (카메라 관통)" },
            new[] { "鏡頭（自己或其他玩家）鑽進網格時，把被穿過的表面推回鏡頭前方，就看不到網格內側。預設開啟；轉換器會依角色自動設定推回範圍與圖層間距。", "When a camera (yours or another player's) goes inside the mesh, the surface it went through is pushed back in front of it, so the inside of the mesh is not visible. On by default; the Converter sets the reach and layer offsets for each avatar.", "カメラ（自分や他のプレイヤー）がメッシュの内側に入ったとき、通り抜けた面をカメラの前に押し戻し、メッシュの内側が見えないようにします。既定でオン。押し戻す範囲とレイヤー間隔は変換時にアバターごとに自動設定されます。", "카메라(자신 또는 다른 플레이어)가 메시 안으로 들어가면, 통과한 면을 카메라 앞으로 밀어내 메시 안쪽이 보이지 않게 합니다. 기본으로 켜져 있으며, 범위와 레이어 간격은 변환할 때 아바타별로 자동 설정됩니다." },
            new[] { "推回範圍 (m)", "Push-back reach (m)", "押し戻す範囲 (m)", "밀어내기 범위 (m)" },
            new[] { "鏡頭穿入多深以內的表面會被推回。一般身高的角色約 0.1～0.2。太大時，貼在你背後的玩家視野可能被擋住。", "Surfaces the camera went past by up to this depth are pushed back. About 0.1–0.2 for an average-height avatar. Too large a value can block the view of a player right behind you.", "カメラがこの深さまでめり込んだ面を押し戻します。一般的な身長のアバターで 0.1～0.2 程度。大きすぎると、背後に密着したプレイヤーの視界をふさぐことがあります。", "카메라가 이 깊이까지 파고든 면을 밀어냅니다. 일반적인 키의 아바타는 약 0.1~0.2. 너무 크면 뒤에 바짝 붙은 플레이어의 시야를 가릴 수 있습니다." },
            new[] { "埋住時的亮度", "Brightness when buried", "埋もれたときの明るさ", "파묻혔을 때 밝기" },
            new[] { "被推到眼前的表面的亮度（1 = 不變暗）。", "Brightness of the surface pushed in front of the camera (1 = not darkened).", "カメラの前に押し戻された面の明るさ（1 = 暗くしない）。", "카메라 앞으로 밀려난 면의 밝기 (1 = 어둡게 하지 않음)." },
            new[] { "背面不推回", "Don't push the back side", "背面は押し戻さない", "뒷면은 밀어내지 않음" },
            new[] { "角色背面的表面不推回，避免擋住貼在你背後的玩家視野。依角色方向判斷。", "Surfaces on the back of the avatar are not pushed back, so a player right behind you is not blinded. Uses the avatar's facing direction.", "アバターの背面は押し戻さず、背後に密着したプレイヤーの視界をふさがないようにします。アバターの向きで判定します。", "아바타 뒷면은 밀어내지 않아 뒤에 바짝 붙은 플레이어의 시야를 가리지 않습니다. 아바타 방향으로 판단합니다." },
            new[] { "圖層間距 (m)", "Layer offset (m)", "レイヤー間隔 (m)", "레이어 간격 (m)" },
            new[] { "防止重疊的網格閃爍。越內層設越大，例如頭髮 0.001、衣服 0.0015、身體 0.002（轉換器會自動設定）。", "Keeps overlapping meshes from flickering. Larger for inner layers, e.g. hair 0.001, clothes 0.0015, body 0.002 (set automatically by the Converter).", "重なったメッシュのちらつきを防ぎます。内側ほど大きく、例：髪 0.001、服 0.0015、体 0.002（変換時に自動設定）。", "겹친 메시의 깜박임을 막습니다. 안쪽일수록 크게, 예: 머리카락 0.001, 옷 0.0015, 몸 0.002 (변환 시 자동 설정)." },
            new[] { "埋入遮罩", "Uzumore mask", "埋もれマスク", "파묻힘 마스크" },
            new[] { "白 = 有埋入效果（推回）、黑 = 排除（不推回）（R 通道）。可用遮罩生成器製作。", "White = Uzumore on (pushed back), black = excluded (not pushed back) (R channel). Can be made with the Mask Generator.", "白 = 埋もれ効果あり（押し戻す）、黒 = 対象外（押し戻さない）（R チャンネル）。マスク生成で作成できます。", "흰색 = 파묻힘 적용 (밀어냄), 검은색 = 제외 (밀어내지 않음) (R 채널). 마스크 생성기로 만들 수 있습니다." },
            new[] { "埋入遮罩強度", "Uzumore mask strength", "埋もれマスクの強さ", "파묻힘 마스크 강도" },
        };

        /// <summary>Every key of the table.</summary>
        public static IEnumerable<string> Keys { get { return Map.Keys; } }
    }

    //==============================================================================================
    // lilToon's own UI strings, for rows that edit lilToon's own settings (Batch Settings > lilToon standard settings),
    // so they read exactly like lilToon's Inspector in all four languages.
    //
    //   Language "Auto": lilToon's text in lilToon's current language (lilLanguageManager.GetLoc), identical to its Inspector.
    //   Fixed language : lilToon's translation file of that language (Editor/Localization/<code>.po of the lilToon package).
    // Keys follow lilToon's display names: "a+b" joins parts, "a|b|c" uses the first part. A key lilToon does not have is
    // shown as it is (lilToon does the same, e.g. "Shadow Caster Bias").
    //==============================================================================================
    public static class LilLoc
    {
        private const string LocalizationFolderGUID = "2feb2bcbf5b4ef043910b310c21b6ba7";   // lilToon/Editor/Localization
        private static readonly Dictionary<ML.Language, Dictionary<string, string>> tables = new Dictionary<ML.Language, Dictionary<string, string>>();

        /// <summary>Changes when the text of LilLoc.T may change (Macaron language setting, lilToon language).</summary>
        public static string Stamp
        {
            get
            {
                string lil = "";
                try { lil = global::lilToon.lilLanguageManager.langSet.languageName; } catch(Exception) { lil = ""; }
                return (int)ML.Setting + "/" + (int)ML.Current + "/" + lil;
            }
        }

        public static string T(string key)
        {
            if(string.IsNullOrEmpty(key)) return key;
            string label = key.Split('|')[0];
            string[] parts = label.Split('+');
            if(parts.Length == 1) return Get(label);
            var sb = new System.Text.StringBuilder();
            foreach(string part in parts) sb.Append(Get(part));
            return sb.ToString();
        }

        public static string[] TA(string[] keys)
        {
            if(keys == null) return null;
            string[] r = new string[keys.Length];
            for(int i = 0; i < keys.Length; i++) r[i] = T(keys[i]);
            return r;
        }

        private static string Get(string key)
        {
            string v;
            if(ML.Setting == ML.Language.Auto && TryLilToon(key, out v)) return v;
            Dictionary<string, string> table = Table(ML.Current);
            if(table != null && table.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            if(TryLilToon(key, out v)) return v;
            table = Table(ML.Language.English);
            if(table != null && table.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return key;
        }

        private static bool TryLilToon(string key, out string value)
        {
            value = null;
            try
            {
                value = global::lilToon.lilLanguageManager.GetLoc(key);
            }
            catch(Exception)
            {
                value = null;
            }
            return !string.IsNullOrEmpty(value) && value != key;
        }

        private static string FileCode(ML.Language lang)
        {
            switch(lang)
            {
                case ML.Language.Japanese: return "ja-JP";
                case ML.Language.Korean: return "ko-KR";
                case ML.Language.ChineseTraditional: return "zh-Hant";
                default: return "en-US";
            }
        }

        private static Dictionary<string, string> Table(ML.Language lang)
        {
            Dictionary<string, string> table;
            if(tables.TryGetValue(lang, out table)) return table;
            table = null;
            try
            {
                string folder = AssetDatabase.GUIDToAssetPath(LocalizationFolderGUID);
                if(string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                {
                    // Moved / re-imported lilToon: next to its language manager script.
                    foreach(string guid in AssetDatabase.FindAssets("lilLanguageManager t:MonoScript"))
                    {
                        string script = AssetDatabase.GUIDToAssetPath(guid);
                        string candidate = System.IO.Path.GetDirectoryName(script).Replace('\\', '/') + "/Localization";
                        if(AssetDatabase.IsValidFolder(candidate)) { folder = candidate; break; }
                    }
                }
                string path = folder + "/" + FileCode(lang) + ".po";
                if(!string.IsNullOrEmpty(folder) && System.IO.File.Exists(path)) table = ParsePo(System.IO.File.ReadAllText(path));
            }
            catch(Exception)
            {
                table = null;
            }
            tables[lang] = table;
            return table;
        }

        // Minimal gettext reader: msgid / msgstr pairs, continuation lines, \" \\ \n \t escapes.
        private static Dictionary<string, string> ParsePo(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string id = null;
            var current = new System.Text.StringBuilder();
            bool inId = false, inStr = false;
            System.Action flush = () =>
            {
                if(inStr && id != null && id.Length > 0) map[id] = current.ToString();
            };
            foreach(string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if(line.StartsWith("msgid ", StringComparison.Ordinal))
                {
                    flush();
                    inId = true; inStr = false; id = null;
                    current.Length = 0;
                    current.Append(Unquote(line.Substring(6)));
                }
                else if(line.StartsWith("msgstr ", StringComparison.Ordinal))
                {
                    id = current.ToString();
                    inId = false; inStr = true;
                    current.Length = 0;
                    current.Append(Unquote(line.Substring(7)));
                }
                else if(line.StartsWith("\"", StringComparison.Ordinal) && (inId || inStr))
                {
                    current.Append(Unquote(line));
                }
            }
            flush();
            return map;
        }

        private static string Unquote(string s)
        {
            s = s.Trim();
            if(s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            var sb = new System.Text.StringBuilder(s.Length);
            for(int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if(c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch(n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
#endif
