<div align="center">

# lilMCR (Macaron)

**以 lilToon 為基礎的 VRChat 角色著色器與工具組**

![version](https://img.shields.io/badge/version-v1.6.3-ff8fab)
![lilToon](https://img.shields.io/badge/lilToon-2.x-8ecae6)
![platform](https://img.shields.io/badge/VRChat-Avatar-b8a1ff)
![UI](https://img.shields.io/badge/UI-繁中%20%7C%20English%20%7C%20日本語%20%7C%20한국어-lightgrey)

[English](README.md) | **繁體中文**

</div>

lilMCR（Macaron）是 [lilToon](https://github.com/lilxyzw/lilToon) 2.x 的自訂著色器。
在保留 lilToon 原有外觀與設定的前提下，加上接觸陰影、遮蔽、光線調整等功能，並附上一鍵轉換與批次設定工具。

## ✨ 功能

### 著色器
| 功能 | 說明 |
|---|---|
| **接觸陰影** Contact Shadow | 依光源方向在角色身上產生貼近的陰影，可用遮罩控制範圍 |
| **遮蔽** Occlusion | 讓凹陷與貼合處變暗，增加立體感。有四種方式可選（SSAO、柔和 SSAO、GTAO、Ray March），可用遮罩控制範圍 |
| **部位細節** Part Detail | 在材質上加上細紋與柔和高光：皮膚（細紋與毛孔）或布料纖維（織紋）。由 lilMCR 自己繪製，不會更動材質原本的 lilToon 設定 |
| **埋入** Uzumore | 鏡頭進入網格內部時，把頂點推回鏡頭前方，避免穿模看穿 |
| **僅自己可見** Local Only | lilMCR 的效果只在自己的畫面顯示，其他玩家看到的是一般 lilToon |
| **自適應畫質** | 依畫面佔比自動調整陰影與遮蔽的品質 |

支援 lilToon 的所有變體（不透明、Cutout、透明、Fur、Gem、Outline、Lite、Multi 等）。

### 工具
選單位置：Unity 上方的 **`Macaron`**

| 工具 | 選單 | 說明 |
|---|---|---|
| **角色轉換** | `lilMCR/Avatar Converter` | 放入角色、選好影子方向，一鍵把 lilToon 材質轉成 Macaron。品質檔次與風格預設一鍵套用。原始材質不會被修改 |
| **批次設定** | `lilMCR/Batch Settings` | 一次調整整個角色的 Macaron 材質 |
| **Light Adjuster** | 由轉換工具加入 | 在 VRChat 選單內調整亮度、色溫、飽和度、光源方向，並可開關 lilMCR |
| **World Shadow** | `World Shadow` | 世界陰影的設定工具 |
| **Mask Generator** | `Mask Generator` | 直接在 UV 上畫出遮罩貼圖，不會更動網格與原貼圖 |

### 附加元件
| 資料夾 | 說明 |
|---|---|
| **lilMCR PCSS** | PCSS 軟陰影（距離越遠越柔和的陰影），可切換成 PCF（輕量），並可用底色為影子邊緣著色（半影著色）。選單：`Macaron/lilMCR PCSS` |
| **lilMCR Param Compressor** | 上傳時若同步參數超過 VRChat 上限，自動壓縮；未超過則完全不更動 |

附加元件都是獨立資料夾，不需要的可以直接刪除。

## 📦 安裝

**需求**
- [lilToon](https://github.com/lilxyzw/lilToon) 2.x
- VRChat SDK（Avatars）
- [Modular Avatar](https://modular-avatar.nadena.dev/)（Light Adjuster 等選單功能需要）
- [NDMF](https://github.com/bdunderscore/ndmf)（Param Compressor 需要）

**步驟**
1. 先在專案內裝好 lilToon。
2. 從 [Releases](https://github.com/IROHAuwu/lilMCR/releases) 下載，或直接下載本儲存庫。
3. 將 `Assets/!Macaron_edit` 資料夾複製到 Unity 專案的 `Assets/` 底下。

## 🚀 快速開始

1. 開啟 `Macaron > lilMCR > Avatar Converter`。
2. 把角色拖進視窗，選擇影子的方向。
3. 按下轉換。完成後可用 `Batch Settings` 微調。

工具都是非破壞式的：轉換會另外複製材質，選單與參數在上傳時才由 Modular Avatar／NDMF 合併，刪除對應物件即可還原。

## 📁 資料夾結構

```
Assets/!Macaron_edit/
├─ lilMCR/                  主著色器、Inspector、轉換器、World Shadow 等
├─ lilMCR_PCSS/             PCSS 軟陰影附加元件
├─ lilMCR_ParamCompressor/  參數壓縮工具
└─ lilMCR_MaskGenerator/    UV 遮罩產生器
```

## 🌐 語言

工具介面支援繁體中文、English、日本語、한국어，預設跟隨 lilToon Inspector 的語言設定。

## 📜 授權與致謝

- **lilToon** — lilxyzw。授權見 [`LICENSE_lilToon.txt`](Assets/!Macaron_edit/lilMCR/LICENSE_lilToon.txt)
- **UzumoreShader** — sigmal00（MIT）。埋入功能參考其構想，實作為 lilMCR 自行撰寫
- **ND Parameter Compressor** — sakuraimikoto33（MIT），其基礎為 Laura's Param Compressor（MIT）

完整的第三方聲明見各資料夾內的 `THIRD_PARTY_NOTICES.txt`。
