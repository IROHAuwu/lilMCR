using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LilMCR.MaskGenerator
{
    internal static class MaskText
    {
        // Optional bridge: no reference to the Macaron assembly, no circular dependency.
        static readonly Type LanguageType = Type.GetType("liltoon_MCR.ML, liltoon_MCR.Editor");
        static readonly PropertyInfo CurrentProperty = LanguageType == null ? null : LanguageType.GetProperty("Current");
        static readonly MethodInfo PopupMethod = LanguageType == null ? null : LanguageType.GetMethod("LanguagePopup");
        static int Current
        {
            get
            {
                if(CurrentProperty != null) return Convert.ToInt32(CurrentProperty.GetValue(null));
                int value = EditorPrefs.GetInt("Macaron.Language", 0);
                return value >= 1 && value <= 4 ? value : 2;
            }
        }
        public static string T(string key)
        {
            if(key == null) return null;
            int lang = Current;
            string[] row;
            if(lang == 1 || !Table.TryGetValue(key, out row)) return key;
            if(lang == 4 && row.Length > 2) return row[2];     // Korean
            return row[lang == 3 ? 1 : 0];
        }
        public static void LanguagePopup()
        {
            if(PopupMethod != null) { PopupMethod.Invoke(null, null); return; }
            int old = EditorPrefs.GetInt("Macaron.Language", 0);
            int next = EditorGUILayout.Popup("Language", Mathf.Clamp(old, 0, 4),
                new[] { "Auto (English)", "繁體中文", "English", "日本語", "한국어" });
            if(next != old) { EditorPrefs.SetInt("Macaron.Language", next); InternalEditorUtility.RepaintAllViews(); }
        }
        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "不產生陰影遮罩", new[] { "No-shadow casting mask", "影を生成しないマスク", "그림자 생성 제외 마스크" } },
            { "選中的 UV 面 → 黑色 → 不產生陰影", new[] { "Selected UV faces → black → no shadow casting", "選択した UV 面 → 黒 → 影を生成しない", "선택한 UV 면 → 검은색 → 그림자를 드리우지 않음" } },
            { "UV 遮罩生成器", new[] { "UV Mask Generator", "UV マスク生成", "UV 마스크 생성기" } },
            { "選中的 UV 面 → 黑色遮罩 → 不受效果影響", new[] { "Selected UV faces → black mask → effects excluded", "選択した UV 面 → 黒いマスク → 効果の対象外", "선택한 UV 면 → 검은 마스크 → 효과 대상에서 제외" } },
            { "模型 Renderer", new[] { "Model Renderer", "モデルの Renderer", "모델 Renderer" } },
            { "使用目前選取物件", new[] { "Use selected object", "選択中のオブジェクトを使用", "현재 선택한 오브젝트 사용" } },
            { "重新讀取 UV", new[] { "Reload UVs", "UV を再読み込み", "UV 다시 읽기" } },
            { "重設視野", new[] { "Reset view", "表示をリセット", "보기 초기화" } },
            { "無材質", new[] { "No material", "マテリアルなし", "머티리얼 없음" } },
            { "材質槽 / Submesh", new[] { "Material slot / Submesh", "マテリアルスロット / サブメッシュ", "머티리얼 슬롯 / 서브메시" } },
            { "三角面", new[] { "Triangles", "三角面", "삼각면" } },
            { "UV 島", new[] { "UV islands", "UV アイランド", "UV 아일랜드" } },
            { "網格", new[] { "Wireframe", "ワイヤー", "와이어프레임" } },
            { "全選", new[] { "Select all", "すべて選択", "모두 선택" } },
            { "清除", new[] { "Clear", "クリア", "지우기" } },
            { "反選", new[] { "Invert", "選択を反転", "선택 반전" } },
            { "復原", new[] { "Undo selection", "選択を元に戻す", "선택 되돌리기" } },
            { "左鍵拖曳以圓形筆刷選取；Shift＋左鍵取消；[ / ] 調整筆刷大小；中鍵移動；滾輪縮放。重疊 UV 共用遮罩。", new[] { "Left-drag to select with the round brush; Shift+left to deselect; [ / ] to resize the brush; middle-drag to pan; wheel to zoom. Overlapping UVs share the mask.", "左ドラッグで円形ブラシ選択、Shift＋左で解除、[ / ] でブラシサイズ変更、中ドラッグで移動、ホイールで拡大縮小。重複する UV はマスクを共有します。", "왼쪽 드래그로 원형 브러시 선택, Shift+왼쪽으로 해제, [ / ]로 브러시 크기 조절, 가운데 드래그로 이동, 휠로 확대/축소. 겹치는 UV는 마스크를 공유합니다." } },
            { "筆刷大小", new[] { "Brush size", "ブラシサイズ", "브러시 크기" } },
            { "已選 {0} / {1} 個三角面（橘色）", new[] { "Selected {0} / {1} triangles (orange)", "選択中：{0} / {1} 三角面（オレンジ）", "삼각면 {0} / {1}개 선택됨 (주황색)" } },
            { "輸出大小", new[] { "Output resolution", "出力解像度", "출력 크기" } },
            { "黑色區域擴張（像素）", new[] { "Black region padding (pixels)", "黒い領域の拡張（ピクセル）", "검은 영역 확장 (픽셀)" } },
            { "套用目標", new[] { "Apply to", "適用先", "적용 대상" } },
            { "全域遮罩（含 PCSS）", new[] { "Global mask (including PCSS)", "全体マスク（PCSS を含む）", "전체 마스크 (PCSS 포함)" } },
            { "AO 遮罩", new[] { "AO mask", "AO マスク", "AO 마스크" } },
            { "Contact Shadow 遮罩", new[] { "Contact shadow mask", "コンタクトシャドウマスク", "접촉 그림자 마스크" } },
            { "生成 PNG 並自動套用…", new[] { "Generate PNG and apply…", "PNG を生成して適用…", "PNG 생성 후 자동 적용…" } },
            { "生成成功後會自動套用至目前材質的指定遮罩，強度設為 1；AO 遮罩反轉設為關閉。共用此材質的物件都會受影響，材質修改可用 Unity Undo 復原。", new[] { "After generation, the mask is applied to the current material with strength 1 and AO mask inversion off. All objects sharing the material are affected. Material changes support Unity Undo.", "生成後、現在のマテリアルに強度 1 で適用し、AO マスクの反転をオフにします。同じマテリアルを共有するすべてのオブジェクトに影響します。マテリアルの変更は Unity Undo で戻せます。", "생성에 성공하면 현재 머티리얼의 지정한 마스크에 자동으로 적용하고 강도를 1로, AO 마스크 반전을 끔으로 설정합니다. 이 머티리얼을 공유하는 모든 오브젝트에 영향을 주며, 머티리얼 변경은 Unity Undo로 되돌릴 수 있습니다." } },
            { "目前材質不支援此遮罩目標，請選擇 lilMCR 或 lilMCR PCSS 材質。", new[] { "This material does not support the mask target. Select a lilMCR or lilMCR PCSS material.", "このマテリアルは選択したマスクに対応していません。lilMCR または lilMCR PCSS マテリアルを選択してください。", "현재 머티리얼은 이 마스크 대상을 지원하지 않습니다. lilMCR 또는 lilMCR PCSS 머티리얼을 선택하세요." } },
            { "找不到 Mesh；請選擇 SkinnedMeshRenderer 或 MeshRenderer。", new[] { "No mesh found. Select a SkinnedMeshRenderer or MeshRenderer.", "メッシュがありません。SkinnedMeshRenderer または MeshRenderer を選択してください。", "Mesh를 찾을 수 없습니다. SkinnedMeshRenderer 또는 MeshRenderer를 선택하세요." } },
            { "材質槽沒有對應的 Submesh。", new[] { "The material slot has no matching submesh.", "マテリアルスロットに対応するサブメッシュがありません。", "머티리얼 슬롯에 대응하는 서브메시가 없습니다." } },
            { "此槽沒有材質。", new[] { "This slot has no material.", "このスロットにマテリアルがありません。", "이 슬롯에는 머티리얼이 없습니다." } },
            { "只支援三角形拓撲。", new[] { "Only triangle topology is supported.", "三角形のトポロジーのみ対応しています。", "삼각형 토폴로지만 지원합니다." } },
            { "沒有有效的 UV0 或三角面。", new[] { "No valid UV0 or triangles.", "有効な UV0 または三角面がありません。", "유효한 UV0 또는 삼각면이 없습니다." } },
            { "目前支援 0–1 範圍 UV。此材質經 MainTex Tiling/Offset 後超出範圍；不會自動裁切或錯誤輸出。請使用一般 0–1 UV 材質。", new[] { "Only UVs within 0–1 are supported. MainTex tiling/offset puts this mesh outside that range. Use a material with UVs within 0–1.", "UV は 0～1 の範囲のみ対応しています。MainTex のタイリング／オフセット適用後に範囲外になります。0～1 の UV を使用してください。", "0–1 범위의 UV만 지원합니다. 이 머티리얼은 MainTex Tiling/Offset 적용 후 범위를 벗어나며, 자동으로 잘라내거나 잘못 출력하지 않습니다. 0–1 UV를 쓰는 일반 머티리얼을 사용하세요." } },
            { "使用 UV0 與 MainTex Tiling/Offset。UV 動畫、旋轉、Parallax 或其他動態 UV 變形不在本工具預覽範圍內。", new[] { "Uses UV0 and MainTex tiling/offset. UV animation, rotation, parallax and other dynamic UV deformation are not previewed.", "UV0 と MainTex のタイリング／オフセットを使用します。UV アニメーション、回転、視差、その他の動的 UV 変形はプレビュー対象外です。", "UV0과 MainTex Tiling/Offset을 사용합니다. UV 애니메이션, 회전, Parallax 등 동적인 UV 변형은 미리보기에 반영되지 않습니다." } },
            { "生成並套用遮罩", new[] { "Generate and apply mask", "マスクを生成して適用", "마스크 생성 및 적용" } },
            { "選中的區域會輸出為黑色，並自動套用至目前選擇的遮罩目標。", new[] { "Selected areas are exported in black and applied to the selected mask target.", "選択した領域を黒で出力し、選択したマスク先に自動適用します。", "선택한 영역은 검은색으로 출력되며, 현재 선택한 마스크 대상에 자동으로 적용됩니다." } },
            { "無法覆蓋", new[] { "Cannot overwrite", "上書きできません", "덮어쓸 수 없음" } },
            { "請勿覆蓋目前作為背景的主貼圖。", new[] { "Do not overwrite the main texture used as the background.", "背景として使用中のメインテクスチャは上書きできません。", "배경으로 쓰고 있는 메인 텍스처는 덮어쓰지 마세요." } },
            { "確定", new[] { "OK", "OK", "확인" } },
            { "輸出遮罩", new[] { "Export mask", "マスクを出力", "마스크 출력" } },
            { "填入 UV 三角面與擴張邊界…", new[] { "Rasterizing UV faces and padding…", "UV 面の描画と境界の拡張中…", "UV 삼각면 채우기 및 경계 확장 중…" } },
            { "遮罩匯入失敗，材質未套用。", new[] { "Mask import failed; the material was not updated.", "マスクのインポートに失敗しました。マテリアルには適用していません。", "마스크 임포트에 실패해 머티리얼에 적용하지 않았습니다." } },
            { "輸出失敗", new[] { "Export failed", "出力に失敗しました", "출력 실패" } },
            { "埋入遮罩", new[] { "Uzumore mask", "埋もれマスク", "파묻힘 마스크" } },
            { "PCSS 遮罩", new[] { "PCSS mask", "PCSS マスク", "PCSS 마스크" } },
            { "選中的 UV 面 → 黑色 → 沒有 PCSS 影子", new[] { "Selected UV faces → black → no PCSS shadow", "選択した UV 面 → 黒 → PCSS の影なし", "선택한 UV 면 → 검은색 → PCSS 그림자 없음" } },
            { "白色 = 有 PCSS 影子／黑色 = 沒有 PCSS 影子（只影響 PCSS，不影響 AO 與 Contact Shadow）。選中的面輸出為黑色，其餘為白色。", new[] { "White = PCSS shadow / black = no PCSS shadow (PCSS only; AO and Contact Shadow are not affected). Selected faces are written black, the rest white.", "白 = PCSS の影あり／黒 = PCSS の影なし（PCSS のみ。AO と Contact Shadow には影響しません）。選択した面が黒、それ以外が白で出力されます。", "흰색 = PCSS 그림자 있음 / 검은색 = PCSS 그림자 없음 (PCSS만 해당, AO와 Contact Shadow에는 영향 없음). 선택한 면은 검은색, 나머지는 흰색으로 출력됩니다." } },
            { "PCSS 遮罩只適用於 lilMCR PCSS 材質（Macaron/PCSS）。請先用 lilMCR PCSS Setup 轉換材質。", new[] { "The PCSS mask is only for lilMCR PCSS materials (Macaron/PCSS). Convert the materials with lilMCR PCSS Setup first.", "PCSS マスクは lilMCR PCSS マテリアル（Macaron/PCSS）専用です。先に lilMCR PCSS Setup でマテリアルを変換してください。", "PCSS 마스크는 lilMCR PCSS 머티리얼(Macaron/PCSS) 전용입니다. 먼저 lilMCR PCSS Setup으로 머티리얼을 변환하세요." } },
            { "選中的 UV 面 → 黑色 → 排除埋入（不推回）", new[] { "Selected UV faces → black → excluded from Uzumore (not pushed back)", "選択した UV 面 → 黒 → 埋もれの対象外（押し戻さない）", "선택한 UV 면 → 검은색 → 파묻힘 제외 (밀어내지 않음)" } },
            { "白色 = 有埋入效果（鏡頭穿入時推回）／黑色 = 排除（不推回，例如脖子、背後）。選中的面輸出為黑色，其餘為白色。", new[] { "White = Uzumore on (pushed back when a camera goes inside) / Black = excluded (not pushed back, e.g. neck, back). Selected faces are exported black, the rest white.", "白 = 埋もれ効果あり（カメラがめり込んだとき押し戻す）／黒 = 対象外（押し戻さない。例：首、背中）。選択した面は黒、それ以外は白で出力します。", "흰색 = 파묻힘 적용 (카메라가 들어오면 밀어냄) / 검은색 = 제외 (밀어내지 않음, 예: 목, 등). 선택한 면은 검은색, 나머지는 흰색으로 출력합니다." } },
            { "白色 = 正常產生陰影／黑色 = 不產生陰影。選中的面輸出為黑色，其餘為白色。", new[] { "White = casts shadows / Black = no shadow casting. Selected faces are exported black, the rest white.", "白 = 通常どおり影を生成／黒 = 影を生成しない。選択した面は黒、それ以外は白で出力します。", "흰색 = 그림자를 드리움 / 검은색 = 그림자를 드리우지 않음. 선택한 면은 검은색, 나머지는 흰색으로 출력합니다." } },
            { "白色 = 有效果／黑色 = 排除效果。選中的面輸出為黑色，其餘為白色。", new[] { "White = effect on / Black = effect excluded. Selected faces are exported black, the rest white.", "白 = 効果あり／黒 = 効果の対象外。選択した面は黒、それ以外は白で出力します。", "흰색 = 효과 적용 / 검은색 = 효과 제외. 선택한 면은 검은색, 나머지는 흰색으로 출력합니다." } },
            { "部位細節遮罩", new[] { "Part Detail mask", "部位ディテールマスク", "부위 디테일 마스크" } },
            { "選中的 UV 面 → 黑色 → 排除部位細節", new[] { "Selected UV faces → black → excluded from Part Detail", "選択した UV 面 → 黒 → 部位ディテールの対象外", "선택한 UV 면 → 검은색 → 부위 디테일 제외" } },
            { "白色 = 有部位細節效果／黑色 = 排除（不套用細紋與高光）。選中的面輸出為黑色，其餘為白色。", new[] { "White = Part Detail on / Black = excluded (no detail or highlight). Selected faces are exported black, the rest white.", "白 = 部位ディテール効果あり／黒 = 対象外（凹凸とハイライトを適用しない）。選択した面は黒、それ以外は白で出力します。", "흰색 = 부위 디테일 적용 / 검은색 = 제외 (요철과 하이라이트 미적용). 선택한 면은 검은색, 나머지는 흰색으로 출력됩니다." } },
        };
    }
}
