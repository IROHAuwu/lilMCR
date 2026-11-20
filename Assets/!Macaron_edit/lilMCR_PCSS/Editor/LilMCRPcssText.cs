#if UNITY_EDITOR
using System.Collections.Generic;
using liltoon_MCR;

namespace lilMCR.PCSS
{
    /// <summary>
    /// UI text of the add-on (Traditional Chinese / English / Japanese / Korean), following Macaron's language setting.
    /// Keys are the Chinese text; unknown keys are shown as they are.
    /// </summary>
    internal static class PcssText
    {
        /// <summary>Macaron's language selector (shared with Macaron). Returns true when it changed.</summary>
        public static bool LanguagePopup()
        {
            return ML.LanguagePopup();
        }

        public static string S(string zh)
        {
            ML.Language l = ML.Current;
            if(l == ML.Language.ChineseTraditional) return zh;
            string[] row;
            if(!Table.TryGetValue(zh, out row)) return zh;
            if(l == ML.Language.Japanese) return row[1];
            if(l == ML.Language.Korean && row.Length > 2) return row[2];
            return row[0];
        }

        public static string F(string zh, params object[] args)
        {
            try { return string.Format(S(zh), args); }
            catch(System.FormatException) { return string.Format(zh, args); }
        }

        // zh -> { en, ja, ko }
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "一鍵套用品質檔次", new[] { "One-click quality tier", "品質プリセットを一括適用", "품질 프리셋 한 번에 적용" } },
            { "PCSS：柔和度為 0 時只使用最小模糊；大於 0 時離遮擋物越遠越柔。PCF：柔和度是整體固定的寬度（相當於 PCSS 在遮擋物距離 0.1 m 時），不使用遮擋物搜尋範圍。最小模糊以陰影貼圖像素計算。", new[] { "PCSS: with Softness 0 only the minimum blur is used; above 0 the shadow gets softer the further it is from the occluder. PCF: Softness is one fixed width everywhere (what PCSS gives an occluder 0.1 m away) and the Blocker Search Range is not used. Minimum blur is in shadow map texels.", "PCSS：柔らかさが 0 のときは最小ぼかしのみ、0 より大きいと遮蔽物から離れるほど柔らかくなります。PCF：柔らかさは全体で一定の幅（PCSS で遮蔽物が 0.1 m 離れているときの幅）になり、遮蔽物の探索範囲は使いません。最小ぼかしはシャドウマップのテクセル単位です。", "PCSS: 부드러움이 0이면 최소 블러만 사용하고, 0보다 크면 가리는 물체에서 멀수록 부드러워집니다. PCF: 부드러움은 전체에 고정된 폭(PCSS에서 가리는 물체가 0.1 m 떨어졌을 때의 폭)이며 차폐물 탐색 범위는 사용하지 않습니다. 최소 블러는 섀도 맵 텍셀 단위입니다." } },
            // Shared self-shadow setting (Setup, Batch and material inspector).
            { "僅自身陰影", new[] { "Self Shadows Only", "自身の影のみ", "자신의 그림자만" } },
            { "角色共用：修改此項會同步所有 PCSS 材質。",
              new[] { "Shared across the avatar: changing this setting updates all PCSS materials.",
                      "アバター共通：変更すると、すべての PCSS マテリアルに同期されます。", "아바타 공통: 이 항목을 바꾸면 모든 PCSS 머티리얼에 동기화됩니다." } },
            // Generator
            { "找不到 Macaron（lilCustomShaderDatas.lilblock 中 ShaderName 為「{0}」的資料夾）。請先匯入 Macaron。",
              new[] { "Macaron was not found (no folder whose lilCustomShaderDatas.lilblock has ShaderName \"{0}\"). Import Macaron first.",
                      "Macaron が見つかりません（lilCustomShaderDatas.lilblock の ShaderName が「{0}」のフォルダがありません）。先に Macaron をインポートしてください。", "Macaron을 찾을 수 없습니다 (lilCustomShaderDatas.lilblock의 ShaderName이 \"{0}\"인 폴더가 없음). 먼저 Macaron을 임포트하세요." } },
            { "找不到外掛的 Templates 資料夾。請保持 lilMCR_PCSS 資料夾的結構。",
              new[] { "The add-on's Templates folder was not found. Keep the lilMCR_PCSS folder structure as it is.",
                      "アドオンの Templates フォルダが見つかりません。lilMCR_PCSS フォルダの構成を変更しないでください。", "애드온의 Templates 폴더를 찾을 수 없습니다. lilMCR_PCSS 폴더 구조를 그대로 유지하세요." } },
            { "已從 {0} 產生 lilMCR PCSS 著色器（更新 {1} 個檔案，刪除 {2} 個）。",
              new[] { "lilMCR PCSS shaders generated from {0} ({1} files updated, {2} removed).",
                      "{0} から lilMCR PCSS シェーダーを生成しました（{1} ファイル更新、{2} 削除）。", "{0}에서 lilMCR PCSS 셰이더를 생성했습니다 (파일 {1}개 업데이트, {2}개 삭제)." } },
            { "lilMCR PCSS 著色器已是最新（來源：{0}）。",
              new[] { "lilMCR PCSS shaders are up to date (source: {0}).",
                      "lilMCR PCSS シェーダーは最新です（ソース：{0}）。", "lilMCR PCSS 셰이더가 최신 상태입니다 (소스: {0})." } },
            { "找到多個 Macaron 資料夾，使用：{0}",
              new[] { "Several Macaron folders were found, using: {0}",
                      "Macaron フォルダが複数見つかりました。使用するフォルダ：{0}", "Macaron 폴더가 여러 개 있습니다. 사용하는 폴더: {0}" } },
            { "PCSS 自身陰影需要 custom.hlsl 與 custom_insert.hlsl。",
              new[] { "PCSS self shadows require custom.hlsl and custom_insert.hlsl.",
                      "PCSS のセルフシャドウには custom.hlsl と custom_insert.hlsl が必要です。", "PCSS 자기 그림자에는 custom.hlsl과 custom_insert.hlsl이 필요합니다." } },
            { "PCSS 需要 custom.hlsl 中恰好有一個「#define LIL_CUSTOM_PROPERTIES」。",
              new[] { "PCSS requires exactly one \"#define LIL_CUSTOM_PROPERTIES\" in custom.hlsl.",
                      "PCSS には custom.hlsl に「#define LIL_CUSTOM_PROPERTIES」がちょうど 1 つ必要です。", "PCSS에는 custom.hlsl에 \"#define LIL_CUSTOM_PROPERTIES\"가 정확히 하나 필요합니다." } },
            // Inspector
            { "lilMCR PCSS（柔和影子）", new[] { "lilMCR PCSS (soft shadows)", "lilMCR PCSS（ソフトシャドウ）", "lilMCR PCSS (부드러운 그림자)" } },
            { "使用 Macaron > lilMCR PCSS > Setup 為角色建立專用光源，用「Batch Settings」一次調整整個角色。",
              new[] { "Create the avatar's dedicated light with Macaron > lilMCR PCSS > Setup; adjust the whole avatar at once with \"Batch Settings\".",
                      "Macaron > lilMCR PCSS > Setup でアバター専用ライトを作成し、「Batch Settings」でアバター全体をまとめて調整できます。", "Macaron > lilMCR PCSS > Setup에서 아바타 전용 라이트를 만들고, \"Batch Settings\"로 아바타 전체를 한 번에 조정하세요." } },
            { "影子", new[] { "Shadow", "影", "그림자" } },
            { "進階", new[] { "Advanced", "詳細", "고급" } },
            // PCSS mask
            { "遮罩", new[] { "Mask", "マスク", "마스크" } },
            { "PCSS 遮罩", new[] { "PCSS Mask", "PCSS マスク", "PCSS 마스크" } },
            { "白色 = 有 PCSS 影子／黑色 = 沒有 PCSS 影子（R 通道，主貼圖 UV）。",
              new[] { "White = PCSS shadow / black = no PCSS shadow (red channel, main texture UV).",
                      "白 = PCSS の影あり／黒 = PCSS の影なし（R チャンネル、メインテクスチャの UV）。",
                      "흰색 = PCSS 그림자 있음 / 검은색 = PCSS 그림자 없음 (R 채널, 메인 텍스처 UV)." } },
            { "只影響 PCSS 影子；Macaron 的全域遮罩仍會一起套用。可用遮罩生成器的「PCSS 遮罩」生成並自動套用。",
              new[] { "Affects only the PCSS shadow; Macaron's global mask still applies as well. The Mask Generator's \"PCSS mask\" target creates and applies it automatically.",
                      "PCSS の影だけに効きます。Macaron の全体マスクも引き続き適用されます。マスク生成の「PCSS マスク」で生成・自動適用できます。",
                      "PCSS 그림자에만 적용됩니다. Macaron의 전체 마스크도 함께 적용됩니다. 마스크 생성기의 \"PCSS 마스크\"로 생성하고 자동 적용할 수 있습니다." } },
            { "開啟遮罩生成器", new[] { "Open Mask Generator", "マスク生成を開く", "마스크 생성기 열기" } },
            { "請匯入獨立的 lilMCR_MaskGenerator 資料夾以使用此功能。",
              new[] { "Import the separate lilMCR_MaskGenerator folder to use this.",
                      "この機能を使うには、別配布の lilMCR_MaskGenerator フォルダをインポートしてください。",
                      "이 기능을 사용하려면 별도의 lilMCR_MaskGenerator 폴더를 임포트하세요." } },
            { "專用光源 ID：{0}　範圍：{1:0.00} m", new[] { "Dedicated light ID: {0}   Reach: {1:0.00} m", "専用ライト ID：{0}　範囲：{1:0.00} m", "전용 라이트 ID: {0}　범위: {1:0.00} m" } },
            { "這個材質還沒有專用光源（請執行 Setup）。", new[] { "This material has no dedicated light yet (run Setup).", "このマテリアルにはまだ専用ライトがありません（Setup を実行してください）。", "이 머티리얼에는 아직 전용 라이트가 없습니다 (Setup을 실행하세요)." } },

