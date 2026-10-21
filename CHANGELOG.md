# Changelog

## 0.11.2

### Added
- **Shift+Alt+X** also opens and closes the settings window (Home stays).

### Fixed
- **Menu clicks landed below the button.** The pointer is now taken from the screen position, not from `Event.mousePosition`, which came out above the system pointer.
- **Scalp cap depth** (`DLSS/ScalpDepth`).

### Changed
- Dead constants, RVAs and unused imports removed; the version is read from `Plugin.PluginVersion` (attribute, logs, menu title). No compiler warnings left.

## 0.1.15

### Changed
- **NVIDIA libraries updated to 310.1.15** (`nvngx_dlss.dll`, `nvngx_dlssg.dll`, `nvngx_dlssnr.dll`). The neural rendering library is the build for RTX 40, 30 and 20 (the RTX 50 build is a separate file; swap it into `ngx\` on RTX 50 if wanted).

## 0.9.15

### Fixed
- **Crash when switching the DLSS model preset with frame generation on** (for example K to Auto, an access violation in the driver's NGX). Frame generation is now closed, not just paused, during the preset swap and reopens by itself afterwards. All `nvngx_dlss*` modules are pinned in memory. The open DLSS feature keeps its preset until the swap, instead of being rebuilt twice in a row.
- **Shadow of her hair left hanging in the air.** The cause was the shadow cache: lights remembered the shadows of objects that had stopped moving and did not redraw them. The cache is now off by default, with a clear name and description in the menu ("Shadow cache (faster, can leave stale shadows)") and the new setting `Optimization/ShadowCache`. Strand hair is also kept out of the cache.
- **Neural rendering failing to start on RTX 40 and 50** (`CreateFeature(14) failed`). The bridge mod's FP16 network is built for RTX 20 and 30 and its GPU code does not load on newer cards, so it is requested on RTX 20 and 30 only.
- **Menu text running out of its buttons.** Button text stays on one line and the font shrinks to fit, down to size 9. Long checkbox and slider labels fit too.
- **Noisy logs.** Detailed lines are written only when `VerboseLog = true`. Warnings and errors are always written.

### Changed
- **Ultra Quality and the free size slider removed.** Frame sizes that are not one of DLSS's own (Ultra Quality 0.77, any other scale) made the skin soft (forehead, neck, cheeks). The Quality list is DLAA, Quality, Balanced, Performance, Ultra Performance; a saved Ultra Quality (or the removed Custom) runs as Quality.

- **Soft, streaky skin with preset K.** Skin, brows and the scalp cap are transparent-queue materials that write no motion vectors, so DLSS kept old history on them (K holds history longest). They now write motion vectors while the plugin runs (`DLSS/TransparentMotionVectors`, on). The hair's own motion-vector code asked HDRP for a property that does not exist (`_TransparentWritesMotionVec`); it now uses the real `_TransparentWritingMotionVec`.
- **Auto preset is M for every mode**, and neural rendering runs after DLSS by default (`General/NeuralAfterDlss`), which keeps M's edges steady.

### Added
- **Preset L** in the model preset list, with a description in the menu.
- README rewritten for 0.9.15.
- The archive now carries the bridge mod for RTX 20 and 30 (`3 - Bridge (RTX 20 and 30)`).

### Known issues
- Not tested as a clean install from the archive.
