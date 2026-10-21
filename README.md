# CMA DLSS NR 0.11.2

NVIDIA DLSS, DLSS neural rendering and DLSS frame generation for *Corrupted Modeling Agency* (tested with 0.67).

## What it does

- **DLSS** - sharp anti-aliasing at full resolution (DLAA), or a smaller frame reconstructed to your screen for more frames a second (Quality, Balanced, Performance, Ultra Performance). DLSS's own sizes only: at any other the skin comes out soft, so there is no Ultra Quality and no free size slider. A sharpness slider and the model presets K, M, L, E and F.
- **Neural rendering** - NVIDIA's network relights and refines the picture. It can run before DLSS (faster) or after it (finer).
- **Mask** - separate neural rendering strength and detail for the scene, skin, hair and clothing.
- **Frame generation** - generated frames between the rendered ones (x2 and more where the card allows it), with NVIDIA Reflex to keep the controls responsive.
- **Hair** - the game's strand hair gets motion vectors of its own (it moves with the head), and its soft edges write motion too, so DLSS and the network do not leave a trail behind it.
- **Game effects** - the game's bloom, subsurface scattering, depth of field, motion blur, chromatic aberration, vignette, film grain, screen-space reflections, ambient occlusion, screen-space GI, contact shadows, volumetric fog and clouds can be switched off; the Game page also has the switches of the game's GPU fluids.
- **Bridge** - RTX 20 and 30 cards: frame generation and the faster FP16 neural network come from the *nvidia mfg bridge* mod (see below). On RTX 40 and 50 the card runs the original FP8 network by itself.

## What you need

- Windows and an NVIDIA RTX graphics card with an up-to-date driver.
- Frame generation needs "Hardware-accelerated GPU scheduling" switched on in the Windows graphics settings. On RTX 40 and 50 it is NVIDIA's own; on RTX 20 and 30 it comes from the bridge mod.
- No other mod or injector that adds DLSS to the same game, except the bridge mod below.

It does not work on AMD or Intel graphics cards.

## Install

The archive has three folders (the third only for RTX 20 and 30 cards). Close the game first.

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
  config\community.cma.dlssnr.cfg   (appears after the first start)
  plugins\
    CMADlssNr\
      CMADlssNr.dll
      nvngx.dll\DlssBridge.dll
      nvngx.dll\CMAPresenter.dll
      ngx\nvngx_dlss.dll
      ngx\nvngx_dlssg.dll
      ngx\nvngx_dlssnr.dll
```

**Step 3 (RTX 20 and 30 only) - the bridge mod.** Open the folder `3 - Bridge (RTX 20 and 30)` and copy `version.dll` and `dlssg_to_fsr3.ini` of the *nvidia mfg bridge* mod beside `Corrupted Modeling Agency.exe`. Skip this step on RTX 40 and 50. The plugin keeps its `EnableDlssNr` switch in that ini; it is asked for on RTX 20 and 30 only (on newer cards the bridge's FP16 network cannot start and is left off).

**Step 4.** Start the game. The very first start after installing BepInEx takes a few minutes and needs an internet connection: BepInEx prepares itself for the game. A console window may stay open during that time. Later starts are as fast as usual.

## Use

Press **Home** (or **Shift+Alt+X**) in the game to open the settings window. Press it again to close it; your settings are saved.

The window has six pages:

| Page | What is there |
|---|---|
| DLSS | On/off, quality, sharpness, model preset (Auto, K, M, L, E, F), the hair's motion vectors |
| Neural | On/off, before or after DLSS, intensity, local tone, local structure, style, passes |
| Mask | Separate strength and detail for scene, skin, hair and clothing |
| Frame Gen | Frame generation, multiplier, frame pacing, Reflex |
| Game | The game's own effects and the game's GPU fluids |
| Bridge | The bridge mod's state, the FP16 network (RTX 20 and 30), what is switched on as the game starts, optimizations (including the shadow cache) |

Out of the box: DLSS at DLAA with preset M, neural rendering on (before DLSS), sharpening 0.3, frame generation off, Reflex on, shadow cache off.

## Model presets

- **K** - steady, but it keeps old history longest, so the skin can go soft. **M** - sharper; **Auto** takes M (with neural rendering after DLSS by default, which keeps M's edges steady).
- **L** - NVIDIA's newest model, made for Ultra Performance; heavier than K and M.
- **E** and **F** - the older, much lighter models for RTX 20 and 30 cards: E for upscaling, F for DLAA and Ultra Performance.

Changing the preset closes DLSS and opens it again; with frame generation on, that one is closed and opened again too.

## Tips

- **Too slow?** Lower the DLSS quality (Quality or Balanced), or switch neural rendering off - it is the heaviest part.
- **RTX 20 or 30 card?** Pick model preset **E** (or **F** at DLAA and Ultra Performance). For neural rendering install the bridge mod; the Bridge page then has an "FP16 network" switch, which applies at the next start of the game.
- **A shadow stays where something has moved away?** Keep the **Shadow cache** off (the default). It is a speed-up on the Bridge page: spot and point lights remember the shadows of what has not moved; with it on, a shadow of her hair could stay in the air.
- **Glow or a halo round bright skin?** That is the game's bloom. Switch it off on the Game page.
- **"After DLSS"** for neural rendering looks finer but costs as much as running at full resolution, whatever the DLSS quality.
- **Controls feel delayed with frame generation?** Keep Reflex on.
- **The key is taken by something else?** Change `Menu` in `BepInEx\config\community.cma.dlssnr.cfg`.
- **The log is too quiet?** Set `VerboseLog = true` in the `[DLSS]` section for the plugin's information lines (and the neural network's NGX log).

## Uninstall

Delete the folder `BepInEx\plugins\CMADlssNr` (and `version.dll` with `dlssg_to_fsr3.ini` if you installed the bridge mod).

## Credits

- BepInEx 6 (bleeding edge build 785) - https://github.com/BepInEx/BepInEx, LGPL-2.1.
- DLSS, DLSS frame generation, neural rendering and Reflex are NVIDIA's.
- The bridge mod (frame generation and the FP16 network for RTX 20 and 30) is a separate project, built on dlssg-to-fsr3.