            // Batch Settings
            { "通常不需要按：匯入新版 lilMCR 時會自動重新產生 PCSS 版著色器。只有更新後影子消失、材質變成粉紅色，或 Console 出現 lilMCR PCSS 的警告時才需要手動按一次。", new[] { "Usually not needed: the PCSS shaders are regenerated automatically when a new lilMCR is imported. Press it only if the shadows disappear, materials turn pink, or a lilMCR PCSS warning appears in the Console after an update.", "通常は押す必要はありません。新しい lilMCR をインポートすると PCSS 版シェーダーは自動で再生成されます。更新後に影が消えた、マテリアルがピンクになった、Console に lilMCR PCSS の警告が出た場合のみ手動で押してください。", "보통은 누를 필요가 없습니다: 새 lilMCR을 임포트하면 PCSS 셰이더가 자동으로 다시 생성됩니다. 업데이트 후 그림자가 사라지거나, 머티리얼이 분홍색이 되거나, Console에 lilMCR PCSS 경고가 나올 때만 한 번 눌러 주세요." } },
            { "移除（還原）", new[] { "Remove (revert)", "削除（元に戻す）", "제거 (되돌리기)" } },
            { "材質切回原本的 Macaron，並刪除專用光源。可用 Ctrl+Z 復原。", new[] { "Switches the materials back to the original Macaron and deletes the dedicated light. Ctrl+Z to undo.", "マテリアルを元の Macaron に戻し、専用ライトを削除します。Ctrl+Z で元に戻せます。", "머티리얼을 원래 Macaron으로 되돌리고 전용 라이트를 삭제합니다. Ctrl+Z로 되돌릴 수 있습니다." } },
            { "同時刪除這個角色產生的檔案（cookie 等，無法復原）", new[] { "Also delete the files generated for this avatar (cookie etc., cannot be undone)", "このアバター用に生成したファイルも削除（cookie など、元に戻せません）", "이 아바타용으로 생성된 파일도 함께 삭제 (cookie 등, 되돌릴 수 없음)" } },
            { "移除 lilMCR PCSS", new[] { "Remove lilMCR PCSS", "lilMCR PCSS を削除", "lilMCR PCSS 제거" } },
            { "從「{0}」移除 lilMCR PCSS？", new[] { "Remove lilMCR PCSS from \"{0}\"?", "「{0}」から lilMCR PCSS を削除しますか？", "\"{0}\"에서 lilMCR PCSS를 제거할까요?" } },
            { "移除", new[] { "Remove", "削除", "제거" } },
            { "lilMCR 功能", new[] { "lilMCR features", "lilMCR の機能", "lilMCR 기능" } },
            { "接觸陰影", new[] { "Contact shadow", "接触影", "접촉 그림자" } },
            { "效能消耗非常高，且和 PCSS 的功能相近，建議關閉。", new[] { "Very expensive and similar to PCSS: turning it off is recommended.", "負荷が非常に高く、PCSS と機能が近いため、オフを推奨します。", "성능 부담이 매우 크고 PCSS와 기능이 비슷하므로 끄는 것을 권장합니다." } },
            { "全體 AO（環境光遮蔽）", new[] { "Global AO (ambient occlusion)", "全体 AO（環境遮蔽）", "전체 AO (앰비언트 오클루전)" } },
            { "可依喜好開或關。", new[] { "Optional: turn it on or off as you like.", "お好みでオン／オフしてください。", "취향에 따라 켜거나 끄세요." } },
            { "開關：跟隨 Macaron 總開關（目前關閉）", new[] { "Switch: follows Macaron's master switch (currently off)", "スイッチ：Macaron のメインスイッチに連動（現在オフ）", "스위치: Macaron 전체 스위치를 따름 (현재 꺼짐)" } },
            { "開關：跟隨 Macaron 總開關（目前開啟）", new[] { "Switch: follows Macaron's master switch (currently on)", "スイッチ：Macaron のメインスイッチに連動（現在オン）", "스위치: Macaron 전체 스위치를 따름 (현재 켜짐)" } },
            { "光源方向", new[] { "Light direction", "ライトの方向", "광원 방향" } },
            { "專用光源的柔和落影。選擇角色，按一下就完成。", new[] { "Soft cast shadows from a dedicated light. Pick the avatar and apply.", "専用ライトによるやわらかな落ち影。アバターを選んで適用するだけ。", "전용 라이트로 만드는 부드러운 그림자. 아바타를 고르고 버튼 한 번이면 끝납니다." } },
            { "拖動圓盤調整專用光源的方向（以角色為基準）。", new[] { "Drag on the disc to set the dedicated light's direction (relative to the avatar).", "円盤をドラッグして専用ライトの方向を調整します（アバター基準）。", "원판을 드래그해 전용 라이트의 방향을 정합니다 (아바타 기준)." } },
            { "選擇角色", new[] { "Select the avatar", "アバターを選択", "아바타 선택" } },
            { "編輯器預覽：開（點擊關閉）", new[] { "Editor preview: ON (click to turn off)", "エディタープレビュー：オン（クリックでオフ）", "에디터 미리보기: 켜짐 (클릭해서 끄기)" } },
            { "編輯器預覽：關（點擊在 Unity 中預覽影子）", new[] { "Editor preview: OFF (click to preview the shadows in Unity)", "エディタープレビュー：オフ（クリックで Unity 上で影をプレビュー）", "에디터 미리보기: 꺼짐 (클릭해서 Unity에서 그림자 미리보기)" } },
            { "開啟後光源也會作用在 Default 圖層（Unity 編輯器中的角色）。關閉統一調整視窗或按「套用」時會自動關閉。", new[] { "While on, the light also reaches the Default layer (the avatar in the Unity editor). It turns off automatically when Batch Settings is closed or Apply is pressed.", "オンの間はライトが Default レイヤー（Unity エディター上のアバター）にも作用します。一括調整ウィンドウを閉じるか「適用」を押すと自動でオフになります。", "켜 두면 라이트가 Default 레이어 (Unity 에디터의 아바타)에도 적용됩니다. 일괄 설정 창을 닫거나 \"적용\"을 누르면 자동으로 꺼집니다." } },
            { "沿用底色", new[] { "Use base color", "下地の色を使う", "베이스 색 사용" } },
            { "影子帶上材質本身的顏色（色相），不會變成灰色。0 = 只用影子顏色。", new[] { "The shadow takes the material's own color (hue) instead of turning gray. 0 = shadow color only.", "影にマテリアル自身の色（色相）を乗せ、灰色になりません。0 = 影の色のみ。", "그림자에 머티리얼 자체의 색 (색상)을 입혀 회색이 되지 않게 합니다. 0 = 그림자 색만 사용." } },
            { "影子飽和度", new[] { "Shadow saturation", "影の彩度", "그림자 채도" } },
            { "PCF（輕量）", new[] { "PCF (light)", "PCF（軽量）", "PCF (경량)" } },
            { "影子類型", new[] { "Shadow type", "影の種類", "그림자 종류" } },
            { "PCSS：離遮擋物越遠越柔和。PCF：整體同一個柔和度，較省效能。兩種共用同一張陰影貼圖和下面的拉桿。", new[] { "PCSS: softer the further from the occluder. PCF: the same softness everywhere, cheaper. Both share the shadow map and the sliders below.", "PCSS：遮蔽物から離れるほど柔らかくなります。PCF：全体が同じ柔らかさで、より軽量です。どちらも同じシャドウマップと下のスライダーを共用します。", "PCSS: 가리는 물체에서 멀수록 부드러워집니다. PCF: 전체가 같은 부드러움으로 더 가볍습니다. 둘 다 같은 섀도 맵과 아래 슬라이더를 공유합니다." } },
            { "半影著色", new[] { "Penumbra tint", "半影の色付け", "반그림자 색 입히기" } },
            { "半影沿用底色", new[] { "Penumbra uses base color", "半影に下地の色を使う", "반그림자에 베이스 색 사용" } },
            { "半影飽和度", new[] { "Penumbra saturation", "半影の彩度", "반그림자 채도" } },
            { "半影的顏色來源。1 = 只用材質本身的底色，0 = 只用下面兩個自選顏色，中間 = 兩者混合。", new[] { "Where the penumbra color comes from. 1 = the material's own base color only, 0 = the two chosen colors below only, in between = a mix of both.", "半影の色の元。1 = マテリアル自身の下地の色のみ、0 = 下の 2 つの指定色のみ、その間 = 両方の混合。", "반그림자 색의 출처. 1 = 머티리얼 자체의 베이스 색만, 0 = 아래의 두 지정 색만, 그 사이 = 둘의 혼합." } },
            { "半影顏色的飽和度。0 = 中性（不染色），1 = 顏色本身，越大越鮮豔。只用底色時調高這個值，邊緣的色調才會明顯。", new[] { "Saturation of the penumbra color. 0 = neutral (no tint), 1 = the color as is; higher = more vivid. With the base color only, raise this to make the edge's hue show.", "半影の色の彩度。0 = 無彩色（色が付かない）、1 = 色そのまま、大きいほど鮮やか。下地の色だけを使う場合は、この値を上げると縁の色合いがはっきりします。", "반그림자 색의 채도. 0 = 무채색(색이 입혀지지 않음), 1 = 색 그대로, 클수록 선명함. 베이스 색만 쓸 때는 이 값을 올려야 가장자리 색조가 잘 보입니다." } },
            { "半影顏色（亮側）", new[] { "Penumbra color (light side)", "半影の色（明るい側）", "반그림자 색 (밝은 쪽)" } },
            { "半影顏色（影側）", new[] { "Penumbra color (shadow side)", "半影の色（影側）", "반그림자 색 (그림자 쪽)" } },
            { "影子邊緣（亮面與影子之間的過渡帶）染上半影顏色的程度。0 = 關閉。過渡帶越寬（柔和度越大、離遮擋物越遠）越明顯。", new[] { "How much the edge of the shadow (the band between light and shadow) takes the penumbra color. 0 = off. The wider the band (more softness, further from the occluder), the more it shows.", "影の縁（明るい面と影のあいだの帯）に半影の色を乗せる強さ。0 = オフ。帯が広いほど（柔らかさが大きい、遮蔽物から遠い）はっきり見えます。", "그림자 가장자리(밝은 면과 그림자 사이의 띠)에 반그림자 색을 입히는 정도. 0 = 끔. 띠가 넓을수록(부드러움이 크거나 가리는 물체에서 멀수록) 잘 보입니다." } },
            { "影子邊緣靠近亮面那一側的色調。只取色相，不會讓畫面變亮。", new[] { "Hue of the shadow's edge on the side of the light. Only the hue is used: nothing gets brighter.", "影の縁の、明るい面に近い側の色合い。色相だけを使い、明るくはなりません。", "그림자 가장자리 중 밝은 면에 가까운 쪽의 색조. 색상만 사용하며 밝아지지는 않습니다." } },
            { "影子邊緣靠近影子那一側的色調。只取色相，不會讓畫面變亮。", new[] { "Hue of the shadow's edge on the side of the shadow. Only the hue is used: nothing gets brighter.", "影の縁の、影に近い側の色合い。色相だけを使い、明るくはなりません。", "그림자 가장자리 중 그림자에 가까운 쪽의 색조. 색상만 사용하며 밝아지지는 않습니다." } },
            { "影子顏色的飽和度。0 = 中性（只變暗，保留原本的顏色），1 = 沿用的顏色本身，越大越鮮豔。", new[] { "Saturation of the shadow color. 0 = neutral (only darkens, the surface keeps its own color), 1 = the color as is; higher = more vivid.", "影の色の彩度。0 = ニュートラル（暗くするだけで元の色を保つ）、1 = 色そのまま、大きいほど鮮やかになります。", "그림자 색의 채도. 0 = 중립 (어둡게만 하고 원래 색 유지), 1 = 색 그대로, 클수록 선명해집니다." } },
            { "落影處乘上的顏色（亮度與色調）。越暗影子越深。", new[] { "Color multiplied into the cast shadow (brightness and tone). Darker = deeper shadows.", "落ち影に乗算する色（明るさと色調）。暗いほど影が濃くなります。", "그림자가 지는 곳에 곱하는 색 (밝기와 색조). 어두울수록 그림자가 진해집니다." } },
            { "照射範圍", new[] { "Coverage", "照射範囲", "조사 범위" } },
            { "光錐涵蓋角色的範圍。越小，同樣的解析度下影子越細緻；太小時手腳、頭頂、尾巴等會超出範圍而沒有影子。", new[] { "How much of the avatar the light cone covers. Smaller = finer shadows at the same resolution; too small and hands, feet, the head top or a tail fall outside and get no shadow.", "ライトのコーンがアバターを覆う範囲。小さいほど同じ解像度で影が細かくなりますが、小さすぎると手足・頭頂・しっぽなどが範囲外になり影が付きません。", "라이트 원뿔이 아바타를 덮는 범위. 작을수록 같은 해상도에서 그림자가 세밀해지지만, 너무 작으면 손발, 머리 위, 꼬리 등이 범위를 벗어나 그림자가 생기지 않습니다." } },
            { "目前精度：約 {0:0.00} mm / 像素", new[] { "Current precision: about {0:0.00} mm per texel", "現在の精度：約 {0:0.00} mm / テクセル", "현재 정밀도: 약 {0:0.00} mm / 텍셀" } },
            { "背光面的影子", new[] { "Shadow on back faces", "背面の影", "역광면 그림자" } },
            { "背對專用光源的面也加上影子。1 = 背光面也有影子，0 = 只在受光面顯示落影。", new[] { "Also shadow surfaces facing away from the dedicated light. 1 = back faces are shadowed too, 0 = cast shadows only on lit surfaces.", "専用ライトに背を向けた面にも影を付けます。1 = 背面にも影、0 = 光が当たる面だけに落ち影。", "전용 라이트를 등진 면에도 그림자를 넣습니다. 1 = 역광면에도 그림자, 0 = 빛을 받는 면에만 그림자." } },
            { "開啟統一調整", new[] { "Open Batch Settings", "一括調整を開く", "일괄 설정 열기" } },
            { "lilMCR PCSS 統一調整", new[] { "lilMCR PCSS Batch Settings", "lilMCR PCSS 一括調整", "lilMCR PCSS 일괄 설정" } },
            { "一次調整角色所有 lilMCR PCSS 材質的設定，也可以調整影子顏色與光源方向。", new[] { "Adjust every lilMCR PCSS material of the avatar at once, including the shadow color and the light direction.", "アバターのすべての lilMCR PCSS マテリアルをまとめて調整します。影の色とライトの方向も変更できます。", "아바타의 모든 lilMCR PCSS 머티리얼 설정을 한 번에 조정하며, 그림자 색과 광원 방향도 바꿀 수 있습니다." } },
            { "這個角色還沒有套用 lilMCR PCSS。請先在 Setup 按「套用」。", new[] { "lilMCR PCSS is not set up on this avatar yet. Press Apply in Setup first.", "このアバターにはまだ lilMCR PCSS が適用されていません。先に Setup で「適用」を押してください。", "이 아바타에는 아직 lilMCR PCSS가 적용되지 않았습니다. 먼저 Setup에서 \"적용\"을 누르세요." } },
            { "開啟 Setup", new[] { "Open Setup", "Setup を開く", "Setup 열기" } },
            { "全部恢復預設值", new[] { "Reset all to defaults", "すべて初期値に戻す", "모두 기본값으로 되돌리기" } },
            { "把 {0} 個材質的 lilMCR PCSS 設定恢復成預設值？（可用 Ctrl+Z 復原）", new[] { "Reset the lilMCR PCSS settings of {0} materials to the defaults? (Ctrl+Z to undo)", "{0} 個のマテリアルの lilMCR PCSS 設定を初期値に戻しますか？（Ctrl+Z で元に戻せます）", "머티리얼 {0}개의 lilMCR PCSS 설정을 기본값으로 되돌릴까요? (Ctrl+Z로 되돌릴 수 있습니다)" } },
            { "取消", new[] { "Cancel", "キャンセル", "취소" } },
            { "調整對象：{0} / {1} 個材質", new[] { "Targets: {0} / {1} materials", "対象：{0} / {1} 個のマテリアル", "조정 대상: {0} / {1}개 머티리얼" } },
            { "影子顏色", new[] { "Shadow color", "影の色", "그림자 색" } },
            { "影子強度", new[] { "Shadow strength", "影の強さ", "그림자 강도" } },
            { "柔和度（光源大小）", new[] { "Softness (light size)", "柔らかさ（光源サイズ）", "부드러움 (광원 크기)" } },
            { "越小影子越銳利、越接近遮擋物的形狀；越大離遮擋物越遠的地方越模糊。", new[] { "Smaller = sharper shadows that follow the occluder's shape; larger = more blur farther from the occluder.", "小さいほど影がシャープで遮蔽物の形に近くなり、大きいほど遮蔽物から離れた所がぼけます。", "작을수록 그림자가 선명하고 차폐물 모양에 가까워지며, 클수록 차폐물에서 먼 곳이 더 흐려집니다." } },
            { "遮擋物搜尋範圍", new[] { "Occluder search range", "遮蔽物の探索範囲", "차폐물 탐색 범위" } },
            { "品質", new[] { "Quality", "品質", "품질" } },
            { "最小模糊", new[] { "Minimum blur", "最小ぼかし", "최소 블러" } },
            { "貼近遮擋物處的最小模糊（陰影貼圖像素）。0 = 最銳利（邊緣仍會平滑處理）。", new[] { "Minimum blur next to the occluder (shadow map texels). 0 = sharpest (edges are still smoothed).", "遮蔽物に接する部分の最小ぼかし（シャドウマップのテクセル）。0 = 最もシャープ（エッジは滑らかに処理されます）。", "차폐물에 가까운 곳의 최소 블러 (그림자 맵 텍셀). 0 = 가장 선명 (가장자리는 여전히 부드럽게 처리됨)." } },
            { "法線偏移", new[] { "Normal bias", "法線バイアス", "노멀 바이어스" } },
            { "基礎偏移為 0.002 m；此值為額外偏移，0 即使用基礎值。條紋出現時調高，影子分離時調低。", new[] { "Base bias is 0.002 m. This value adds extra bias; 0 uses the base value. Increase for stripes; decrease if shadows detach.", "基本バイアスは 0.002 m です。この値は追加分で、0 では基本値を使用します。縞が出る場合は上げ、影が離れる場合は下げてください。", "기본 바이어스는 0.002 m이며, 이 값은 추가 바이어스입니다. 0이면 기본값만 사용합니다. 줄무늬가 보이면 올리고, 그림자가 떨어져 보이면 낮추세요." } },
            { "深度偏移", new[] { "Depth bias", "深度バイアス", "깊이 바이어스" } },
            { "光錐邊緣柔和度", new[] { "Cone edge softness", "コーン境界の柔らかさ", "원뿔 가장자리 부드러움" } },
            { "陰影貼圖解析度", new[] { "Shadow map resolution", "シャドウマップ解像度", "그림자 맵 해상도" } },
            { "解析度越高，影子越接近遮擋物的形狀；但看到你的每個人都要多用顯示記憶體與效能。", new[] { "Higher resolution makes the shadow follow the occluder's shape more closely, but costs video memory and performance for everyone who sees you.", "解像度が高いほど影が遮蔽物の形に近くなりますが、あなたを見る全員の VRAM とパフォーマンスを消費します。", "해상도가 높을수록 그림자가 차폐물 모양에 가까워지지만, 당신을 보는 모든 사람의 VRAM과 성능을 더 사용합니다." } },
            // Setup window
            { "請選擇場景中的角色。", new[] { "Select an avatar in the scene.", "シーン内のアバターを選択してください。", "씬의 아바타를 선택하세요." } },
            { "著色器", new[] { "Shaders", "シェーダー", "셰이더" } },
            { "Macaron 來源：{0}", new[] { "Macaron source: {0}", "Macaron ソース：{0}", "Macaron 소스: {0}" } },
            { "重新產生著色器", new[] { "Regenerate shaders", "シェーダーを再生成", "셰이더 다시 생성" } },
            { "這個角色沒有 Macaron 材質。請先用 Macaron 的轉換工具轉換。",
              new[] { "This avatar has no Macaron materials. Convert it with Macaron's converter first.",
                      "このアバターには Macaron マテリアルがありません。先に Macaron の変換ツールで変換してください。", "이 아바타에는 Macaron 머티리얼이 없습니다. 먼저 Macaron 변환 도구로 변환하세요." } },
            { "加入專用聚光燈（極弱的光，對其他物體和玩家看不出來）並把材質切換成 PCSS 版本。增加 1 盞燈（Performance Rank）。",
              new[] { "Adds a dedicated spot light (extremely weak, invisible to everything else) and switches the materials to the PCSS versions. Adds 1 light (Performance Rank).",
                      "専用スポットライト（ごく弱い光で、他のオブジェクトやプレイヤーには見えません）を追加し、マテリアルを PCSS 版に切り替えます。ライトが 1 つ増えます（Performance Rank）。", "전용 스포트라이트 (아주 약한 빛이라 다른 물체나 플레이어에게는 보이지 않음)를 추가하고 머티리얼을 PCSS 버전으로 바꿉니다. 라이트가 1개 늘어납니다 (Performance Rank)." } },
            { "套用", new[] { "Apply", "適用", "적용" } },
            { "已套用 lilMCR PCSS：{0} 個材質，光源 ID {1}。", new[] { "lilMCR PCSS applied: {0} materials, light ID {1}.", "lilMCR PCSS を適用しました：{0} 個のマテリアル、ライト ID {1}。", "lilMCR PCSS를 적용했습니다: 머티리얼 {0}개, 라이트 ID {1}." } },
            { "已移除 lilMCR PCSS：{0} 個材質切回 Macaron。", new[] { "lilMCR PCSS removed: {0} materials back to Macaron.", "lilMCR PCSS を削除しました：{0} 個のマテリアルを Macaron に戻しました。", "lilMCR PCSS를 제거했습니다: 머티리얼 {0}개를 Macaron으로 되돌렸습니다." } },
            { "找不到 PCSS 版本的著色器：{0}。請按「重新產生著色器」。",
              new[] { "PCSS version of the shader not found: {0}. Press \"Regenerate shaders\".",
                      "PCSS 版のシェーダーが見つかりません：{0}。「シェーダーを再生成」を押してください。", "PCSS 버전 셰이더를 찾을 수 없습니다: {0}. \"셰이더 다시 생성\"을 누르세요." } },
            { "這些材質也被其他物件使用，它們會一起變成 PCSS 版本：{0}。若只想改這個角色，請先讓它使用自己的材質副本。",
              new[] { "These materials are also used by other objects, which switch to PCSS as well: {0}. To change only this avatar, give it its own copies of the materials first.",
                      "これらのマテリアルは他のオブジェクトでも使われているため、一緒に PCSS 版になります：{0}。このアバターだけを変更したい場合は、先にマテリアルを複製して割り当ててください。", "이 머티리얼은 다른 오브젝트에서도 사용 중이라 함께 PCSS 버전으로 바뀝니다: {0}. 이 아바타만 바꾸려면 먼저 머티리얼 복사본을 지정하세요." } },
            { "注意：重新執行 Macaron 的轉換後，材質會回到原本的 Macaron 著色器，請再按一次套用。",
              new[] { "Note: after running Macaron's converter again the materials are back on the original Macaron shaders; press Apply again.",
                      "注意：Macaron の変換を再実行するとマテリアルは元の Macaron シェーダーに戻ります。もう一度「適用」を押してください。", "주의: Macaron 변환을 다시 실행하면 머티리얼이 원래 Macaron 셰이더로 돌아가므로 \"적용\"을 한 번 더 누르세요." } },
        };
    }
}
#endif
