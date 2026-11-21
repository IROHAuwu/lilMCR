<div align="center">

# lilMCR (Macaron)

**A lilToon-based avatar shader and toolset for VRChat**

![version](https://img.shields.io/badge/version-v1.5.8-ff8fab)
![lilToon](https://img.shields.io/badge/lilToon-2.x-8ecae6)
![platform](https://img.shields.io/badge/VRChat-Avatar-b8a1ff)
![UI](https://img.shields.io/badge/UI-English%20%7C%20繁中%20%7C%20日本語%20%7C%20한국어-lightgrey)

**English** | [繁體中文](README.zh-TW.md)

</div>

lilMCR (Macaron) is a custom shader for [lilToon](https://github.com/lilxyzw/lilToon) 2.x.
It keeps lilToon's look and settings and adds contact shadows, occlusion and light adjustment, along with one-click conversion and batch-editing tools.

## ✨ Features

### Shader
| Feature | Description |
|---|---|
| **Contact Shadow** | Tight shadows on the avatar that follow the light direction. The affected area can be limited with a mask |
| **Occlusion** | Darkens creases and areas where surfaces meet for more depth. The affected area can be limited with a mask |
| **Uzumore** | When the camera goes inside a mesh, vertices are pushed back in front of the camera so you can't see through it |
| **Local Only** | lilMCR effects are shown only on your own client; other players see regular lilToon |
| **Adaptive Screen Quality** | Shadow and occlusion quality adjusts to how much of the screen the avatar covers |

All lilToon variants are supported (Opaque, Cutout, Transparent, Fur, Gem, Outline, Lite, Multi and more).

### Tools
Found under **`Macaron`** in the Unity menu bar.

| Tool | Menu | Description |
|---|---|---|
| **Avatar Converter** | `lilMCR/Avatar Converter` | Drop in an avatar, pick the shadow direction, and convert its lilToon materials to Macaron in one click. Original materials are never modified |
| **Batch Settings** | `lilMCR/Batch Settings` | Edit every Macaron material on an avatar at once |
| **Light Adjuster** | Added from the converter | Adjust brightness, color temperature, saturation and light direction from the VRChat menu, and switch lilMCR on or off |
| **World Shadow** | `World Shadow` | Setup tool for world shadows |
| **Mask Generator** | `Mask Generator` | Paint mask textures directly on the UV layout. Meshes and source textures are left untouched |

### Add-ons
| Folder | Description |
|---|---|
| **lilMCR PCSS** | Percentage-closer soft shadows: shadows get softer the farther they are from the caster, with an optional PCF (light) type and a penumbra tint taken from the base color. Menu: `Macaron/lilMCR PCSS` |
| **lilMCR Param Compressor** | Compresses synced parameters at upload when they exceed VRChat's limit. Avatars within the limit are not changed |

Each add-on is a separate folder. Delete the ones you don't need.

## 📦 Installation

**Requirements**
- [lilToon](https://github.com/lilxyzw/lilToon) 2.x
- VRChat SDK (Avatars)
- [Modular Avatar](https://modular-avatar.nadena.dev/) (for the Light Adjuster and other menu features)
- [NDMF](https://github.com/bdunderscore/ndmf) (for the Param Compressor)

**Steps**
1. Install lilToon in your project.
2. Download from [Releases](https://github.com/IROHAuwu/lilMCR/releases), or download this repository.
3. Copy the `Assets/!Macaron_edit` folder into your Unity project's `Assets/` folder.

## 🚀 Quick start

1. Open `Macaron > lilMCR > Avatar Converter`.
2. Drag your avatar into the window and choose the shadow direction.
3. Press convert. Fine-tune afterwards with `Batch Settings`.

The tools are non-destructive: conversion works on copies of your materials, and menus and parameters are merged by Modular Avatar / NDMF only at upload. Delete the generated object to undo.

## 📁 Folder structure

```
Assets/!Macaron_edit/
├─ lilMCR/                  Main shader, inspector, converter, World Shadow, etc.
├─ lilMCR_PCSS/             PCSS soft shadow add-on
├─ lilMCR_ParamCompressor/  Parameter compressor
└─ lilMCR_MaskGenerator/    UV mask generator
```

## 🌐 Languages

The tool UI is available in English, Traditional Chinese, Japanese and Korean. By default it follows the language selected in the lilToon inspector.

## 📜 License and credits

- **lilToon** by lilxyzw. See [`LICENSE_lilToon.txt`](Assets/!Macaron_edit/lilMCR/LICENSE_lilToon.txt)
- **UzumoreShader** by sigmal00 (MIT). The Uzumore feature follows its idea; the implementation in lilMCR is its own
- **ND Parameter Compressor** by sakuraimikoto33 (MIT), itself based on Laura's Param Compressor (MIT)

Full third-party notices are in the `THIRD_PARTY_NOTICES.txt` file of each folder.
