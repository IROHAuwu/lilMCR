#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace liltoon_MCR
{
    //==============================================================================================
    // Property table shared by the material Inspector and the Batch Settings window.
    // Default values are NOT written here: they come from lilCustomShaderProperties.lilblock,
    // so there is exactly one place that defines them.
    //==============================================================================================

    // Vec4: four values of a Vector property, one slider each (Batch Settings only; e.g. lilToon _MainTexHSVG).
    public enum MacaronPropKind { Toggle, Slider, Popup, Color, Texture, Direction, Range2, Vec4 }

    public sealed class MacaronProp
    {
        public string Name;
        public string Label;
        public string Tooltip;
        public MacaronPropKind Kind;
        public string[] Options;
        public float[] Values;
        public bool Advanced;
        public string SubHeader;            // small heading drawn above this row
        public string TextureExtra;         // Texture rows: property drawn on the same line in the Inspector
        public bool InlineExtra;            // this row is drawn as TextureExtra of a texture row (Inspector skips it)
        public bool ScaleOffset;            // Texture rows: show Tiling / Offset
        public string SpaceProp;            // Direction rows: the "方向基準" property (null = Contact Shadow's)
        public string[] PairLabels;         // Range2 rows: labels of .x / .y
        public float PairMin, PairMax;      // Range2 rows: slider range of both values
        public string[] VecLabels;          // Vec4 rows: labels of .x / .y / .z / .w
        public float[] VecMin, VecMax;      // Vec4 rows: slider range per value
        public bool Hdr;                    // Color rows: HDR color picker (lilToon [lilHDR] colors)
        public bool Lil;                    // Label / SubHeader / Options are lilToon's own text keys (LilLoc), not Macaron's
        public Func<Func<string, float>, bool> Visible;
        public Func<Func<string, float>, float, bool> OptionHidden;   // Popup rows: options (by value) not offered right now
        public Func<float, float> HiddenAs;                          // a stored hidden value shows as this offered value

        // Label / Tooltip are stored in Traditional Chinese and translated when drawn (MacaronLocalization.cs).
        // The translated GUIContents are kept until the UI language changes, so drawing a row does not allocate.
        private string cachedStamp;
        private GUIContent cachedContent;
        private GUIContent[] cachedOptions;

        private void RefreshCache()
        {
            string now = Lil ? LilLoc.Stamp : ((int)ML.Current).ToString();
            if(cachedContent != null && cachedStamp == now) return;
            cachedStamp = now;
            cachedContent = new GUIContent(Text(Label), ML.T(Tooltip));
            cachedOptions = null;
            choiceLabels = null;
            if(Options != null)
            {
                cachedOptions = new GUIContent[Options.Length];
                for(int i = 0; i < Options.Length; i++) cachedOptions[i] = new GUIContent(Text(Options[i]));
            }
        }

        /// <summary>A label of this row in the current language (lilToon's own text for lilToon rows).</summary>
        public string Text(string key) { return Lil ? LilLoc.T(key) : ML.T(key); }

        public GUIContent Content { get { RefreshCache(); return cachedContent; } }
        public bool IsVisible(Func<string, float> get) { return Visible == null || Visible(get); }

        // Offered options of a Popup row (OptionHidden removes some in the current state). Rebuilt only when the set
        // of offered options or the UI language changes.
        private int choiceMask = -1;
        private GUIContent[] choiceLabels;
        private float[] choiceValues;

        public GUIContent[] Choices(Func<string, float> get, out float[] values)
        {
            RefreshCache();
            if(OptionHidden == null || Values == null || cachedOptions == null)
            {
                values = Values;
                return cachedOptions;
            }
            int mask = 0;
            for(int i = 0; i < Values.Length && i < 31; i++) if(!OptionHidden(get, Values[i])) mask |= 1 << i;
            if(choiceLabels == null || mask != choiceMask)
            {
                var labels = new List<GUIContent>();
                var vals = new List<float>();
                for(int i = 0; i < Values.Length && i < cachedOptions.Length; i++)
                {
                    if(i < 31 && (mask & (1 << i)) == 0) continue;
                    labels.Add(cachedOptions[i]);
                    vals.Add(Values[i]);
                }
                choiceMask = mask;
                choiceLabels = labels.ToArray();
                choiceValues = vals.ToArray();
            }
            values = choiceValues;
            return choiceLabels;
        }

        /// <summary>The value a Popup row shows for a stored value (a hidden option shows as its HiddenAs value).</summary>
        public float ShownValue(float stored, Func<string, float> get)
        {
            return OptionHidden != null && HiddenAs != null && OptionHidden(get, stored) ? HiddenAs(stored) : stored;
        }
    }

    public sealed class MacaronSection
    {
        public string Id;
        public string Title;
        public bool Lil;                    // Title is a lilToon text key (LilLoc)
        public string DisplayTitle { get { return Lil ? LilLoc.T(Title) : ML.T(Title); } }
        public string EnableProp;
        public string Help;
        public List<MacaronProp> Props = new List<MacaronProp>();
    }

    public static class MacaronCore
    {
        public const string ShaderRoot = "Macaron";
        public const string ShaderMain = ShaderRoot + "/lilToon";
        public const string PropPrefix = "_MCR_";
        public const string ConvertedFolder = "Assets/liltoon_MCR_ConvertedMaterials";
        public const string ConvertedSuffix = "_MCR";
        public const string DepthSourceName = "MCR_DepthSource";   // object with the light that makes Unity render the depth texture
        public const string MenuRoot = "Macaron/";

        public const string PropShadowSpace = "_MCR_ShadowSpace";
        public const string PropShadowDirection = "_MCR_ShadowDirection";
        public static readonly string[] BasisProps = { "_MCR_AvatarBasisX", "_MCR_AvatarBasisY", "_MCR_AvatarBasisZ" };

        // Stored in Traditional Chinese; draw them through ML.T / ML.TA. Must stay above Sections (static init order).
        public static readonly string[] SpaceLabels = { "跟隨角色", "跟隨鏡頭", "世界座標" };
        public static readonly string[] SpaceHelp =
        {
            "從角色正面看的方向。角色轉身、走動時，影子方向跟著角色一起轉，不受世界光源影響。",
            "以畫面為基準：不論從哪個角度看，影子都落在畫面上的同一個方向。",
            "以世界座標 X / Y / Z 為基準（前後 = Z 軸）。"
        };

        public static readonly List<MacaronSection> Sections = BuildSections();

        /// <summary>Standard lilToon settings shown in Batch Settings ("lilToon 標準設定"). These are lilToon's own
        /// properties: the material Inspector never draws them (lilToon does), Macaron's reset / copy / tiers never touch them.</summary>
        public static readonly List<MacaronSection> LilToonSections = BuildLilToonSections();

        //------------------------------------------------------------------------------------------
        // Table

        private static MacaronProp P(string name, string label, MacaronPropKind kind, string tooltip = null)
        {
            return new MacaronProp { Name = name, Label = label, Kind = kind, Tooltip = tooltip };
        }

        private static MacaronProp Popup(string name, string label, string[] options, float[] values, string tooltip = null)
        {
            return new MacaronProp { Name = name, Label = label, Kind = MacaronPropKind.Popup, Options = options, Values = values, Tooltip = tooltip };
        }

        // Two values stored in .x / .y of a Vector property, each drawn as its own slider.
        private static MacaronProp Pair(string name, string label, string labelX, string labelY, float min, float max, string tooltip = null)
        {
            return new MacaronProp
            {
                Name = name, Label = label, Kind = MacaronPropKind.Range2, Tooltip = tooltip,
                PairLabels = new[] { labelX, labelY }, PairMin = min, PairMax = max
            };
        }

        private static MacaronProp Adv(MacaronProp p) { p.Advanced = true; return p; }
        private static MacaronProp When(MacaronProp p, Func<Func<string, float>, bool> visible) { p.Visible = visible; return p; }
        private static MacaronProp HideOptions(MacaronProp p, Func<Func<string, float>, float, bool> hidden, Func<float, float> shownAs)
        {
            p.OptionHidden = hidden;
            p.HiddenAs = shownAs;
            return p;
        }
        private static MacaronProp Head(MacaronProp p, string subHeader) { p.SubHeader = subHeader; return p; }

        // 0 SSAO / 1 Ray March / 2 GTAO / 3 Soft SSAO
        private static int OccMethodOf(Func<string, float> g) { return Mathf.RoundToInt(g("_MCR_OccMethod")); }
        private static bool IsSSAO(Func<string, float> g) { return OccMethodOf(g) == 0; }
        private static bool IsRay(Func<string, float> g) { return OccMethodOf(g) == 1; }
        private static bool IsGTAO(Func<string, float> g) { return OccMethodOf(g) == 2; }
        private static bool IsSoft(Func<string, float> g) { return OccMethodOf(g) == 3; }
        private static bool IsSSAOOrSoft(Func<string, float> g) { return IsSSAO(g) || IsSoft(g); }
        // Settings shared by Ray March and GTAO (Focus, Thickness, scale mode, far tint).
        private static bool IsMetric(Func<string, float> g) { return IsRay(g) || IsGTAO(g); }

        private static List<MacaronSection> BuildSections()
        {
            List<MacaronSection> list = new List<MacaronSection>();

            //--------------------------------------------------------------------------------------
            MacaronSection basic = new MacaronSection { Id = "basic", Title = "基本設定", EnableProp = "_MCR_Active" };
            basic.Props.Add(P("_MCR_Amount", "整體強度", MacaronPropKind.Slider, "遮蔽與接觸陰影的總強度。"));
            // Local Only (mcr_local_only.hlsl, MacaronLocalOnly.cs). A user choice: not part of the quality tiers.
            basic.Props.Add(P("_MCR_LocalOnly", "僅自己可見", MacaronPropKind.Toggle, "開啟後，lilMCR 的所有效果（遮蔽、接觸陰影、埋入、光照調節、世界陰影、PCSS）只在你自己的畫面顯示；其他玩家看到的是沒有 lilMCR 效果的原版 lilToon。需要光照調節器（判斷是不是自己）；沒有光照調節器時連自己也看不到效果。你自己的鏡子與相機會拍到效果。也可以在光照調節器勾選「僅自己可見」加入選單，在遊戲中切換（預設不加入）。"));
            basic.Props.Add(P("_MCR_FloorBrightness", "最低亮度", MacaronPropKind.Slider, "不會比原本顏色的這個比例更暗（0.25 = 最暗到原本的 25%）。避免角色變全黑。"));
            basic.Props.Add(P("_MCR_NormalSmooth", "法線平滑", MacaronPropKind.Slider, "以平滑法線判斷遮蔽，消除沿著多邊形出現的一格一格陰影。通常保持 1。"));
            basic.Props.Add(Adv(Pair("_MCR_FadeDistance", "距離淡出 (m)", "開始", "結束", 0f, 50f, "從「開始」到「結束」之間效果逐漸消失。結束 ≤ 開始 = 不淡出。")));
            basic.Props.Add(Adv(P("_MCR_AlphaAware", "半透明時避開被遮住的部分", MacaronPropKind.Toggle, "半透明材質：被不透明物體擋住的地方不加遮蔽與接觸陰影。")));
            MacaronProp globalMask = Adv(P("_MCR_GlobalMask", "整體遮罩", MacaronPropKind.Texture, "白 = 有效果、黑 = 無效果（R 通道）。"));
            globalMask.TextureExtra = "_MCR_GlobalMaskAmount";
            globalMask.ScaleOffset = true;
            basic.Props.Add(globalMask);
            MacaronProp globalMaskAmount = Adv(P("_MCR_GlobalMaskAmount", "整體遮罩強度", MacaronPropKind.Slider));
            globalMaskAmount.InlineExtra = true;
            basic.Props.Add(globalMaskAmount);
            var casterMask = Adv(P("_MCR_CasterMask", "不產生陰影遮罩", MacaronPropKind.Texture,
                "黑色不產生陰影；白色正常投影。保留角色本身外觀，與接收遮罩分開。世界陰影須建立 / 更新一次。"));
            casterMask.TextureExtra = "_MCR_CasterMaskAmount"; casterMask.ScaleOffset = true;
            basic.Props.Add(casterMask);
            var casterAmount = Adv(P("_MCR_CasterMaskAmount", "不產生陰影遮罩強度", MacaronPropKind.Slider));
            casterAmount.InlineExtra = true; basic.Props.Add(casterAmount);
            list.Add(basic);

            //--------------------------------------------------------------------------------------
            MacaronSection occ = new MacaronSection { Id = "occ", Title = "遮蔽（環境光遮蔽）", EnableProp = "_MCR_OccEnable" };
            occ.Props.Add(Popup("_MCR_OccMethod", "方式", new[] { "SSAO（輕量）", "柔和 SSAO（輕量）", "光線步進 Ray March（高負擔）", "GTAO（中負擔）" }, new[] { 0f, 3f, 1f, 2f },
                "Ray March 效能負擔比 SSAO 高。GTAO 依遮蔽物的角度計算被遮住的比例，陰影柔和且遠近一致。"));
            // "Standard (no 2x2)" only differs from Standard in Ray March (2x2 quad share); SSAO / GTAO do not offer it.
            occ.Props.Add(HideOptions(Popup("_MCR_OccQuality", "品質", new[] { "輕量", "標準", "標準（無 2×2）", "精細", "最高" }, new[] { 0f, 1f, 4f, 2f, 3f },
                "Ray March（DX11）：標準 = 相鄰 2×2 像素各打約一半光線再合併，輕量 = 各約 1/4；輪廓、深度落差、折角處自動完整採樣。標準（無 2×2）= 標準的採樣數但每個像素自己完整計算，不會因合併而模糊。精細、最高不分攤。"),
                (g, v) => Mathf.Approximately(v, OccQualityNo2x2) && !IsRay(g), v => OccQualityStandard));
            occ.Props.Add(Adv(P("_MCR_OccScreenLOD", "依畫面大小調整品質", MacaronPropKind.Toggle, "減少看不出差別的採樣：Ray March 範圍在畫面上很小時減少採樣、近看時限制範圍在畫面上的大小。關閉可保留原本的品質與範圍。")));
            occ.Props.Add(P("_MCR_OccAmount", "濃度", MacaronPropKind.Slider));
            occ.Props.Add(P("_MCR_OccContrast", "對比", MacaronPropKind.Slider, "-1 = 淡的遮蔽也明顯、0 = 不變、1 = 淡的更淡、濃的更濃。"));

            occ.Props.Add(When(P("_MCR_SSAOReach", "範圍 (px)", MacaronPropKind.Slider), IsSSAO));
            // Same control as GTAO's Front Distance (same text, so the translations are shared).
            occ.Props.Add(When(P("_MCR_SSAODepthReach", "前後距離 (m)", MacaronPropKind.Slider,
                "遮蔽物最遠可以離這一點多遠（例如稍微離開臉的頭髮、離開胸口的手）。只影響前後，影子往左右擴張的寬度由「範圍」決定。"), IsSSAO));
            occ.Props.Add(When(Adv(P("_MCR_SSAOBias", "自我遮蔽容許 (m)", MacaronPropKind.Slider,
                "高出平滑表面不到這個距離的部分視為表面本身。出現一格一格的陰影時稍微加大。")), IsSSAO));
            occ.Props.Add(When(P("_MCR_SSAOSpread", "陰影傾向", MacaronPropKind.Slider,
                "0 = 強調輪廓與縫隙、1 = 像落影一樣在突出物下方大片變暗。"), IsSSAO));
            occ.Props.Add(When(Adv(P("_MCR_SSAOEdgeReject", "輪廓抑制", MacaronPropKind.Slider,
                "減弱幾乎側對鏡頭的面（角色外輪廓）上的遮蔽，消除黑色描邊。")), IsSSAOOrSoft));

            // Soft SSAO: same labels / help as the other methods where the control means the same.
            occ.Props.Add(When(P("_MCR_SoftRadius", "範圍 (m)", MacaronPropKind.Slider, "多遠的遮蔽物會影響這一點。"), IsSoft));
            occ.Props.Add(When(P("_MCR_SoftDepthReach", "前後距離 (m)", MacaronPropKind.Slider,
                "遮蔽物最遠可以離這一點多遠（例如稍微離開臉的頭髮、離開胸口的手）。只影響前後，影子往左右擴張的寬度由「範圍」決定。"), IsSoft));
            occ.Props.Add(When(P("_MCR_SoftFocus", "集中度", MacaronPropKind.Slider,
                "0 = 範圍內平均取樣（漸層較寬）、1 = 集中在這一點附近（縫隙較深、外圍較淡）。"), IsSoft));
            occ.Props.Add(When(Adv(P("_MCR_SoftLift", "離面程度", MacaronPropKind.Slider,
                "取樣點離開表面的程度。0 = 貼著表面（彎曲的面會自己變暗）、1 = 只沿著法線方向。")), IsSoft));
            occ.Props.Add(When(Adv(P("_MCR_SoftBias", "自我遮蔽容許 (m)", MacaronPropKind.Slider,
                "高出平滑表面不到這個距離的部分視為表面本身。出現一格一格的陰影時稍微加大。")), IsSoft));
            occ.Props.Add(When(Adv(P("_MCR_SoftClean", "去除自我遮蔽", MacaronPropKind.Slider,
                "0 = 不處理：背向鏡頭轉過去的面會整片淡淡變暗（較柔和）。1 = 只有真正被擋住的地方變暗（較乾淨）。")), IsSoft));

            occ.Props.Add(When(P("_MCR_OccReach", "範圍 (m)", MacaronPropKind.Slider, "多遠的遮蔽物會影響這一點。"), IsRay));
            // GTAO has its own Reach with a smaller range of values (same label and help, so the translations are shared).
            occ.Props.Add(When(P("_MCR_GTAOReach", "範圍 (m)", MacaronPropKind.Slider, "多遠的遮蔽物會影響這一點。"), IsGTAO));
            occ.Props.Add(When(P("_MCR_GTAODepthReach", "前後距離 (m)", MacaronPropKind.Slider,
                "遮蔽物最遠可以離這一點多遠（例如稍微離開臉的頭髮、離開胸口的手）。只影響前後，影子往左右擴張的寬度由「範圍」決定。"), IsGTAO));
            occ.Props.Add(When(P("_MCR_GTAOSoftness", "邊緣漸層", MacaronPropKind.Slider,
                "遮蔽往範圍邊緣淡出的柔和程度。0 = 在範圍邊緣明顯切斷、1 = 從中心開始逐漸變淡。"), IsGTAO));
            occ.Props.Add(When(P("_MCR_OccFocus", "集中度", MacaronPropKind.Slider, "0 = 遠處的遮蔽物也有影響、1 = 只有很近的縫隙變暗。"), IsMetric));
            occ.Props.Add(When(Adv(P("_MCR_OccThickness", "遮蔽物厚度 (m)", MacaronPropKind.Slider)), IsMetric));
            occ.Props.Add(When(Adv(P("_MCR_OccSoftness", "柔和度", MacaronPropKind.Slider, "越大雜訊越少、邊緣越柔和；0 = 最銳利。")), IsRay));
            occ.Props.Add(When(Adv(P("_MCR_OccAngle", "角度範圍 (度)", MacaronPropKind.Slider, "只看法線周圍這個角度內的遮蔽。")), IsRay));
            occ.Props.Add(When(Adv(Popup("_MCR_OccScaleMode", "範圍單位", new[] { "公尺（固定大小）", "畫面（跟隨縮放）" }, new[] { 0f, 1f },
                "畫面：鏡頭拉近（FOV 變小）時範圍跟著放大，在畫面上看起來一樣大。")), IsMetric));
            occ.Props.Add(When(Adv(P("_MCR_GTAOWideAmount", "大範圍遮蔽強度", MacaronPropKind.Slider,
                "在「範圍」之外再加一圈較大的柔和遮蔽（例如手臂、頭髮在身體上的大片陰影）。0 = 關閉。")), IsGTAO));
            occ.Props.Add(When(Adv(P("_MCR_GTAOWideReach", "大範圍遮蔽範圍 (m)", MacaronPropKind.Slider)),
                g => IsGTAO(g) && g("_MCR_GTAOWideAmount") > 0f));

            occ.Props.Add(Head(P("_MCR_OccTintNear", "近處顏色", MacaronPropKind.Color), "顏色"));
            occ.Props.Add(When(P("_MCR_OccTintFar", "遠處顏色", MacaronPropKind.Color, "遮蔽物離得越遠，顏色越接近這個。"), IsMetric));
            occ.Props.Add(P("_MCR_OccAlbedoTint", "帶入底色", MacaronPropKind.Slider, "遮蔽的顏色帶上材質本身的顏色（像縫隙裡的反光），避免變灰。"));
            occ.Props.Add(P("_MCR_OccReflection", "遮蔽反射", MacaronPropKind.Slider, "遮蔽處額外減弱反射、MatCap、邊緣光、亮片。"));
            occ.Props.Add(Adv(P("_MCR_OccNoise", "隨機抖動", MacaronPropKind.Slider)));

            occ.Props.Add(Adv(Popup("_MCR_OccBlend", "合成方式", new[] { "減算", "乘算" }, new[] { 1f, 0f })));
            MacaronProp occMask = Adv(P("_MCR_OccMask", "遮蔽遮罩", MacaronPropKind.Texture, "白 = 有遮蔽、黑 = 無遮蔽（R 通道）。"));
            occMask.TextureExtra = "_MCR_OccMaskAmount";
            occ.Props.Add(occMask);
            MacaronProp occMaskAmount = Adv(P("_MCR_OccMaskAmount", "遮蔽遮罩強度", MacaronPropKind.Slider));
            occMaskAmount.InlineExtra = true;
            occ.Props.Add(occMaskAmount);
            occ.Props.Add(Adv(P("_MCR_OccMaskFlip", "反轉遮蔽遮罩", MacaronPropKind.Toggle)));
            list.Add(occ);

            //--------------------------------------------------------------------------------------
            MacaronSection shadow = new MacaronSection
            {
                Id = "shadow", Title = "接觸陰影（固定方向）", EnableProp = "_MCR_ShadowEnable",
                Help = "使用自己設定的光源方向，不受世界光源影響。其他玩家或物體擋在前面時也不會變黑。"
            };
            shadow.Props.Add(P("_MCR_ShadowIntensity", "濃度", MacaronPropKind.Slider));
            shadow.Props.Add(P("_MCR_ShadowColor", "顏色", MacaronPropKind.Color));
            shadow.Props.Add(Popup(PropShadowSpace, "方向基準", SpaceLabels, new[] { 0f, 1f, 2f }));
            shadow.Props.Add(P(PropShadowDirection, "光源方向", MacaronPropKind.Direction, "點擊或拖曳圓盤設定光從哪裡照過來。影子會落在相反方向。"));
            shadow.Props.Add(P("_MCR_ShadowLength", "影子長度 (m)", MacaronPropKind.Slider));
            shadow.Props.Add(P("_MCR_ShadowSoftness", "柔和度 (度)", MacaronPropKind.Slider, "0 = 銳利，數值越大邊緣越柔和。"));
            shadow.Props.Add(Popup("_MCR_ShadowQuality", "品質", new[] { "最低", "低", "中", "高", "極高" }, new[] { -1f, 0f, 1f, 2f, 3f },
                "最低：1 條光線、5 步，影子長時會出現分層，適合短影子或遠景。"));
            shadow.Props.Add(Adv(P("_MCR_ShadowScreenLOD", "依畫面大小調整品質", MacaronPropKind.Toggle, "近看時限制影子在畫面上的長度以降低負擔。關閉可保留原本的長度。")));
            shadow.Props.Add(Adv(P("_MCR_ShadowReject", "忽略前方物體距離 (m)", MacaronPropKind.Slider,
                "比這個距離更靠近鏡頭的物體（例如其他玩家）不會投下陰影。")));
            shadow.Props.Add(Adv(P("_MCR_ShadowThickness", "遮蔽物厚度 (m)", MacaronPropKind.Slider)));
            shadow.Props.Add(Adv(Pair("_MCR_ShadowOffset", "起點偏移 (m)", "深度", "法線", 0f, 0.02f, "出現一格一格的自我陰影時稍微加大。")));
            shadow.Props.Add(Adv(P("_MCR_ShadowNormalReject", "背光面排除", MacaronPropKind.Slider)));
            shadow.Props.Add(Adv(P("_MCR_ShadowBackShade", "背光面陰影", MacaronPropKind.Slider, "背對光源的面也加上陰影（像簡單的明暗）。")));
            shadow.Props.Add(Adv(P("_MCR_ShadowJitter", "隨機抖動", MacaronPropKind.Slider)));
            MacaronProp shadowMask = Adv(P("_MCR_ShadowMask", "陰影遮罩", MacaronPropKind.Texture, "白 = 有陰影、黑 = 無陰影（R 通道）。"));
            shadowMask.TextureExtra = "_MCR_ShadowMaskAmount";
            shadow.Props.Add(shadowMask);
            MacaronProp shadowMaskAmount = Adv(P("_MCR_ShadowMaskAmount", "陰影遮罩強度", MacaronPropKind.Slider));
            shadowMaskAmount.InlineExtra = true;
            shadow.Props.Add(shadowMaskAmount);
            list.Add(shadow);

            //--------------------------------------------------------------------------------------
            // Uzumore (custom_insert.hlsl, Macaron_UzumoreOS). On / Brightness are part of every quality tier; Reach and
            // Layer Offset are per avatar (MacaronUzumore.Setup: Converter, or Batch Settings when it is turned on).
            MacaronSection uzu = new MacaronSection
            {
                Id = "uzu", Title = "埋入（鏡頭穿入）", EnableProp = "_MCR_UzuEnable",
                Help = "鏡頭（自己或其他玩家）鑽進網格時，把被穿過的表面推回鏡頭前方，就看不到網格內側。預設開啟；轉換器會依角色自動設定推回範圍與圖層間距。"
            };
            uzu.Props.Add(P("_MCR_UzuAmount", "推回範圍 (m)", MacaronPropKind.Slider,
                "鏡頭穿入多深以內的表面會被推回。一般身高的角色約 0.1～0.2。太大時，貼在你背後的玩家視野可能被擋住。"));
            uzu.Props.Add(P("_MCR_UzuBrightness", "埋住時的亮度", MacaronPropKind.Slider, "被推到眼前的表面的亮度（1 = 不變暗）。"));
            uzu.Props.Add(P("_MCR_UzuSelf", "自己的視角也套用", MacaronPropKind.Toggle,
                "預設關閉：只有別人的視角會推回。需要光照調節器（轉換時預設加入）；沒有光照調節器時自己的視角一律推回。"));
            uzu.Props.Add(Adv(P("_MCR_UzuBackReject", "背面不推回", MacaronPropKind.Slider,
                "角色背面的表面不推回，避免擋住貼在你背後的玩家視野。依角色方向判斷。")));
            uzu.Props.Add(Adv(P("_MCR_UzuBias", "圖層間距 (m)", MacaronPropKind.Slider,
                "防止重疊的網格閃爍。越內層設越大，例如頭髮 0.001、衣服 0.0015、身體 0.002（轉換器會自動設定）。")));
            MacaronProp uzuMask = Adv(P("_MCR_UzuMask", "埋入遮罩", MacaronPropKind.Texture, "白 = 有埋入效果（推回）、黑 = 排除（不推回）（R 通道）。可用遮罩生成器製作。"));
            uzuMask.TextureExtra = "_MCR_UzuMaskAmount";
            uzu.Props.Add(uzuMask);
            MacaronProp uzuMaskAmount = Adv(P("_MCR_UzuMaskAmount", "埋入遮罩強度", MacaronPropKind.Slider));
            uzuMaskAmount.InlineExtra = true;
            uzu.Props.Add(uzuMaskAmount);
            list.Add(uzu);

            //--------------------------------------------------------------------------------------
            // Skin (custom_insert.hlsl, Macaron_Skin). Off by default and not part of any quality tier: it is meant for
            // the skin materials only. Turning it on fills an empty detail texture slot (EnsureSkinTexture).
            MacaronSection skin = new MacaronSection
            {
                Id = "skin", Title = "膚質", EnableProp = "_MCR_SkinEnable",
                Help = "在皮膚上加上細紋、柔和高光與明暗交界的透光感。全部由 lilMCR 自己繪製，不會更動材質原本的 lilToon 設定。只建議用在身體的皮膚材質。"
            };
            skin.Props.Add(P("_MCR_SkinDetail", "細紋強度", MacaronPropKind.Slider, "皮膚紋理與毛孔的凹凸感。0 = 沒有細紋。"));
            skin.Props.Add(P("_MCR_SkinLilNormal", "套用到 lilToon 的反光", MacaronPropKind.Slider, "讓 lilToon 本身的反射、MatCap、邊緣光也跟著細紋起伏。0 = 只有膚質自己的高光有細紋。"));
            skin.Props.Add(P("_MCR_SkinEven", "均勻密度", MacaronPropKind.Toggle, "不管 UV 怎麼拉伸（包含形態鍵造成的拉伸），細紋在表面上都保持同樣的大小。會多讀 3 次貼圖。關閉時細紋直接跟著 UV，拉伸的地方紋路也會被拉長。"));
            skin.Props.Add(When(P("_MCR_SkinSize", "細紋大小 (m)", MacaronPropKind.Slider, "一張細紋貼圖在表面上蓋住的寬度。數值越小紋路越細。"), g => g("_MCR_SkinEven") > 0.5f));
            skin.Props.Add(When(P("_MCR_SkinTiling", "細紋密度", MacaronPropKind.Slider, "細紋貼圖在 UV 上重複的次數。數值越大紋路越細。"), g => g("_MCR_SkinEven") <= 0.5f));
            skin.Props.Add(P("_MCR_SkinSpecular", "高光強度", MacaronPropKind.Slider));
            skin.Props.Add(P("_MCR_SkinSmoothness", "光滑度", MacaronPropKind.Slider, "數值越大高光越小越亮（像出汗或上油），越小越霧面。"));
            skin.Props.Add(P("_MCR_SkinSheen", "邊緣柔光", MacaronPropKind.Slider, "斜看皮膚時邊緣的一層淡淡反光。"));
            skin.Props.Add(P("_MCR_SkinScatter", "透光感", MacaronPropKind.Slider, "明暗交界處的暖色，以及逆光時邊緣的透光。"));
            skin.Props.Add(Adv(P("_MCR_SkinScatterColor", "透光顏色", MacaronPropKind.Color)));
            skin.Props.Add(P("_MCR_SkinMottle", "膚色斑駁", MacaronPropKind.Slider, "讓膚色有自然的深淺不均、小斑點與淡淡的微血管。"));
            skin.Props.Add(Adv(P("_MCR_SkinMottleColor", "斑駁顏色", MacaronPropKind.Color)));
            skin.Props.Add(Adv(P("_MCR_SkinTex", "細紋貼圖", MacaronPropKind.Texture, "RG = 法線、B = 凹陷、A = 顏色變化（低於 0.5 = 斑駁、高於 0.5 = 微血管）。留空時開啟會自動填入內附的貼圖。")));
            MacaronProp skinMask = Adv(P("_MCR_SkinMask", "膚質遮罩", MacaronPropKind.Texture, "白 = 有膚質效果、黑 = 排除（R 通道）。可用遮罩生成器製作。"));
            skinMask.TextureExtra = "_MCR_SkinMaskAmount";
            skin.Props.Add(skinMask);
            MacaronProp skinMaskAmount = Adv(P("_MCR_SkinMaskAmount", "膚質遮罩強度", MacaronPropKind.Slider));
            skinMaskAmount.InlineExtra = true;
            skin.Props.Add(skinMaskAmount);
            list.Add(skin);

            return list;
        }

        private static MacaronProp Hdr(MacaronProp p) { p.Hdr = true; return p; }

        // lilToon property names / ranges follow lilToon's lts.shader, labels are lilToon's own text keys (LilLoc), in the
        // order of lilToon's Inspector. A material whose shader lacks a property (Lite, Fur, Gem ...) is skipped for it.
        private static MacaronProp L(string name, string key, MacaronPropKind kind, string tooltip = null)
        {
            return new MacaronProp { Name = name, Label = key, Kind = kind, Tooltip = tooltip, Lil = true };
        }

        private static MacaronProp LPopup(string name, string key, string[] optionKeys, float[] values)
        {
            return new MacaronProp { Name = name, Label = key, Kind = MacaronPropKind.Popup, Options = optionKeys, Values = values, Lil = true };
        }

        private static MacaronProp LVec(string name, string key, string[] labelKeys, float[] min = null, float[] max = null)
        {
            return new MacaronProp { Name = name, Label = key, Kind = MacaronPropKind.Vec4, VecLabels = labelKeys, VecMin = min, VecMax = max, Lil = true };
        }

        private static MacaronSection LSection(string id, string titleKey, string enableProp = null, string help = null)
        {
            return new MacaronSection { Id = id, Title = titleKey, Lil = true, EnableProp = enableProp, Help = help };
        }

        private static bool ShadowMask(Func<string, float> g, int type) { return Mathf.RoundToInt(g("_ShadowMaskType")) == type; }

        private static List<MacaronSection> BuildLilToonSections()
        {
            List<MacaronSection> list = new List<MacaronSection>();

            MacaronSection light = LSection("lil_light", "sLightingSettings");
            light.Props.Add(L("_LightMinLimit", "sLightMinLimit", MacaronPropKind.Slider));
            light.Props.Add(L("_LightMaxLimit", "sLightMaxLimit", MacaronPropKind.Slider));
            light.Props.Add(L("_MonochromeLighting", "sMonochromeLighting", MacaronPropKind.Slider));
            light.Props.Add(L("_AsUnlit", "sAsUnlit", MacaronPropKind.Slider));
            light.Props.Add(L("_VertexLightStrength", "sVertexLightStrength", MacaronPropKind.Slider));
            light.Props.Add(Adv(L("_lilDirectionalLightStrength", "sDirectionalLightStrength", MacaronPropKind.Slider)));
            light.Props.Add(Adv(L("_BeforeExposureLimit", "sBeforeExposureLimit", MacaronPropKind.Slider)));
            list.Add(light);

            // Shadow: every option of lilToon's shadow block.
            MacaronSection shadow = LSection("lil_shadow", "sShadowSetting", "_UseShadow");
            shadow.Props.Add(LPopup("_ShadowMaskType", "sMaskType", new[] { "sStrength", "sFlat", "SDF" }, new[] { 0f, 1f, 2f }));
            shadow.Props.Add(L("_ShadowStrengthMask", "sStrengthMask", MacaronPropKind.Texture));
            shadow.Props.Add(L("_ShadowStrengthMaskLOD", "LOD", MacaronPropKind.Slider));
            shadow.Props.Add(When(L("_ShadowFlatBorder", "sBorder", MacaronPropKind.Slider), g => ShadowMask(g, 1)));
            shadow.Props.Add(When(L("_ShadowFlatBlur", "sBlur", MacaronPropKind.Slider), g => !ShadowMask(g, 0)));
            shadow.Props.Add(L("_ShadowStrength", "sStrength", MacaronPropKind.Slider));
            shadow.Props.Add(Head(LPopup("_ShadowColorType", "sColorType", new[] { "sColorTypeNormal", "sColorTypeLUT" }, new[] { 0f, 1f }), "sShadow1stColor"));
            shadow.Props.Add(L("_ShadowColor", "sShadow1stColor", MacaronPropKind.Color));
            shadow.Props.Add(L("_ShadowColorTex", "sShadow1stColor+ (+sTexture+)", MacaronPropKind.Texture));
            shadow.Props.Add(L("_ShadowBorder", "sBorder", MacaronPropKind.Slider));
            shadow.Props.Add(L("_ShadowBlur", "sBlur", MacaronPropKind.Slider));
            shadow.Props.Add(L("_ShadowNormalStrength", "sNormalStrength", MacaronPropKind.Slider));
            shadow.Props.Add(L("_ShadowReceive", "sReceiveShadow", MacaronPropKind.Slider));

            shadow.Props.Add(Head(L("_Shadow2ndColor", "sShadow2ndColor", MacaronPropKind.Color), "sShadow2ndColor"));
            shadow.Props.Add(L("_Shadow2ndColorTex", "sShadow2ndColor+ (+sTexture+)", MacaronPropKind.Texture));
            shadow.Props.Add(L("_Shadow2ndBorder", "sBorder", MacaronPropKind.Slider));
            shadow.Props.Add(L("_Shadow2ndBlur", "sBlur", MacaronPropKind.Slider));
            shadow.Props.Add(L("_Shadow2ndNormalStrength", "sNormalStrength", MacaronPropKind.Slider));
            shadow.Props.Add(L("_Shadow2ndReceive", "sReceiveShadow", MacaronPropKind.Slider));

            shadow.Props.Add(Head(L("_Shadow3rdColor", "sShadow3rdColor", MacaronPropKind.Color), "sShadow3rdColor"));
            shadow.Props.Add(L("_Shadow3rdColorTex", "sShadow3rdColor+ (+sTexture+)", MacaronPropKind.Texture));
            shadow.Props.Add(L("_Shadow3rdBorder", "sBorder", MacaronPropKind.Slider));
            shadow.Props.Add(L("_Shadow3rdBlur", "sBlur", MacaronPropKind.Slider));
            shadow.Props.Add(L("_Shadow3rdNormalStrength", "sNormalStrength", MacaronPropKind.Slider));
            shadow.Props.Add(L("_Shadow3rdReceive", "sReceiveShadow", MacaronPropKind.Slider));

            shadow.Props.Add(Head(L("_ShadowBorderColor", "sShadowBorderColor", MacaronPropKind.Color), "sShadowBorderColor"));
            shadow.Props.Add(L("_ShadowBorderRange", "sShadowBorderRange", MacaronPropKind.Slider));
            shadow.Props.Add(Head(L("_ShadowMainStrength", "sContrast", MacaronPropKind.Slider), "sShadow"));
            shadow.Props.Add(L("_ShadowEnvStrength", "sShadowEnvStrength", MacaronPropKind.Slider));
            shadow.Props.Add(L("_lilShadowCasterBias", "Shadow Caster Bias", MacaronPropKind.Slider));
            shadow.Props.Add(Head(L("_ShadowBlurMask", "sBlurMask", MacaronPropKind.Texture), "sBlurMask"));
            shadow.Props.Add(L("_ShadowBlurMaskLOD", "LOD", MacaronPropKind.Slider));
            shadow.Props.Add(Head(L("_ShadowBorderMask", "AO Map", MacaronPropKind.Texture), "AO Map"));
            shadow.Props.Add(L("_ShadowBorderMaskLOD", "LOD", MacaronPropKind.Slider));
            shadow.Props.Add(L("_ShadowPostAO", "sIgnoreBorderProperties", MacaronPropKind.Toggle));
            shadow.Props.Add(LVec("_ShadowAOShift", "AO Map", new[] { "1st Scale", "1st Offset", "2nd Scale", "2nd Offset" }));
            shadow.Props.Add(LVec("_ShadowAOShift2", "AO Map", new[] { "3rd Scale", "3rd Offset" }));
            // 2nd / 3rd shadow and the less used options open under "Advanced", like lilToon keeps them further down.
            for(int i = 0; i < shadow.Props.Count; i++)
            {
                string n = shadow.Props[i].Name;
                if(n.StartsWith("_Shadow2nd") || n.StartsWith("_Shadow3rd") || n.StartsWith("_ShadowBorderColor") || n == "_ShadowBorderRange"
                    || n.StartsWith("_ShadowBlurMask") || n.StartsWith("_ShadowBorderMask") || n.StartsWith("_ShadowAOShift")
                    || n == "_ShadowPostAO" || n == "_lilShadowCasterBias" || n == "_ShadowStrengthMaskLOD")
                    shadow.Props[i].Advanced = true;
            }
            list.Add(shadow);

            MacaronSection rimShade = LSection("lil_rimshade", "RimShade", "_UseRimShade");
            rimShade.Props.Add(L("_RimShadeColor", "sColor", MacaronPropKind.Color));
            rimShade.Props.Add(L("_RimShadeMask", "sMask", MacaronPropKind.Texture));
            rimShade.Props.Add(L("_RimShadeNormalStrength", "sNormalStrength", MacaronPropKind.Slider));
            rimShade.Props.Add(L("_RimShadeBorder", "sBorder", MacaronPropKind.Slider));
            rimShade.Props.Add(L("_RimShadeBlur", "sBlur", MacaronPropKind.Slider));
            rimShade.Props.Add(L("_RimShadeFresnelPower", "sFresnelPower", MacaronPropKind.Slider));
            list.Add(rimShade);

            MacaronSection rim = LSection("lil_rim", "sRimLight", "_UseRim");
            rim.Props.Add(Hdr(L("_RimColor", "sColor", MacaronPropKind.Color)));
            rim.Props.Add(L("_RimBorder", "sBorder", MacaronPropKind.Slider));
            rim.Props.Add(L("_RimBlur", "sBlur", MacaronPropKind.Slider));
            rim.Props.Add(L("_RimFresnelPower", "sFresnelPower", MacaronPropKind.Slider));
            rim.Props.Add(L("_RimEnableLighting", "sEnableLighting", MacaronPropKind.Slider));
            list.Add(rim);

            MacaronSection backlight = LSection("lil_backlight", "sBacklight", "_UseBacklight");
            backlight.Props.Add(Hdr(L("_BacklightColor", "sColor", MacaronPropKind.Color)));
            backlight.Props.Add(L("_BacklightBorder", "sBorder", MacaronPropKind.Slider));
            backlight.Props.Add(L("_BacklightBlur", "sBlur", MacaronPropKind.Slider));
            list.Add(backlight);

            MacaronSection outline = LSection("lil_outline", "sOutline", null, "只會套用到有輪廓線的材質。");
            outline.Props.Add(Hdr(L("_OutlineColor", "sColor", MacaronPropKind.Color)));
            outline.Props.Add(L("_OutlineWidth", "sWidth", MacaronPropKind.Slider));
            outline.Props.Add(L("_OutlineEnableLighting", "sEnableLighting", MacaronPropKind.Slider));
            list.Add(outline);

            MacaronSection color = LSection("lil_color", "sColorAdjust");
            color.Props.Add(LVec("_MainTexHSVG", "sColorAdjust", new[] { "sHue", "sSaturation", "sValue", "sGamma" },
                new[] { -0.5f, 0f, 0f, 0.01f }, new[] { 0.5f, 2f, 2f, 2f }));
            list.Add(color);

            return list;
        }

        /// <summary>Every property name used by LilToonSections (enable switches included).</summary>
        public static IEnumerable<string> LilToonPropertyNames()
        {
            foreach(MacaronSection s in LilToonSections)
            {
                if(s.EnableProp != null) yield return s.EnableProp;
                foreach(MacaronProp p in s.Props) yield return p.Name;
            }
        }

        /// <summary>Any lilToon-based material (lilToon itself, Macaron and other lilToon custom shaders).</summary>
        public static bool IsLilToonMaterial(Material m)
        {
            return m != null && m.shader != null && m.HasProperty("_lilToonVersion");
        }

        //------------------------------------------------------------------------------------------
        // Shader helpers

        public static Shader MainShader { get { return Shader.Find(ShaderMain); } }

        public static bool IsMacaronShader(Shader shader)
        {
            if(shader == null) return false;
            // "Macaron/lilToon" and the hidden variants "Hidden/Macaron/..."
            string n = shader.name;
            return n.StartsWith(ShaderRoot + "/", StringComparison.Ordinal) || n.StartsWith("Hidden/" + ShaderRoot + "/", StringComparison.Ordinal);
        }

        public static bool IsMacaronMaterial(Material m)
        {
            return m != null && IsMacaronShader(m.shader);
        }

        public static bool IsBasisProp(string name)
        {
            return Array.IndexOf(BasisProps, name) >= 0;
        }

        /// <summary>Per-avatar data written by the tools: never reset, copied or batch-edited.</summary>
        public static bool IsAvatarDataProp(string name)
        {
            // _MCR_UzuIsLocal: animated by the IsLocal layer (Uzumore, Local Only), its stored value must stay 0.
            return IsBasisProp(name) || name == "_MCR_WorldCapture" || name == "_MCR_UzuIsLocal";
        }

        /// <summary>Set per mesh (Uzumore Layer Offset: inner meshes larger): editable, but never copied from one material
        /// to another (Copy / Paste settings, "Apply all settings of this material").</summary>
        public static bool IsPerMeshProp(string name)
        {
            return name == "_MCR_UzuBias";
        }

        //------------------------------------------------------------------------------------------
        // World capture pass (MCR_WORLD_CAPTURE, LightMode "Always")
        //
        // The pass is submitted for every camera that sees the material, even though it only draws inside this
        // avatar's own world-shadow capture (_MCR_WorldCapture.w = capture key). Materials without a capture key
        // switch it off per material. Material.SetShaderPassEnabled works by LightMode, so this is only done
        // when the capture pass is the shader's single "Always" pass; otherwise the pass stays on (safe default).
        // The capture key is static per material (never animated), so the pass state is derived from it.

        public const string CaptureLightMode = "Always";
        public const string CapturePassName = "MCR_WORLD_CAPTURE";
        // Not cached: lilToon regenerates the shader in place, and a pass scan is a few iterations.
        private static readonly HashSet<string> warnedAlways = new HashSet<string>();

        /// <summary>True when the capture pass is the only "Always" pass of the shader (safe to toggle by LightMode).</summary>
        public static bool CapturePassToggleable(Material m)
        {
            Shader shader = m == null ? null : m.shader;
            if(shader == null) return false;
            bool result;
#if UNITY_2021_2_OR_NEWER
            int always = 0;
            bool hasCapture = false;
            var lightMode = new ShaderTagId("LightMode");
            for(int i = 0; i < shader.passCount; i++)
            {
                if(shader.FindPassTagValue(i, lightMode).name != CaptureLightMode) continue;
                always++;
                hasCapture |= m.GetPassName(i) == CapturePassName;
            }
            result = always == 1 && hasCapture;
            if(always > 1 && warnedAlways.Add(shader.name))
                Debug.LogWarning("[Macaron] " + shader.name + ": more than one \"Always\" pass; the world capture pass is left enabled.");
#else
            result = false;
#endif
            return result;
        }

        // Skin: the detail texture shipped with lilMCR (Textures/MCR_SkinDetail.png).
        public const string SkinTexGuid = "c3a1f0d2b7e44c6f9a58d1e2037b64aa";

        /// <summary>Skin turned on with an empty detail texture slot: fills in the texture shipped with lilMCR.
        /// Returns true when the material was changed (the caller records Undo / marks dirty).</summary>
        public static bool EnsureSkinTexture(Material m)
        {
            if(m == null || !m.HasProperty("_MCR_SkinTex") || !m.HasProperty("_MCR_SkinEnable")) return false;
            if(m.GetFloat("_MCR_SkinEnable") <= 0.5f || m.GetTexture("_MCR_SkinTex") != null) return false;
            Texture tex = AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(SkinTexGuid));
            if(tex == null) return false;
            m.SetTexture("_MCR_SkinTex", tex);
            return true;
        }

        /// <summary>Enables the capture pass only for materials that take part in a world shadow capture.
        /// Returns true when the material was changed (the caller records Undo / marks dirty).</summary>
        public static bool SyncCapturePass(Material m)
        {
            bool want;
            if(!CapturePassNeedsChange(m, out want)) return false;
            m.SetShaderPassEnabled(CaptureLightMode, want);
            return true;
        }

        // Wanted state of the capture pass; true when the material's current state differs from it.
        private static bool CapturePassNeedsChange(Material m, out bool want)
        {
            want = true;
            if(m == null || !m.HasProperty("_MCR_WorldCapture")) return false;
            want = m.GetVector("_MCR_WorldCapture").w > 0.5f || !CapturePassToggleable(m);
            return m.GetShaderPassEnabled(CaptureLightMode) != want;
        }

        /// <summary>SyncCapturePass for every material of an avatar, with Undo. Returns the number changed.</summary>
        public static int SyncCapturePasses(GameObject avatar)
        {
            if(avatar == null) return 0;
            var done = new HashSet<Material>();
            int changed = 0;
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            foreach(Material m in r.sharedMaterials)
            {
                bool want;
                if(m == null || !done.Add(m) || !CapturePassNeedsChange(m, out want)) continue;
                Undo.RecordObject(m, "Macaron Capture Pass");
                m.SetShaderPassEnabled(CaptureLightMode, want);
                EditorUtility.SetDirty(m);
                changed++;
            }
            return changed;
        }

        public static IEnumerable<int> McrPropertyIndices(Shader shader)
        {
            if(shader == null) yield break;
            int count = shader.GetPropertyCount();
            for(int i = 0; i < count; i++)
            {
                if(shader.GetPropertyName(i).StartsWith(PropPrefix, StringComparison.Ordinal)) yield return i;
            }
        }

        /// <summary>All _MCR_ properties back to the shader defaults (= Natural). The avatar axis is kept.</summary>
        public static void ResetToDefaults(Material m, bool includeTextures)
        {
            if(m == null || m.shader == null) return;
            Shader shader = m.shader;
            foreach(int i in McrPropertyIndices(shader))
            {
                string name = shader.GetPropertyName(i);
                if(IsAvatarDataProp(name)) continue;
                SetToDefault(m, shader, i, includeTextures);
            }
            ClearOccMethodStore(m);
            m.SetOverrideTag(TierTag, "");
            m.SetOverrideTag(LegacyTierTag, "");
            // Uzumore's per-avatar values are back at the defaults: let Batch Settings set them up again.
            m.SetOverrideTag(MacaronUzumore.SetupTag, "");
            // The direction chosen in the Converter is this material's default.
            int space;
            Vector3 direction;
            if(TryGetConvertedDirection(m, out space, out direction)) ApplyDirection(m, space, direction);
        }

        //------------------------------------------------------------------------------------------
        // Occlusion method switch (SSAO / Ray March / GTAO)
        //
        // Some occlusion settings are used by both methods. Each method keeps its own copy: when the method changes,
        // the values of the method being left are stored in a material override tag and the stored values of the
        // new method are restored. The first switch to SSAO (nothing stored yet) applies the SSAO starting values.

        public const string PropOccMethod = "_MCR_OccMethod";
        private const string OccStoreTag = "MCR_OccStore";

        /// <summary>Occlusion settings shared by every method (kept per method).</summary>
        public static readonly string[] OccSharedProps =
        {
            "_MCR_OccQuality", "_MCR_OccScreenLOD", "_MCR_OccAmount", "_MCR_OccContrast", "_MCR_OccNoise",
            "_MCR_OccTintNear", "_MCR_OccAlbedoTint", "_MCR_OccReflection", "_MCR_OccBlend",
        };

        // SSAO starting values. Shared ones are always applied on the first switch; SSAO-only ones only while they
        // are still at the shader default (an earlier SSAO setup of this material is kept).
        private static readonly KeyValuePair<string, Vector4>[] SsaoSharedStart =
        {
            new KeyValuePair<string, Vector4>("_MCR_OccQuality", new Vector4(1f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccScreenLOD", new Vector4(1f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccAmount", new Vector4(1f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccContrast", new Vector4(0f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccNoise", new Vector4(0.5f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccTintNear", new Vector4(99f / 255f, 99f / 255f, 99f / 255f, 1f)),   // #636363
            new KeyValuePair<string, Vector4>("_MCR_OccAlbedoTint", new Vector4(1f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccReflection", new Vector4(1f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccBlend", new Vector4(0f, 0, 0, 0)),
        };
        private static readonly KeyValuePair<string, Vector4>[] SsaoOwnStart =
        {
            new KeyValuePair<string, Vector4>("_MCR_SSAOReach", new Vector4(2f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_SSAODepthReach", new Vector4(0.01f, 0, 0, 0)),
        };

        // Soft SSAO starting values of the shared settings (its own settings start from the shader defaults).
        private static readonly KeyValuePair<string, Vector4>[] SoftSharedStart =
        {
            new KeyValuePair<string, Vector4>("_MCR_OccQuality", new Vector4(1f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccAmount", new Vector4(0.8f, 0, 0, 0)),
            new KeyValuePair<string, Vector4>("_MCR_OccNoise", new Vector4(0.2f, 0, 0, 0)),
        };

        private static string OccStoreTagName(int method) { return OccStoreTag + method; }

        /// <summary>Changes the occlusion method of a material, keeping each method's own shared settings.
        /// The caller records Undo and marks the material dirty.</summary>
        // Occlusion Quality values: Standard, and Standard without the 2x2 quad share (Ray March only).
        public const float OccQualityStandard = 1f, OccQualityNo2x2 = 4f;

        public static void SwitchOccMethod(Material m, float method)
        {
            if(m == null || !m.HasProperty(PropOccMethod)) return;
            int from = Mathf.RoundToInt(m.GetFloat(PropOccMethod));
            int to = Mathf.RoundToInt(method);
            m.SetFloat(PropOccMethod, method);
            if(from == to) return;

            m.SetOverrideTag(OccStoreTagName(from), SerializeProps(m, OccSharedProps));
            string stored = m.GetTag(OccStoreTagName(to), false, "");
            if(!string.IsNullOrEmpty(stored))
            {
                DeserializeProps(m, stored);
            }
            else if(to == 0)
            {
                foreach(var kv in SsaoSharedStart) SetValue(m, kv.Key, kv.Value);
                foreach(var kv in SsaoOwnStart) if(IsAtDefault(m, kv.Key)) SetValue(m, kv.Key, kv.Value);
            }
            else
            {
                // First switch to Ray March / GTAO / Soft SSAO: the shared settings start from the shader defaults.
                foreach(string name in OccSharedProps) ResetPropertyToDefault(m, name);
                if(to == 3) foreach(var kv in SoftSharedStart) SetValue(m, kv.Key, kv.Value);
            }
            // SSAO / GTAO have no 2x2 share: "Standard (no 2x2)" is the same as Standard there and is not offered.
            if(to != 1 && m.HasProperty("_MCR_OccQuality") && Mathf.Approximately(m.GetFloat("_MCR_OccQuality"), OccQualityNo2x2))
                m.SetFloat("_MCR_OccQuality", OccQualityStandard);
        }

        //------------------------------------------------------------------------------------------
        // Quality tiers (one-click presets: Converter, Batch Settings, lilMCR PCSS Batch)
        //
        // Every Macaron (_MCR_) and lilMCR PCSS (_lilMCRPCSS_) value of the tier is written; PCSS values are skipped on
        // materials without PCSS. Not written: per-avatar data (avatar axis, world capture key), the shadow direction
        // chosen in the Converter, mask textures, and the PCSS light's own data (light ID, projection, reach) and the
        // avatar-wide "self shadows only" option. The occlusion method changes through SwitchOccMethod, so the
        // per-method settings of the method that is left are kept for a later manual switch.

        public const int TierLightest = 0, TierLight = 1, TierStandard = 2, TierHigh = 3, TierOverkill = 4;
        // Styles follow the quality tiers (TierStyleFirst..TierMax); the buttons show them in a block of their own.
        public const int TierStyleDeep = 5, TierStylePink = 6, TierStyleCyan = 7, TierStyleStd = 8;
        public const int TierStyleFirst = TierStyleDeep;
        /// <summary>The last tier (tiers are 0..TierMax).</summary>
        public const int TierMax = TierStyleStd;
        public const string TierStyleHeader = "風格化";
        public static readonly string[] TierLabels = { "最輕量", "輕量", "標準", "高品質", "Overkill", "POI風(濃)", "POI風(軟粉)", "POI風(軟青)", "POI風(標準)" };
        public static readonly string[] TierTooltips =
        {
            "預估成本：角色像素 +40～70%（對比 lilMCR 全關）。",
            "預估成本：角色像素 +60～100%（對比 lilMCR 全關）。",
            "預估成本：角色像素 +110～190%（對比 lilMCR 全關）。",
            "預估成本：角色像素 +80～140%（對比 lilMCR 全關）。",
            "預估成本：角色像素 +280～500%（對比 lilMCR 全關），PCSS 陰影貼圖另佔約 256 MB 顯示記憶體。",
            "柔和 SSAO（最高品質，每像素 32 次取樣）搭配柔邊的接觸陰影。縫隙的陰影又深又窄，顏色跟著材質本身的顏色。",
            "柔和 SSAO（最高品質，每像素 32 次取樣）搭配細的接觸陰影，陰影帶粉色。粉色會套用到所有材質，要改請調「近處顏色」或把「帶入底色」調回 1。",
            "和「POI風(軟粉)」相同的柔和 SSAO 與細接觸陰影，陰影改為淡青色。顏色會套用到所有材質，要改請調「近處顏色」或把「帶入底色」調回 1。",
            "柔和 SSAO（最高品質，每像素 32 次取樣）範圍中等、顏色為黑色，搭配較長（5 cm）的灰色接觸陰影。整體比「POI風(濃)」柔和、均勻。",
        };

        private static KeyValuePair<string, Vector4> T(string name, float x, float y, float z, float w)
        {
            return new KeyValuePair<string, Vector4>(name, new Vector4(x, y, z, w));
        }

        // Base table = the High tier. Every other tier is this table with some values changed
        // (WithOverrides), so every tier writes the same set of properties.
        private static readonly KeyValuePair<string, Vector4>[] TierBase_ =
        {
            T("_MCR_Active", 1f, 0f, 0f, 0f),
            T("_MCR_AdjActive", 1f, 0f, 0f, 0f),
            T("_MCR_AdjLocalOnly", 0f, 0f, 0f, 0f),
            T("_MCR_AdjBrightness", 1f, 0f, 0f, 0f),
            T("_MCR_AdjFixedBrightness", 0f, 0f, 0f, 0f),
            T("_MCR_AdjLightColorInfluence", 1f, 0f, 0f, 0f),
            T("_MCR_AdjLightPitch", 45f, 0f, 0f, 0f),
            T("_MCR_AdjLightStrength", 0f, 0f, 0f, 0f),
            T("_MCR_AdjLightYaw", 0f, 0f, 0f, 0f),
            T("_MCR_AdjSaturation", 1f, 0f, 0f, 0f),
            T("_MCR_AdjTemperature", 0f, 0f, 0f, 0f),
            T("_MCR_AlphaAware", 1f, 0f, 0f, 0f),
            T("_MCR_Amount", 1f, 0f, 0f, 0f),
            T("_MCR_CasterMaskAmount", 1f, 0f, 0f, 0f),
            T("_MCR_FloorBrightness", 0f, 0f, 0f, 0f),
            T("_MCR_GlobalMaskAmount", 1f, 0f, 0f, 0f),
            T("_MCR_NormalSmooth", 1f, 0f, 0f, 0f),
            T("_MCR_OccAlbedoTint", 1f, 0f, 0f, 0f),
            T("_MCR_OccAmount", 1f, 0f, 0f, 0f),
            T("_MCR_OccAngle", 60f, 0f, 0f, 0f),
            T("_MCR_OccBlend", 0f, 0f, 0f, 0f),
            T("_MCR_OccContrast", 0f, 0f, 0f, 0f),
            T("_MCR_OccEnable", 1f, 0f, 0f, 0f),
            T("_MCR_OccFocus", 0.5f, 0f, 0f, 0f),
            T("_MCR_OccMaskAmount", 1f, 0f, 0f, 0f),
            T("_MCR_OccMaskFlip", 0f, 0f, 0f, 0f),
            T("_MCR_OccMethod", 1f, 0f, 0f, 0f),
            T("_MCR_OccNoise", 0.5f, 0f, 0f, 0f),
            T("_MCR_OccQuality", 4f, 0f, 0f, 0f),
            T("_MCR_OccReach", 0.02f, 0f, 0f, 0f),
            T("_MCR_OccReflection", 1f, 0f, 0f, 0f),
            T("_MCR_OccScaleMode", 0f, 0f, 0f, 0f),
            T("_MCR_OccScreenLOD", 1f, 0f, 0f, 0f),
            T("_MCR_OccSoftness", 1f, 0f, 0f, 0f),
            T("_MCR_OccThickness", 0.015f, 0f, 0f, 0f),
            T("_MCR_SSAOReach", 9.5f, 0f, 0f, 0f),
            T("_MCR_ShadowBackShade", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowEnable", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowIntensity", 0.5f, 0f, 0f, 0f),
            T("_MCR_ShadowJitter", 0f, 0f, 0f, 0f),
            T("_MCR_ShadowLength", 0.005f, 0f, 0f, 0f),
            T("_MCR_ShadowMaskAmount", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowNormalReject", 0f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowReject", 0.2f, 0f, 0f, 0f),
            T("_MCR_ShadowScreenLOD", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowSoftness", 0f, 0f, 0f, 0f),
            T("_MCR_ShadowThickness", 0.0005f, 0f, 0f, 0f),
            T("_lilMCRPCSS_AlbedoTint", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_BackShadow", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_BlockerRange", 0.5f, 0f, 0f, 0f),
            T("_lilMCRPCSS_ConeEdge", 0.01f, 0f, 0f, 0f),
            T("_lilMCRPCSS_DepthBias", 0f, 0f, 0f, 0f),
            T("_lilMCRPCSS_Enable", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_LightSize", 0.005f, 0f, 0f, 0f),
            T("_lilMCRPCSS_MaxLevel", 0.0005f, 0f, 0f, 0f),
            T("_lilMCRPCSS_MinBlur", 2f, 0f, 0f, 0f),
            T("_lilMCRPCSS_Mode", 0f, 0f, 0f, 0f),
            T("_lilMCRPCSS_NormalBias", 4f, 0f, 0f, 0f),
            T("_lilMCRPCSS_PenumbraAlbedo", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_PenumbraSaturation", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_PenumbraTint", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_Quality", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_ShadowSaturation", 0.1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_Strength", 1f, 0f, 0f, 0f),
            T("_MCR_FadeDistance", 0f, 5f, 0f, 0f),
            T("_MCR_OccTintFar", 0f, 0f, 0f, 1f),
            T("_MCR_OccTintNear", 0.37f, 0.37f, 0.37f, 1f),
            T("_MCR_SSAODepthReach", 0.04f, 0f, 0f, 0f),
            T("_MCR_SSAOBias", 0.0008f, 0f, 0f, 0f),
            T("_MCR_SSAOSpread", 0.35f, 0f, 0f, 0f),
            T("_MCR_SoftBias", 0.0025f, 0f, 0f, 0f),
            T("_MCR_SoftClean", 0.4f, 0f, 0f, 0f),
            T("_MCR_SoftDepthReach", 0.3f, 0f, 0f, 0f),
            T("_MCR_SoftFocus", 0.22f, 0f, 0f, 0f),
            T("_MCR_SoftLift", 0.09f, 0f, 0f, 0f),
            T("_MCR_SoftRadius", 0.0165f, 0f, 0f, 0f),
            T("_MCR_SSAOEdgeReject", 0.3f, 0f, 0f, 0f),
            T("_MCR_ShadowColor", 0.21960777f, 0.21960777f, 0.21960777f, 1f),
            T("_MCR_ShadowOffset", 0f, 0.001f, 0f, 0f),
            T("_lilMCRPCSS_ShadowColor", 0.7f, 0.7f, 0.7f, 1f),
            T("_lilMCRPCSS_PenumbraColor", 1f, 0.6f, 0.4f, 1f),
            T("_lilMCRPCSS_PenumbraColor2", 0.85f, 0.35f, 0.5f, 1f),
            // GTAO (shader defaults; the Standard tier overrides them)
            T("_MCR_GTAOReach", 0.01f, 0f, 0f, 0f),
            T("_MCR_GTAODepthReach", 0.05f, 0f, 0f, 0f),
            T("_MCR_GTAOSoftness", 0.6f, 0f, 0f, 0f),
            T("_MCR_GTAOWideAmount", 0f, 0f, 0f, 0f),
            T("_MCR_GTAOWideReach", 0.06f, 0f, 0f, 0f),
            // Uzumore: every option back to the shader defaults in every tier. Reach / Layer Offset are then set per
            // avatar again by ApplyTierToAvatar (MacaronUzumore.Setup). The mask texture is kept, like every mask.
            T("_MCR_UzuEnable", 1f, 0f, 0f, 0f),
            T("_MCR_UzuAmount", 0.15f, 0f, 0f, 0f),
            T("_MCR_UzuBias", 0.001f, 0f, 0f, 0f),
            T("_MCR_UzuBrightness", 0.2f, 0f, 0f, 0f),
            T("_MCR_UzuBackReject", 0.5f, 0f, 0f, 0f),
            T("_MCR_UzuSelf", 0f, 0f, 0f, 0f),
            T("_MCR_UzuMaskAmount", 1f, 0f, 0f, 0f),
        };

        // The other tiers are the base (High) tier with these values changed.
        private static readonly KeyValuePair<string, Vector4>[] TierLightest_ = WithOverrides(new[]
        {
            T("_MCR_OccMethod", 0f, 0f, 0f, 0f),
            T("_MCR_OccQuality", 0f, 0f, 0f, 0f),
            T("_MCR_OccAmount", 1f, 0f, 0f, 0f),
            T("_MCR_OccNoise", 0.2f, 0f, 0f, 0f),
            T("_MCR_OccAlbedoTint", 0.5f, 0f, 0f, 0f),
            T("_MCR_OccTintNear", 0.5943396f, 0.5943396f, 0.5943396f, 1f),
            T("_MCR_SSAOReach", 2.5f, 0f, 0f, 0f),
            T("_MCR_SSAODepthReach", 0.016f, 0f, 0f, 0f),
            T("_MCR_SSAOBias", 0f, 0f, 0f, 0f),
            T("_MCR_SSAOSpread", 0.6f, 0f, 0f, 0f),
            T("_MCR_SSAOEdgeReject", 0f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", -1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_MinBlur", 1.5f, 0f, 0f, 0f),
        });

        // Standard: GTAO with a soft gradient, medium contact shadow, PCSS minimum blur 2.
        private static readonly KeyValuePair<string, Vector4>[] StandardOverrides =
        {
            T("_MCR_OccMethod", 2f, 0f, 0f, 0f),
            T("_MCR_OccQuality", 2f, 0f, 0f, 0f),
            T("_MCR_OccAmount", 1f, 0f, 0f, 0f),
            T("_MCR_OccNoise", 1f, 0f, 0f, 0f),
            T("_MCR_OccTintNear", 0.424528122f, 0.422525644f, 0.422525644f, 1f),
            T("_MCR_GTAOReach", 0.01f, 0f, 0f, 0f),
            T("_MCR_GTAODepthReach", 0.09f, 0f, 0f, 0f),
            T("_MCR_GTAOSoftness", 1f, 0f, 0f, 0f),
            T("_MCR_GTAOWideAmount", 0f, 0f, 0f, 0f),
            T("_MCR_GTAOWideReach", 0.06f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", 1f, 0f, 0f, 0f),
            T("_lilMCRPCSS_MinBlur", 2f, 0f, 0f, 0f),
        };

        // Light: the Standard values with GTAO at Light quality and a Low contact shadow.
        private static readonly KeyValuePair<string, Vector4>[] TierLight_ = WithOverrides(StandardOverrides, new[]
        {
            T("_MCR_OccQuality", 0f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", 0f, 0f, 0f, 0f),
        });

        private static readonly KeyValuePair<string, Vector4>[] TierStandard_ = WithOverrides(StandardOverrides);

        // High: the base table itself.
        private static readonly KeyValuePair<string, Vector4>[] TierHigh_ = TierBase_;

        // Overkill: everything at the highest quality.
        private static readonly KeyValuePair<string, Vector4>[] TierOverkill_ = WithOverrides(new[]
        {
            T("_MCR_OccQuality", 3f, 0f, 0f, 0f),
            // Enough directions at this quality for a mostly fixed pattern: far less grain.
            T("_MCR_OccNoise", 0.2f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", 3f, 0f, 0f, 0f),
            T("_lilMCRPCSS_MinBlur", 3f, 0f, 0f, 0f),
            T("_lilMCRPCSS_Quality", 3f, 0f, 0f, 0f),
        });

        // Styles: looks to choose from, shown in their own block under the quality tiers. Each one is a tier of its own
        // (the High table with its overrides), so it can be applied, recorded and replaced like any other tier.

        // Deep soft creases: Soft SSAO at its best quality with a short Radius gathered near the pixel and the
        // contrast curve at its strongest (narrow, deep crease shading in the material's own color), and a soft-edged
        // contact shadow without the back side shade.
        private static readonly KeyValuePair<string, Vector4>[] TierStyleDeep_ = WithOverrides(new[]
        {
            T("_MCR_OccMethod", 3f, 0f, 0f, 0f),
            T("_MCR_OccQuality", 3f, 0f, 0f, 0f),
            T("_MCR_OccAmount", 1f, 0f, 0f, 0f),
            T("_MCR_OccContrast", -1f, 0f, 0f, 0f),
            T("_MCR_OccNoise", 0.2f, 0f, 0f, 0f),
            T("_MCR_OccAlbedoTint", 1f, 0f, 0f, 0f),
            T("_MCR_OccTintNear", 0f, 0f, 0f, 1f),
            T("_MCR_SSAOEdgeReject", 0.3f, 0f, 0f, 0f),
            T("_MCR_SoftRadius", 0.004f, 0f, 0f, 0f),
            T("_MCR_SoftDepthReach", 0.3f, 0f, 0f, 0f),
            T("_MCR_SoftFocus", 0.7f, 0f, 0f, 0f),
            T("_MCR_SoftLift", 0.09f, 0f, 0f, 0f),
            T("_MCR_SoftBias", 0f, 0f, 0f, 0f),
            T("_MCR_SoftClean", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowIntensity", 0.7f, 0f, 0f, 0f),
            T("_MCR_ShadowColor", 0f, 0f, 0f, 1f),
            T("_MCR_ShadowLength", 0.0079f, 0f, 0f, 0f),
            T("_MCR_ShadowSoftness", 12.69f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", 2f, 0f, 0f, 0f),
            T("_MCR_ShadowJitter", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowBackShade", 0f, 0f, 0f, 0f),
        });

        // Soft pink: Soft SSAO at its best quality with a wide, gentle falloff, and a thin contact shadow without the
        // back side shade. The shading is a fixed skin-shadow pink on every material (Albedo Tint off).
        private static readonly KeyValuePair<string, Vector4>[] StylePinkOverrides =
        {
            T("_MCR_OccMethod", 3f, 0f, 0f, 0f),
            T("_MCR_OccQuality", 3f, 0f, 0f, 0f),
            T("_MCR_OccAmount", 0.8f, 0f, 0f, 0f),
            T("_MCR_OccNoise", 0.2f, 0f, 0f, 0f),
            T("_MCR_OccAlbedoTint", 0f, 0f, 0f, 0f),
            T("_MCR_OccTintNear", 0.856f, 0.698f, 0.724f, 1f),
            T("_MCR_SoftRadius", 0.0165f, 0f, 0f, 0f),
            T("_MCR_SoftDepthReach", 0.3f, 0f, 0f, 0f),
            T("_MCR_SoftFocus", 0.22f, 0f, 0f, 0f),
            T("_MCR_SoftLift", 0.09f, 0f, 0f, 0f),
            T("_MCR_SoftBias", 0.0025f, 0f, 0f, 0f),
            T("_MCR_SoftClean", 0.4f, 0f, 0f, 0f),
            T("_MCR_ShadowIntensity", 0.5f, 0f, 0f, 0f),
            T("_MCR_ShadowColor", 0.896f, 0.706f, 0.769f, 1f),
            T("_MCR_ShadowJitter", 1f, 0f, 0f, 0f),
            T("_MCR_ShadowBackShade", 0f, 0f, 0f, 0f),
        };
        private static readonly KeyValuePair<string, Vector4>[] TierStylePink_ = WithOverrides(StylePinkOverrides);

        // Soft cyan: Soft pink with a pale cyan (#B2C9DA) for both the occlusion tint and the contact shadow.
        // (Soft SSAO only uses the near tint; the far tint is left alone so it cannot leak into another method.)
        private static readonly KeyValuePair<string, Vector4>[] TierStyleCyan_ = WithOverrides(StylePinkOverrides, new[]
        {
            T("_MCR_OccTintNear", 0.698f, 0.788f, 0.855f, 1f),
            T("_MCR_ShadowColor", 0.698f, 0.788f, 0.855f, 1f),
        });

        // Standard: Soft SSAO at its best quality with a medium Radius and a plain black tint, and a longer gray contact
        // shadow with most of the back side shade.
        private static readonly KeyValuePair<string, Vector4>[] TierStyleStd_ = WithOverrides(new[]
        {
            T("_MCR_OccMethod", 3f, 0f, 0f, 0f),
            T("_MCR_OccQuality", 3f, 0f, 0f, 0f),
            T("_MCR_OccAmount", 1f, 0f, 0f, 0f),
            T("_MCR_OccContrast", -0.2f, 0f, 0f, 0f),
            T("_MCR_OccNoise", 0.2f, 0f, 0f, 0f),
            T("_MCR_OccAlbedoTint", 0f, 0f, 0f, 0f),
            T("_MCR_OccTintNear", 0f, 0f, 0f, 1f),
            T("_MCR_SSAOEdgeReject", 0.3f, 0f, 0f, 0f),
            T("_MCR_SoftRadius", 0.009f, 0f, 0f, 0f),
            T("_MCR_SoftDepthReach", 0.3f, 0f, 0f, 0f),
            T("_MCR_SoftFocus", 0.38f, 0f, 0f, 0f),
            T("_MCR_SoftLift", 0.09f, 0f, 0f, 0f),
            T("_MCR_SoftBias", 0.0006f, 0f, 0f, 0f),
            T("_MCR_SoftClean", 0.38f, 0f, 0f, 0f),
            T("_MCR_ShadowIntensity", 0.865f, 0f, 0f, 0f),
            T("_MCR_ShadowColor", 0.311f, 0.311f, 0.311f, 1f),
            T("_MCR_ShadowLength", 0.05f, 0f, 0f, 0f),
            T("_MCR_ShadowThickness", 0.003f, 0f, 0f, 0f),
            // Keeps surfaces that face away from the light from shading themselves (long shadow, thick occluder).
            T("_MCR_ShadowNormalReject", 0.5f, 0f, 0f, 0f),
            T("_MCR_ShadowSoftness", 0f, 0f, 0f, 0f),
            T("_MCR_ShadowQuality", 2f, 0f, 0f, 0f),
            T("_MCR_ShadowJitter", 0.5f, 0f, 0f, 0f),
            T("_MCR_ShadowBackShade", 0.75f, 0f, 0f, 0f),
        });

        // The base table with the override lists applied in order (a later list wins).
        private static KeyValuePair<string, Vector4>[] WithOverrides(params KeyValuePair<string, Vector4>[][] overrideLists)
        {
            var result = (KeyValuePair<string, Vector4>[])TierBase_.Clone();
            foreach(var overrides in overrideLists)
            {
                for(int i = 0; i < result.Length; i++)
                {
                    foreach(var o in overrides) if(o.Key == result[i].Key) result[i] = o;
                }
            }
            return result;
        }

        private static KeyValuePair<string, Vector4>[] TierValues(int tier)
        {
            switch(tier)
            {
                case TierLightest: return TierLightest_;
                case TierLight: return TierLight_;
                case TierHigh: return TierHigh_;
                case TierOverkill: return TierOverkill_;
                case TierStyleDeep: return TierStyleDeep_;
                case TierStylePink: return TierStylePink_;
                case TierStyleCyan: return TierStyleCyan_;
                case TierStyleStd: return TierStyleStd_;
                default: return TierStandard_;
            }
        }

        /// <summary>Applies a quality tier to one material. The caller records Undo and marks it dirty.</summary>
        public static void ApplyTier(Material m, int tier)
        {
            if(m == null || m.shader == null || !m.HasProperty(PropOccMethod)) return;
            KeyValuePair<string, Vector4>[] values = TierValues(tier);
            foreach(var kv in values)
            {
                if(kv.Key == PropOccMethod) { SwitchOccMethod(m, kv.Value.x); break; }
            }
            foreach(var kv in values)
            {
                if(kv.Key == PropOccMethod) continue;
                SetValue(m, kv.Key, kv.Value);
            }
            int applied = tier >= TierLightest && tier <= TierMax ? tier : TierStandard;
            m.SetOverrideTag(TierTag, applied.ToString(System.Globalization.CultureInfo.InvariantCulture));
            m.SetOverrideTag(LegacyTierTag, "");
            // Uzumore Reach / Layer Offset are back at the defaults until MacaronUzumore.Setup runs for the avatar.
            if(m.HasProperty("_MCR_UzuAmount")) m.SetOverrideTag(MacaronUzumore.SetupTag, "");
        }

        // The tier last applied to a material is kept in an override tag, so lilMCR PCSS can follow it when it is set up later.
        // "MCR_Tier" is an older 4-tier numbering (0 Lightest, 1 Light, 2 High, 3 Overkill), still read for older materials.
        private const string TierTag = "MCR_Tier2";
        private const string LegacyTierTag = "MCR_Tier";
        private static readonly int[] LegacyTierMap = { TierLightest, TierLight, TierHigh, TierOverkill };

        /// <summary>The quality tier last applied to the material, or -1 (never applied, or reset since).</summary>
        public static int AppliedTier(Material m)
        {
            if(m == null) return -1;
            var ns = System.Globalization.NumberStyles.Integer;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            int t;
            if(int.TryParse(m.GetTag(TierTag, false, ""), ns, ci, out t))
                return t >= TierLightest && t <= TierMax ? t : -1;
            if(int.TryParse(m.GetTag(LegacyTierTag, false, ""), ns, ci, out t) && t >= 0 && t < LegacyTierMap.Length)
                return LegacyTierMap[t];
            return -1;
        }

        /// <summary>The quality tier applied to most of the avatar's materials, or -1 when none has one.</summary>
        public static int AppliedTier(GameObject avatar)
        {
            if(avatar == null) return -1;
            int[] votes = new int[TierMax + 1];
            var seen = new HashSet<Material>();
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || !seen.Add(m)) continue;
                    int t = AppliedTier(m);
                    if(t >= 0) votes[t]++;
                }
            }
            int best = -1;
            for(int t = 0; t < votes.Length; t++) if(votes[t] > 0 && (best < 0 || votes[t] > votes[best])) best = t;
            return best;
        }

        /// <summary>Writes only the tier values whose names start with prefix (lilMCR PCSS values after its shader swap).</summary>
        public static void ApplyTierValues(Material m, int tier, string prefix)
        {
            if(m == null || m.shader == null) return;
            foreach(var kv in TierValues(tier)) if(kv.Key.StartsWith(prefix, StringComparison.Ordinal)) SetValue(m, kv.Key, kv.Value);
        }

        /// <summary>Per-avatar part of a tier that lives outside Macaron. lilMCR PCSS registers the shadow map
        /// resolution of its light here (Macaron cannot reference the add-on's assembly).</summary>
        public static event Action<GameObject, int> TierAppliedToAvatar;

        /// <summary>Call after ApplyTier on an avatar's materials.</summary>
        public static void ApplyTierToAvatar(GameObject avatar, int tier) { ApplyTierToAvatar(avatar, tier, null); }

        /// <summary>ApplyTierToAvatar that leaves the given materials alone (the Converter's excluded materials).</summary>
        public static void ApplyTierToAvatar(GameObject avatar, int tier, ICollection<Material> exclude)
        {
            if(avatar == null) return;
            // The tier reset Uzumore's Reach / Layer Offset: set them for this avatar again.
            MacaronUzumore.Setup(avatar, exclude);
            if(TierAppliedToAvatar != null) TierAppliedToAvatar(avatar, tier);
        }

        /// <summary>ApplyTier for several materials with one Undo step. Returns the number changed.</summary>
        public static int ApplyTier(IEnumerable<Material> materials, int tier)
        {
            var list = new List<Material>();
            foreach(Material m in materials) if(m != null && m.HasProperty(PropOccMethod) && !list.Contains(m)) list.Add(m);
            if(list.Count == 0) return 0;
            Undo.RecordObjects(list.ToArray(), "Macaron Quality Tier");
            foreach(Material m in list)
            {
                ApplyTier(m, tier);
                EditorUtility.SetDirty(m);
            }
            return list.Count;
        }

        public static void ClearOccMethodStore(Material m)
        {
            if(m == null) return;
            for(int method = 0; method <= 2; method++) m.SetOverrideTag(OccStoreTagName(method), "");
        }

        // Float / Range / Color / Vector properties only: name=x,y,z,w;...
        private static string SerializeProps(Material m, string[] names)
        {
            var sb = new System.Text.StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach(string name in names)
            {
                int i = m.shader.FindPropertyIndex(name);
                if(i < 0) continue;
                Vector4 v = GetValue(m, name, m.shader.GetPropertyType(i));
                sb.Append(name).Append('=')
                  .Append(v.x.ToString("R", ci)).Append(',').Append(v.y.ToString("R", ci)).Append(',')
                  .Append(v.z.ToString("R", ci)).Append(',').Append(v.w.ToString("R", ci)).Append(';');
            }
            return sb.ToString();
        }

        private static void DeserializeProps(Material m, string data)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach(string entry in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = entry.IndexOf('=');
                if(eq <= 0) continue;
                string name = entry.Substring(0, eq);
                if(Array.IndexOf(OccSharedProps, name) < 0) continue;      // only the shared settings
                string[] parts = entry.Substring(eq + 1).Split(',');
                if(parts.Length != 4) continue;
                var v = new Vector4();
                bool ok = true;
                for(int c = 0; c < 4; c++)
                {
                    float f;
                    if(!float.TryParse(parts[c], System.Globalization.NumberStyles.Float, ci, out f)) { ok = false; break; }
                    v[c] = f;
                }
                if(ok) SetValue(m, name, v);
            }
        }

        private static Vector4 GetValue(Material m, string name, UnityEngine.Rendering.ShaderPropertyType type)
        {
            if(type == UnityEngine.Rendering.ShaderPropertyType.Color) { Color c = m.GetColor(name); return new Vector4(c.r, c.g, c.b, c.a); }
            if(type == UnityEngine.Rendering.ShaderPropertyType.Vector) return m.GetVector(name);
            return new Vector4(m.GetFloat(name), 0, 0, 0);
        }

        private static void SetValue(Material m, string name, Vector4 v)
        {
            int i = m.shader.FindPropertyIndex(name);
            if(i < 0) return;
            var type = m.shader.GetPropertyType(i);
            if(type == UnityEngine.Rendering.ShaderPropertyType.Color) m.SetColor(name, new Color(v.x, v.y, v.z, v.w));
            else if(type == UnityEngine.Rendering.ShaderPropertyType.Vector) m.SetVector(name, v);
            else if(type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range) m.SetFloat(name, v.x);
        }

        private static bool IsAtDefault(Material m, string name)
        {
            int i = m.shader.FindPropertyIndex(name);
            if(i < 0) return false;
            var type = m.shader.GetPropertyType(i);
            Vector4 cur = GetValue(m, name, type);
            Vector4 def = type == UnityEngine.Rendering.ShaderPropertyType.Vector || type == UnityEngine.Rendering.ShaderPropertyType.Color
                ? m.shader.GetPropertyDefaultVectorValue(i)
                : new Vector4(m.shader.GetPropertyDefaultFloatValue(i), 0, 0, 0);
            return (cur - def).sqrMagnitude < 1e-10f;
        }

        public static void ResetPropertyToDefault(Material m, string name)
        {
            if(m == null || m.shader == null) return;
            int i = m.shader.FindPropertyIndex(name);
            if(i < 0) return;
            int space;
            Vector3 direction;
            if((name == PropShadowDirection || name == PropShadowSpace) && TryGetConvertedDirection(m, out space, out direction))
            {
                // The direction chosen in the Converter is this material's default.
                if(name == PropShadowDirection) m.SetVector(name, new Vector4(direction.x, direction.y, direction.z, 0f));
                else m.SetFloat(name, space);
                return;
            }
            SetToDefault(m, m.shader, i, true);
        }

        private static void SetToDefault(Material m, Shader shader, int i, bool includeTextures)
        {
            string name = shader.GetPropertyName(i);
            switch(shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    m.SetFloat(name, shader.GetPropertyDefaultFloatValue(i));
                    break;
                case ShaderPropertyType.Color:
                    m.SetColor(name, shader.GetPropertyDefaultVectorValue(i));
                    break;
                case ShaderPropertyType.Vector:
                    m.SetVector(name, shader.GetPropertyDefaultVectorValue(i));
                    break;
                case ShaderPropertyType.Texture:
                    if(!includeTextures) break;
                    m.SetTexture(name, null);
                    m.SetTextureScale(name, Vector2.one);
                    m.SetTextureOffset(name, Vector2.zero);
                    break;
            }
        }

        /// <summary>Copies one property (any type) from src to dst. Both must have it.</summary>
        public static void CopyProperty(Material src, Material dst, string name)
        {
            if(src == null || dst == null || dst.shader == null) return;
            if(!src.HasProperty(name) || !dst.HasProperty(name)) return;
            int i = dst.shader.FindPropertyIndex(name);
            if(i < 0) return;
            switch(dst.shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    dst.SetFloat(name, src.GetFloat(name));
                    break;
                case ShaderPropertyType.Color:
                    dst.SetColor(name, src.GetColor(name));
                    break;
                case ShaderPropertyType.Vector:
                    dst.SetVector(name, src.GetVector(name));
                    break;
                case ShaderPropertyType.Texture:
                    dst.SetTexture(name, src.GetTexture(name));
                    dst.SetTextureScale(name, src.GetTextureScale(name));
                    dst.SetTextureOffset(name, src.GetTextureOffset(name));
                    break;
            }
        }

        public static Vector3 DefaultDirection()
        {
            Shader s = MainShader;
            if(s != null)
            {
                int i = s.FindPropertyIndex(PropShadowDirection);
                if(i >= 0)
                {
                    Vector4 v = s.GetPropertyDefaultVectorValue(i);
                    Vector3 d = new Vector3(v.x, v.y, v.z);
                    if(d.sqrMagnitude > 1e-6f) return d.normalized;
                }
            }
            return new Vector3(-0.35820895f, 0.23880596f, 0.9025841f).normalized;
        }

        /// <summary>The shader default of the Contact Shadow "方向基準" (0 Avatar / 1 Camera / 2 World).</summary>
        public static int DefaultSpace()
        {
            Shader s = MainShader;
            if(s != null)
            {
                int i = s.FindPropertyIndex(PropShadowSpace);
                if(i >= 0) return Mathf.Clamp(Mathf.RoundToInt(s.GetPropertyDefaultFloatValue(i)), 0, 2);
            }
            return 1;
        }

        //------------------------------------------------------------------------------------------
        // Direction chosen in the Converter. It is the "default" of the material from then on: the reset buttons and the
        // direction pad's "恢復預設方向" use it instead of the shader default.

        private const string ConvertedDirectionTag = "MCR_ConvertDir";

        public static void SetConvertedDirection(Material m, int space, Vector3 direction)
        {
            if(m == null) return;
            if(direction.sqrMagnitude < 1e-6f) direction = Vector3.up;
            direction.Normalize();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            m.SetOverrideTag(ConvertedDirectionTag, string.Format(ci, "{0:R},{1:R},{2:R},{3}", direction.x, direction.y, direction.z, space));
        }

        /// <summary>The direction / space written by the Converter. false for materials converted before it was recorded.</summary>
        public static bool TryGetConvertedDirection(Material m, out int space, out Vector3 direction)
        {
            space = DefaultSpace();
            direction = DefaultDirection();
            if(m == null) return false;
            string[] parts = m.GetTag(ConvertedDirectionTag, false, "").Split(',');
            if(parts.Length != 4) return false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var ns = System.Globalization.NumberStyles.Float;
            float x, y, z;
            int sp;
            if(!float.TryParse(parts[0], ns, ci, out x) || !float.TryParse(parts[1], ns, ci, out y) || !float.TryParse(parts[2], ns, ci, out z)
                || !int.TryParse(parts[3], System.Globalization.NumberStyles.Integer, ci, out sp)) return false;
            Vector3 d = new Vector3(x, y, z);
            if(d.sqrMagnitude < 1e-6f) return false;
            space = Mathf.Clamp(sp, 0, 2);
            direction = d.normalized;
            return true;
        }

        /// <summary>Reset target of the direction pad: the conversion direction when it was made in the same space, else the shader default.</summary>
        public static Vector3 DefaultDirectionFor(Material m, int space)
        {
            int convertedSpace;
            Vector3 d;
            return TryGetConvertedDirection(m, out convertedSpace, out d) && convertedSpace == space ? d : DefaultDirection();
        }

        public static void ApplyDirection(Material m, int space, Vector3 direction)
        {
            if(m == null) return;
            if(direction.sqrMagnitude < 1e-6f) direction = Vector3.up;
            direction.Normalize();
            if(m.HasProperty(PropShadowSpace)) m.SetFloat(PropShadowSpace, space);
            if(m.HasProperty(PropShadowDirection)) m.SetVector(PropShadowDirection, new Vector4(direction.x, direction.y, direction.z, 0f));
        }

        //------------------------------------------------------------------------------------------
        // Avatar axis ("跟隨角色" space)
        //
        // The shader turns the direction into object space with _MCR_AvatarBasisX/Y/Z and then uses
        // unity_ObjectToWorld. For a SkinnedMeshRenderer that matrix is the root bone (or the renderer
        // when there is no root bone), so the axes are written in that space.
        //   X = right as seen from the front (= the avatar's left), Y = up, Z = the avatar's front.

        public static Transform ObjectSpaceTransform(Renderer r)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if(smr != null && smr.rootBone != null) return smr.rootBone;
            return r.transform;
        }

        public static void ComputeAvatarBasis(Renderer r, Transform avatarRoot, out Vector3 bx, out Vector3 by, out Vector3 bz)
        {
            Transform root = avatarRoot != null ? avatarRoot : r.transform.root;
            Matrix4x4 worldToObject = ObjectSpaceTransform(r).worldToLocalMatrix;
            bx = SafeNormalize(worldToObject.MultiplyVector(-root.right), Vector3.left);
            by = SafeNormalize(worldToObject.MultiplyVector(root.up), Vector3.up);
            bz = SafeNormalize(worldToObject.MultiplyVector(root.forward), Vector3.forward);
        }

        public static void WriteAvatarBasis(Material m, Renderer r, Transform avatarRoot)
        {
            if(m == null || r == null || !m.HasProperty(BasisProps[0])) return;
            Vector3 bx, by, bz;
            ComputeAvatarBasis(r, avatarRoot, out bx, out by, out bz);
            m.SetVector(BasisProps[0], new Vector4(bx.x, bx.y, bx.z, 0f));
            m.SetVector(BasisProps[1], new Vector4(by.x, by.y, by.z, 0f));
            m.SetVector(BasisProps[2], new Vector4(bz.x, bz.y, bz.z, 0f));
        }

        private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        {
            return v.sqrMagnitude > 1e-10f ? v.normalized : fallback;
        }

        /// <summary>VRC Avatar Descriptor above t, else the top-most Animator, else the hierarchy root.</summary>
        public static Transform FindAvatarRoot(Transform t)
        {
            Transform animatorRoot = null;
            for(Transform cur = t; cur != null; cur = cur.parent)
            {
                foreach(Component c in cur.GetComponents<Component>())
                {
                    if(c != null && c.GetType().Name == "VRCAvatarDescriptor") return cur;
                }
                if(cur.GetComponent<Animator>() != null) animatorRoot = cur;
            }
            return animatorRoot != null ? animatorRoot : (t != null ? t.root : null);
        }

        /// <summary>Renderers of an avatar, SkinnedMeshRenderers first (their space wins when a material is shared).</summary>
        public static List<Renderer> AvatarRenderers(GameObject avatar)
        {
            List<Renderer> list = new List<Renderer>();
            if(avatar == null) return list;
            list.AddRange(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            foreach(Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if(r is SkinnedMeshRenderer) continue;
                if(r is MeshRenderer) list.Add(r);
            }
            return list;
        }

        /// <summary>Writes the avatar axis of every given material that appears in the avatar. Returns the count.</summary>
        public static int WriteAvatarBasisForAvatar(GameObject avatar, ICollection<Material> only)
        {
            if(avatar == null) return 0;
            HashSet<Material> done = new HashSet<Material>();
            foreach(Renderer r in AvatarRenderers(avatar))
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || done.Contains(m) || !IsMacaronMaterial(m)) continue;
                    if(only != null && !only.Contains(m)) continue;
                    WriteAvatarBasis(m, r, avatar.transform);
                    EditorUtility.SetDirty(m);
                    done.Add(m);
                }
            }
            return done.Count;
        }

        /// <summary>Finds the materials in the open scenes and writes their avatar axis. Returns the count.</summary>
        public static int WriteAvatarBasisFromScene(ICollection<Material> materials)
        {
            HashSet<Material> done = new HashSet<Material>();
            List<Renderer> renderers = new List<Renderer>();
            renderers.AddRange(UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>(true));
            renderers.AddRange(UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true));
            foreach(Renderer r in renderers)
            {
                foreach(Material m in r.sharedMaterials)
                {
                    if(m == null || done.Contains(m) || !materials.Contains(m)) continue;
                    WriteAvatarBasis(m, r, FindAvatarRoot(r.transform));
                    EditorUtility.SetDirty(m);
                    done.Add(m);
                }
            }
            return done.Count;
        }

        //------------------------------------------------------------------------------------------
        // Misc

        // Creates an asset folder and any missing parents, deepest existing ancestor first (recursive).
        public static void EnsureFolder(string assetPath)
        {
            string path = assetPath.Replace('\\', '/').TrimEnd('/');
            if(path.Length == 0 || AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            if(cut <= 0) return;                      // "Assets" itself always exists
            string parent = path.Substring(0, cut);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(cut + 1));
        }

        private static readonly HashSet<char> InvalidFileChars = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());

        // One pass over the name with a single buffer (instead of one string copy per invalid character).
        public static string SafeFileName(string name)
        {
            if(string.IsNullOrEmpty(name)) return "Avatar";
            char[] chars = name.ToCharArray();
            for(int i = 0; i < chars.Length; i++) if(InvalidFileChars.Contains(chars[i])) chars[i] = '_';
            string safe = new string(chars).Trim();
            return safe.Length == 0 ? "Avatar" : safe;
        }
    }

    //==============================================================================================
    // Small GUI pieces shared by the windows and the Inspector
    //==============================================================================================

    public static class MacaronGUI
    {
        // Pale blue-violet palette
        public static readonly Color Accent = new Color(0.74f, 0.72f, 0.96f);      // lavender
        public static readonly Color AccentDark = new Color(0.44f, 0.42f, 0.68f);  // deep periwinkle (banner)
        public static readonly Color Mint = new Color(0.70f, 0.82f, 0.98f);        // pale sky blue (positive chip)
        public static readonly Color Lemon = new Color(0.86f, 0.78f, 0.98f);       // pale lilac (secondary chip / light dot)
        public static readonly Color Neutral = new Color(0.80f, 0.80f, 0.88f);     // grey-lavender (inactive chip)

        private static GUIStyle titleStyle, subtitleStyle, stepStyle, chipStyle, centerMini;

        private static void InitStyles()
        {
            if(titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            titleStyle.normal.textColor = Color.white;
            subtitleStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            subtitleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            stepStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            chipStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            chipStyle.normal.textColor = new Color(0.12f, 0.12f, 0.12f);
            centerMini = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        public static void Banner(string title, string subtitle)
        {
            InitStyles();
            Rect r = GUILayoutUtility.GetRect(10f, 52f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, AccentDark);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 3f, r.width, 3f), Accent);
            GUI.Label(new Rect(r.x + 12f, r.y + 6f, r.width - 24f, 22f), title, titleStyle);
            GUI.Label(new Rect(r.x + 12f, r.y + 27f, r.width - 24f, 20f), subtitle, subtitleStyle);
            GUILayout.Space(6f);
        }

        public static void Step(int number, string title)
        {
            InitStyles();
            Rect r = GUILayoutUtility.GetRect(10f, 22f, GUILayout.ExpandWidth(true));
            Rect badge = new Rect(r.x, r.y + 2f, 18f, 18f);
            EditorGUI.DrawRect(badge, Accent);
            GUI.Label(badge, number.ToString(), chipStyle);
            GUI.Label(new Rect(r.x + 26f, r.y, r.width - 26f, r.height), title, stepStyle);
        }

        public static void Chip(string text, Color color, float width)
        {
            InitStyles();
            Rect r = GUILayoutUtility.GetRect(width, 20f, GUILayout.Width(width));
            EditorGUI.DrawRect(r, color);
            GUI.Label(r, text, chipStyle);
        }

        // Style buttons per row (the block grows by rows as styles are added).
        private const int StylesPerRow = 4;

        /// <summary>The quality tier buttons in one row and, under them, the style buttons in a block of their own.
        /// Returns the tier clicked, or -1.</summary>
        public static int TierButtons(int highlighted = -1)
        {
            int clicked = -1;
            Color old = GUI.backgroundColor;
            using(new EditorGUILayout.HorizontalScope())
            {
                for(int i = 0; i < MacaronCore.TierStyleFirst; i++)
                {
                    GUI.backgroundColor = i == highlighted ? Accent : old;
                    if(TierButton(i)) clicked = i;
                }
            }
            GUI.backgroundColor = old;
            GUILayout.Space(2f);
            EditorGUILayout.LabelField(ML.T(MacaronCore.TierStyleHeader), EditorStyles.miniBoldLabel);
            for(int first = MacaronCore.TierStyleFirst; first <= MacaronCore.TierMax; first += StylesPerRow)
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    for(int n = 0; n < StylesPerRow; n++)
                    {
                        int i = first + n;
                        // An unfinished row keeps the button width of a full one.
                        if(i > MacaronCore.TierMax) { GUILayout.Label(GUIContent.none, GUILayout.Height(24f)); continue; }
                        GUI.backgroundColor = i == highlighted ? Accent : old;
                        if(TierButton(i)) clicked = i;
                    }
                }
            }
            GUI.backgroundColor = old;
            return clicked;
        }

        private static bool TierButton(int tier)
        {
            return GUILayout.Button(new GUIContent(ML.T(MacaronCore.TierLabels[tier]), ML.T(MacaronCore.TierTooltips[tier])), GUILayout.Height(24f));
        }

        public static bool BigButton(string text, float height)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = Accent;
            bool clicked = GUILayout.Button(text, GUILayout.Height(height));
            GUI.backgroundColor = old;
            return clicked;
        }

        //------------------------------------------------------------------------------------------
        // Foldouts
        //
        // Parent header  = full width, same look as lilToon's own section headers (lilEditorGUI.Foldout).
        // Child header   = lined up with lilToon's own child boxes (the margins of lilEditorGUI.boxOuter), a little lower
        //                  than the parent header, so a child (AO, contact shadow, PCSS ...) is told apart from its parent
        //                  (Macaron) by its width and height, with the same text size as lilToon.
        // InsetFoldout   = small foldout used inside a box ("進階設定"); its arrow is moved well inside the box border.

        public const float ChildIndent = 14f;
        private const float InsetArrow = 14f;
        // Arrow of the header foldouts: clear of the rounded left border of the header box.
        private const float HeaderArrowX = 10f;
        private static GUIStyle childHeaderStyle, parentHeaderStyle;
        private static bool headerSkinPro;

        private static void InitHeaders()
        {
            if(childHeaderStyle != null && headerSkinPro == EditorGUIUtility.isProSkin) return;
            headerSkinPro = EditorGUIUtility.isProSkin;
            GUIStyle module = GUI.skin.FindStyle("ShurikenModuleTitle") ?? EditorStyles.helpBox;
            parentHeaderStyle = new GUIStyle(module)
            {
                font = EditorStyles.label.font,
                fontSize = EditorStyles.label.fontSize,
                fontStyle = EditorStyles.label.fontStyle,
                border = new RectOffset(15, 7, 4, 4),
                contentOffset = new Vector2(HeaderArrowX + 16f, -2f),
                fixedHeight = 22f
            };
            childHeaderStyle = new GUIStyle(module)
            {
                font = EditorStyles.label.font,
                fontSize = EditorStyles.label.fontSize,
                fontStyle = EditorStyles.label.fontStyle,
                border = new RectOffset(15, 7, 4, 4),
                contentOffset = new Vector2(HeaderArrowX + 16f, -2f),
                fixedHeight = 20f
            };
        }

        private static bool HeaderFoldout(Rect rect, string title, bool open, GUIStyle style)
        {
            Event e = Event.current;
            if(e.type == EventType.Repaint)
            {
                style.Draw(rect, new GUIContent(title), false, false, false, false);
                Rect arrow = new Rect(rect.x + HeaderArrowX, rect.y + Mathf.Max(1f, (rect.height - 13f) * 0.5f - 1f), 13f, 13f);
                EditorStyles.foldout.Draw(arrow, false, false, open, false);
            }
            if(e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                open = !open;
                e.Use();
            }
            return open;
        }

        /// <summary>Full-width section header (lilToon style). Used for parents in windows (lilToon draws its own in the Inspector).</summary>
        public static bool ParentFoldout(string title, bool open)
        {
            InitHeaders();
            Rect rect = GUILayoutUtility.GetRect(16f, 22f, parentHeaderStyle, GUILayout.ExpandWidth(true));
            return HeaderFoldout(rect, title, open, parentHeaderStyle);
        }

        /// <summary>Child section header, lined up with lilToon's child boxes (boxOuter margins) and a little lower than the
        /// parent header. Draw the child's box with lilEditorGUI.boxOuter right below it so both edges line up.</summary>
        public static bool ChildFoldout(string title, bool open)
        {
            InitHeaders();
            RectOffset margin = global::lilToon.lilEditorGUI.boxOuter.margin;
            Rect rect = GUILayoutUtility.GetRect(16f, 20f, childHeaderStyle, GUILayout.ExpandWidth(true));
            rect.xMin += margin.left;
            rect.xMax -= margin.right;
            return HeaderFoldout(rect, title, open, childHeaderStyle);
        }

        /// <summary>Foldout inside a box: the arrow sits a few pixels inside the box instead of on its border.</summary>
        public static bool InsetFoldout(bool open, string title)
        {
            Rect r = EditorGUILayout.GetControlRect();
            int indent = EditorGUI.indentLevel;
            r.xMin += InsetArrow + indent * 15f;
            EditorGUI.indentLevel = 0;
            try
            {
                return EditorGUI.Foldout(r, open, title, true);
            }
            finally
            {
                EditorGUI.indentLevel = indent;
            }
        }

        /// <summary>Indents everything drawn inside it by ChildIndent (child header + its box), so children are narrower than the parent.</summary>
        // GUI.Scope skips CloseScope while the GUI is exiting (GUIUtility.ExitGUI), like Unity's own layout scopes.
        public sealed class ChildScope : GUI.Scope
        {
            public ChildScope() : this(ChildIndent) { }

            public ChildScope(float indent)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent);
                EditorGUILayout.BeginVertical();
            }

            protected override void CloseScope()
            {
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
        }

        //------------------------------------------------------------------------------------------
        // Light direction pad
        //
        // A disc = the light seen from the front (avatar space) / from the camera / along world Z.
        // Center = light straight from the front, rim = light from the side. "前 / 後" picks the hemisphere.
        // The arrow shows where the shadow falls.

        private static readonly int PadHash = "MacaronDirectionPad".GetHashCode();

        /// <returns>true when the user changed the direction.</returns>
        /// <param name="resetDirection">Direction of the "恢復預設方向" button (the conversion direction); null = shader default.</param>
        public static bool DirectionPad(ref Vector3 direction, int space, bool mixed, Vector3? resetDirection = null)
        {
            InitStyles();
            Vector3 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
            bool changed = false;

            float size = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.36f, 110f, 150f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f + 4f);
            Rect pad = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));

            EditorGUILayout.BeginVertical();
            bool front = dir.z >= 0f;
            int sel = GUILayout.Toolbar(front ? 0 : 1, new[] { ML.T("光在前方"), ML.T("光在後方") });
            if(sel != (front ? 0 : 1))
            {
                float z = Mathf.Max(Mathf.Abs(dir.z), 0.3f) * (sel == 0 ? 1f : -1f);
                Vector2 xy = new Vector2(dir.x, dir.y);
                float xyLen = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                xy = xy.sqrMagnitude > 1e-6f ? xy.normalized * xyLen : new Vector2(0f, xyLen);
                dir = new Vector3(xy.x, xy.y, z);
                changed = true;
            }

            GUILayout.Space(4f);
            if(mixed) EditorGUILayout.LabelField(ML.T("（多個材質的方向不同）"), EditorStyles.wordWrappedMiniLabel);
            else EditorGUILayout.LabelField(Describe(dir, space), EditorStyles.wordWrappedLabel);
            GUILayout.Space(2f);
            EditorGUILayout.LabelField(ML.T(space == 0 ? "以「從正面看角色」時的左右為準。" : (space == 1 ? "以畫面的上下左右為準。" : "圓盤 = 世界 X / Y，前後 = 世界 Z。")), EditorStyles.wordWrappedMiniLabel);
            GUILayout.FlexibleSpace();
            if(GUILayout.Button(ML.T("恢復預設方向"), EditorStyles.miniButton))
            {
                dir = resetDirection.HasValue && resetDirection.Value.sqrMagnitude > 1e-6f
                    ? resetDirection.Value.normalized : MacaronCore.DefaultDirection();
                changed = true;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // Mouse
            int id = GUIUtility.GetControlID(PadHash, FocusType.Passive, pad);
            Event e = Event.current;
            Vector2 c = pad.center;
            float radius = pad.width * 0.5f - 8f;
            switch(e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if(e.button == 0 && pad.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        dir = FromPad(e.mousePosition, c, radius, front);
                        changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if(GUIUtility.hotControl == id)
                    {
                        dir = FromPad(e.mousePosition, c, radius, front);
                        changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if(GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    DrawPad(pad, c, radius, dir, space, mixed);
                    break;
            }

            if(changed)
            {
                direction = dir.normalized;
                GUI.changed = true;
            }
            return changed;
        }

        private static Vector3 FromPad(Vector2 mouse, Vector2 c, float radius, bool front)
        {
            Vector2 v = (mouse - c) / radius;
            v.y = -v.y;
            if(v.magnitude > 1f) v.Normalize();
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - v.sqrMagnitude));
            z = Mathf.Max(z, 0.02f) * (front ? 1f : -1f);
            return new Vector3(v.x, v.y, z).normalized;
        }

        private static void DrawPad(Rect pad, Vector2 c, float radius, Vector3 dir, int space, bool mixed)
        {
            bool pro = EditorGUIUtility.isProSkin;
            EditorGUI.DrawRect(pad, pro ? new Color(0.15f, 0.15f, 0.20f) : new Color(0.84f, 0.84f, 0.93f));

            Color guide = pro ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.22f);
            Handles.color = guide;
            Handles.DrawWireDisc(new Vector3(c.x, c.y, 0f), Vector3.forward, radius);
            Handles.DrawWireDisc(new Vector3(c.x, c.y, 0f), Vector3.forward, radius * 0.5f);
            Handles.DrawLine(new Vector3(c.x - radius, c.y, 0f), new Vector3(c.x + radius, c.y, 0f));
            Handles.DrawLine(new Vector3(c.x, c.y - radius, 0f), new Vector3(c.x, c.y + radius, 0f));

            // Figure (avatar space) or screen frame (camera / world)
            Color figure = pro ? new Color(0.76f, 0.76f, 0.95f, 0.55f) : new Color(0.32f, 0.30f, 0.52f, 0.45f);
            Handles.color = figure;
            if(space == 0)
            {
                Handles.DrawSolidDisc(new Vector3(c.x, c.y - radius * 0.36f, 0f), Vector3.forward, radius * 0.2f);
                Rect body = new Rect(c.x - radius * 0.24f, c.y - radius * 0.12f, radius * 0.48f, radius * 0.72f);
                EditorGUI.DrawRect(body, figure);
            }
            else
            {
                Rect frame = new Rect(c.x - radius * 0.42f, c.y - radius * 0.28f, radius * 0.84f, radius * 0.56f);
                Handles.DrawSolidRectangleWithOutline(frame, new Color(figure.r, figure.g, figure.b, 0.12f), figure);
            }

            if(mixed) return;

            Vector2 lightPx = c + new Vector2(dir.x, -dir.y) * radius;
            bool front = dir.z >= 0f;

            // Shadow arrow: opposite of the light, length = how much it is a side light
            Vector2 toShadow = -(lightPx - c);
            float side = toShadow.magnitude / radius;
            if(side > 0.05f)
            {
                Vector2 n = toShadow.normalized;
                Vector2 start = c + n * radius * 0.08f;
                Vector2 end = c + n * radius * (0.25f + 0.55f * side);
                Handles.color = new Color(0.24f, 0.20f, 0.48f, pro ? 0.95f : 0.85f);
                Handles.DrawAAPolyLine(4f, new Vector3(start.x, start.y, 0f), new Vector3(end.x, end.y, 0f));
                Vector2 left = new Vector2(-n.y, n.x);
                Vector3[] head =
                {
                    new Vector3(end.x + n.x * 7f, end.y + n.y * 7f, 0f),
                    new Vector3(end.x + left.x * 5f, end.y + left.y * 5f, 0f),
                    new Vector3(end.x - left.x * 5f, end.y - left.y * 5f, 0f)
                };
                Handles.DrawAAConvexPolygon(head);
            }

            // Light
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            Handles.DrawAAPolyLine(1.5f, new Vector3(c.x, c.y, 0f), new Vector3(lightPx.x, lightPx.y, 0f));
            Handles.color = front ? Lemon : new Color(Lemon.r, Lemon.g, Lemon.b, 0.5f);
            if(front)
            {
                Handles.DrawSolidDisc(new Vector3(lightPx.x, lightPx.y, 0f), Vector3.forward, 7f);
            }
            else
            {
                Handles.DrawWireDisc(new Vector3(lightPx.x, lightPx.y, 0f), Vector3.forward, 7f);
                Handles.DrawWireDisc(new Vector3(lightPx.x, lightPx.y, 0f), Vector3.forward, 5f);
            }

            GUI.Label(new Rect(pad.x, pad.yMax - 16f, pad.width, 14f), ML.T(front ? "● 光在前方" : "○ 光在後方"), centerMini);
        }

        public static string Describe(Vector3 dir, int space)
        {
            int h = dir.x < -0.3f ? -1 : (dir.x > 0.3f ? 1 : 0);    // -1 left, 1 right
            int v = dir.y > 0.3f ? 1 : (dir.y < -0.3f ? -1 : 0);     // 1 up, -1 down
            int d = dir.z > 0.35f ? 1 : (dir.z < -0.35f ? -1 : 0);   // 1 front, -1 back, 0 side
            switch(ML.Current)
            {
                case ML.Language.English:
                {
                    string from = DescribeEn(h, v, "front");
                    string to = (h != 0 || v != 0) ? DescribeEn(-h, -v, null) : "straight behind (barely visible)";
                    string side = d > 0 ? "front" : (d < 0 ? "back" : "side");
                    return "Light from the " + from + " (" + side + ")\nShadow falls to the " + to;
                }
                case ML.Language.Korean:
                {
                    string from = DescribeKo(h, v, "정면");
                    string to = (h != 0 || v != 0) ? DescribeKo(-h, -v, null) : "바로 뒤 (거의 보이지 않음)";
                    string side = d > 0 ? "앞쪽" : (d < 0 ? "뒤쪽" : "측면");
                    return from + "·" + side + "에서 빛이 비침\n그림자 방향: " + to;
                }
                case ML.Language.Japanese:
                {
                    string from = DescribeJa(h, v, "正面");
                    string to = (h != 0 || v != 0) ? DescribeJa(-h, -v, null) : "真後ろ（ほぼ見えない）";
                    string side = d > 0 ? "前方" : (d < 0 ? "後方" : "側面");
                    return from + "・" + side + "から光が当たる\n影の向き：" + to;
                }
                default:
                {
                    string hs = h < 0 ? "左" : (h > 0 ? "右" : "");
                    string vs = v > 0 ? "上" : (v < 0 ? "下" : "");
                    string ds = d > 0 ? "前方" : (d < 0 ? "後方" : "側面");
                    string from = (hs + vs).Length > 0 ? (hs + vs) : "正";
                    string sh = h < 0 ? "右" : (h > 0 ? "左" : "");
                    string sv = v > 0 ? "下" : (v < 0 ? "上" : "");
                    string to = (sh + sv).Length > 0 ? (sh + sv) : "正後方（幾乎看不到）";
                    return "光從" + from + "・" + ds + "照過來\n影子落向：" + to;
                }
            }
        }

        private static string DescribeEn(int h, int v, string center)
        {
            string hs = h < 0 ? "left" : (h > 0 ? "right" : "");
            string vs = v > 0 ? "upper" : (v < 0 ? "lower" : "");
            if(hs.Length == 0 && vs.Length == 0) return center;
            if(hs.Length == 0) return v > 0 ? "top" : "bottom";
            return vs.Length > 0 ? vs + " " + hs : hs;
        }

        private static string DescribeKo(int h, int v, string center)
        {
            string hs = h < 0 ? "왼쪽" : (h > 0 ? "오른쪽" : "");
            string vs = v > 0 ? "위" : (v < 0 ? "아래" : "");
            if(hs.Length == 0 && vs.Length == 0) return center;
            if(hs.Length == 0) return vs;
            return vs.Length > 0 ? hs + " " + vs : hs;
        }

        private static string DescribeJa(int h, int v, string center)
        {
            string vs = v > 0 ? "上" : (v < 0 ? "下" : "");
            string hs = h < 0 ? "左" : (h > 0 ? "右" : "");
            if(hs.Length == 0 && vs.Length == 0) return center;
            return hs + vs;
        }
    }
}
#endif
