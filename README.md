# CMA DLSS NR

NVIDIA DLSS, DLSS neural rendering and DLSS frame generation for *Corrupted Modeling Agency* 0.65.

## What it does

- **DLSS** - sharp anti-aliasing at full resolution (DLAA), or a smaller frame reconstructed to your screen for more frames a second (Ultra Quality, Quality, Balanced, Performance, Ultra Performance). A sharpness slider.
- **Neural rendering** - NVIDIA's network relights and refines the picture. It can run before DLSS (faster) or after it (finer).
- **Mask** - separate neural rendering strength for the scene, skin, hair and clothing.
- **Frame generation** - one generated frame between every two rendered ones, with NVIDIA Reflex to keep the controls responsive.
- **Game effects** - the game's bloom, subsurface scattering, depth of field, motion blur, chromatic aberration, vignette and film grain can be switched off.

## What you need

- Windows and an NVIDIA RTX graphics card with an up-to-date driver.
- Frame generation needs an RTX 40 or 50 card and "Hardware-accelerated GPU scheduling" switched on in the Windows graphics settings.
- No other mod or injector that adds DLSS to the same game at the same time.

It does not work on AMD or Intel graphics cards.

## Install

The archive has two folders. Close the game first.

**Step 1 - BepInEx (the mod loader).** Skip this step if the game already has a `BepInEx` folder and other BepInEx mods work.

Open the folder `1 - BepInEx`, select everything inside it and copy it into the game's folder - the one that holds `Corrupted Modeling Agency.exe`.

**Step 2 - the mod.**

Open the folder `2 - Mod`, select the `BepInEx` folder inside it and copy it into the same game folder. Say yes if Windows asks to merge folders.

When you are done the game folder looks like this:

```
Corrupted Modeling Agency.exe
winhttp.dll
doorstop_config.ini
dotnet\
BepInEx\
  core\
  plugins\
    CMADlssNr\
      CMADlssNr.dll
      nvngx.dll\DlssBridge.dll
      ngx\nvngx_dlss.dll
      ngx\nvngx_dlssg.dll
      ngx\nvngx_dlssnr.dll
```

**Step 3.** Start the game. The very first start after installing BepInEx takes a few minutes and needs an internet connection: BepInEx prepares itself for the game. A console window may stay open during that time. Later starts are as fast as usual.

## Use

Press **Insert** in the game to open the settings window. Press it again to close it; your settings are saved.

The window has five pages:

| Page | What is there |
|---|---|
| DLSS | On/off, quality, sharpness, model preset |
| Neural | On/off, before or after DLSS, intensity, local tone, local structure, style, passes |
| Mask | Separate strength for scene, skin, hair and clothing |
| Frame Gen | Frame generation, frame pacing, Reflex |
| Game | The game's own effects |

Out of the box: DLSS at DLAA with preset M, neural rendering on, frame generation off, Reflex on.

## Tips

- **Too slow?** Lower the DLSS quality (Quality or Balanced), or switch neural rendering off - it is the heaviest part.
- **RTX 20 or 30 card?** Pick model preset **E** (or **F** at DLAA and Ultra Performance): the older, much lighter models.
- **RTX 20 or 30 card, neural rendering?** It needs the FP16 network of the bridge mod (`version.dll` and `dlssg_to_fsr3.ini` beside the game). With it installed, the Neural page has an "FP16 network" switch; it is written to the ini and applies at the next start of the game.
- **Glow or a halo round bright skin?** That is the game's bloom. Switch it off on the Game page.
- **"After DLSS"** for neural rendering looks finer but costs as much as running at full resolution, whatever the DLSS quality.
- **Controls feel delayed with frame generation?** Keep Reflex on.
- **Insert is taken by something else?** Change `Menu` in `BepInEx\config\community.cma.dlssnr.cfg` (the file appears after the first start).

## Uninstall

Delete the folder `BepInEx\plugins\CMADlssNr`.

## Credits

- BepInEx 6 (bleeding edge build 785) - https://github.com/BepInEx/BepInEx, LGPL-2.1.
- DLSS, DLSS frame generation, neural rendering and Reflex are NVIDIA's.
