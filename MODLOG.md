# CMA DLSS NR — journal

Goal (user, 2026-10-07): "DLSS 5 with NR before the upscaler" for Corrupted Modeling Agency 0.65 (Unity 2022.3 HDRP, D3D11, IL2CPP, BepInEx 6), built on a ready-made native bridge to NGX (DlssBridge.dll).

## Layout
- Sources: C:\Users\Scelerosus\source\repos\CMADlssNr (Plugin.cs, Native.cs, Capture.cs).
- Installed: <game>\BepInEx\plugins\CMADlssNr\ : CMADlssNr.dll, nvngx.dll\DlssBridge.dll (native, ABI 4), ngx\nvngx_dlssnr.dll, nvngx_dlss.dll, nvngx_dlssg.dll (NVIDIA's; logs appear there: vdn.log, nvngx.log).
- Decompiled example for reference: session scratchpad reference notes.
- The game folder also has ReShade + the add-on Deep Fried Chicken, which runs nvngx_dlssnr itself: the user tests without it.

## Step 1 (0.1.0, built, NOT run in game)
Network over the finished frame at screen size. Camera.main.targetTexture = our ARGB32 sRGB texture (set in LateUpdate, taken off after the frame); RenderPipelineManager.endCameraRendering -> copy frame, _CameraMotionVectorsTexture and _CameraDepthTexture (the frame's corner of each) -> vdn_set_params -> GL.IssuePluginEvent(0x100|view) -> blit output to the screen.
Unknowns the first run answers (BepInEx\LogOutput.log, lines "Capture:" and "Neural rendering:"):
1. Does the delegate with a ScriptableRenderContext argument convert and fire (else: "did not report the end ... for three frames").
2. Does HDRP render the main camera into a targetTexture and is the frame there finished.
3. Does vdn_view_init succeed on this game's D3D11 device.
4. Dimensions/format of the two HDRP buffers (are they 2D, larger than the frame).
5. Orientation (FlipY) and motion vector signs.
6. Is the game's interface drawn by the main camera (then it is in the frame) or over the screen after it.

## Next
Step 2: proper place in the frame if step 1 shows problems (interface, other cameras). Step 3: reduced target (vdn_sr_query), NR at that extent, then DLSS SR to the screen (vdn_sr_*), jitter. Step 4: settings panel.

## 0.1.0 run by the user (2026-10-07): "no NR seen, only fps drops"
Log: delegate fires, camera 'Indoor Main Camera' renders into the target, view up at 2560x1440, hundreds of frames evaluated, last result 0x1 (success). But both HDRP buffers were refused by my copy: `_CameraDepthTexture` is Tex2DArray 2560x2160 (depth pyramid atlas), `_CameraMotionVectorsTexture` Tex2DArray 2560x1440 (none at first). So the network ran with zero depth and zero motion: likely why nothing was seen.
## 0.1.1
Array buffers: Graphics.CopyTexture(first element, corner w x h) into a same-format 2D texture, then the flipped blit. Not run in game.

## 0.1.1 run: "zero change in the picture, fps drops"
Log: depth R32_SFloat and motion R16G16_SFloat now reach the network ("array, its first"), evaluations succeed (0x1), still nothing seen. Unknown whether the output equals the input or the screen is not showing the output.
## 0.1.2 (diagnostic)
"Measure:" log line (32x18 samples read back: output vs input difference, depth range, motion range) and F10 cycling Show (0 output, 1 colour in, 2 depth in, 3 motion in, 4 split: left the frame, right the output). Not run in game.
Note: Deep Fried Chicken's log of the same game shows it sends preset=1 and autoMask=1; this plugin sends renderPreset 0 as the reference plugin does. To try if the output proves equal to the input.

## 0.1.2 run: "nothing changed" (also with F10)
Measure: output differs from input by 0.013-0.016 on average, up to 0.7 (linear); depth nonzero at all samples; motion vectors move. So the network works; its output never reached the screen.
Cause: Graphics.Blit(x, null) goes to Camera.main's targetTexture, not the backbuffer, while the main camera has one.
## 0.1.3
Camera's targetTexture taken off before the blits to the screen; logs the list of cameras once. Installed 2026-10-07, not run in game.

## 0.1.3 run: "nothing changed"
Log: one camera only ('Indoor Main Camera', to the screen). Measure still shows output != input (1.4 %), input mean 0.14 (the frame IS in our target). The user toggled F10 to depth and motion with NR on: nothing seen. So Graphics.Blit(x, null) from endCameraRendering leaves no trace on the screen in this game, and yet the user sees a normal picture (who draws it is unknown).
## 0.1.4
Composes into m_present (right way up) and shows it with a ScreenSpaceOverlay canvas (sortingOrder -32000) RawImage, besides the blit. Logs once the camera's target and RenderTexture.active at the callback. Not run in game.

## 0.1.4 run: user: "now everything works"
The overlay canvas (ScreenSpaceOverlay RawImage, sortingOrder -32000) is what shows the result; Graphics.Blit to the backbuffer from endCameraRendering does not in this game. Step 1 verified by the user.
## 0.2.0 — NR before DLSS SR (Upscale.cs)
LateUpdate: vdn_sr_open once, vdn_sr_query(screen, Quality) -> camera target of that extent. Compose: NR at the render extent -> blit output to ARGBHalf -> vdn_sr_configure(isHdr 0) / vdn_sr_set_textures / vdn_sr_set_frame(jitter 0,0) / event 0x200|slot<<16 -> ARGBHalf output (enableRandomWrite) of screen size -> present. Config [DLSS] Upscale, Quality (default 2), Model, AutoExposure; F11 cycles quality (5,2,1,0,3). Not run in game.
Known gap: no projection jitter (HDRP has no nonJitteredProjectionMatrix path like built-in; needs its own study). Frame generation (vdn_fg_*) not started: it hooks the swapchain's present and wants camera matrices; the reference plugin allows it only at DLAA.

## 0.2.0 run: "softer with every step down, fps does not rise at all"
Log: all works (render 1707x960 / 1485x835 / 1280x720 / 853x480 -> 2560x1440, DLSS presets K/M/L picked by NGX, no errors). Softness: expected without jitter. FPS: no numbers in the log of that version to judge by.
## 0.2.1
Jitter.cs: while upscaling, camera AA = TemporalAntialiasing (HDRP then jitters the projection and keeps motion vectors unjittered) and FrameSettingsField.Antialiasing (91) switched off through the camera's custom frame settings (mask + bit), so HDRP does not resolve; HDCamera.taaJitter told to DLSS as (-x, +y) (HDRP's own DLSS pass tells (-x, -y); our copies are flipped in Y). UNVERIFIED assumption: HDRP 14 keeps jittering when that frame setting is off; the log lines "Jitter:" show the values.
"Speed:" log line every 5 s: fps, render extent, DLSS mode, native GPU wait per frame (vdn_ring_wait_micros). Not run in game.

## 0.2.1 run: "blur stays, fps does not change"
Log (scene): fps with NR: native 31-32, Quality 29-36, Balanced 36-37, Performance 37-38, Ultra Performance 38-40. In the menu at Performance: 93-104. So ~25 ms a frame in the scene do not depend on pixels (CPU-bound game? unknown: no number without the mod). Native GPU waits: 0.
Jitter: HDRP does jitter with the AA pass off (assumption holds), but the scene camera's values are a tenth of a pixel at most ((-0.025, 0.017)...): hd.taaJitterScale is small on that camera. taaFrameIndex advances by 2 a frame (1,3,5,7): HDCamera is updated twice a frame (the camera rendered twice? unknown).
## 0.2.2
taaJitterScale forced to 1 while upscaling (restored after); camera materialMipBias = log2(renderW/outW) (config MipBias); "Speed:" now also with NR off (the game alone) and the main-thread ms of the plugin's own calls. Not run in game.

## User: "with Deep Fried Chicken I have 60 fps with a model in frame" (ours: 32 native, 40 at Ultra Performance)
Cause found by reasoning from three facts of the logs (blit to screen never seen yet a normal picture shown; taaFrameIndex +2 a frame; fps independent of the render extent): Unity renders cameras with a targetTexture first and screen cameras after; giving the camera's target back inside endCameraRendering made the same camera be rendered AGAIN to the screen at full size, every frame.
## 0.2.3
Target given back in OnGUI's Repaint (after all rendering), with a safety GiveBack at the top of LateUpdate; no blit to the backbuffer (the overlay canvas is the only presenter, always on). Not run in game. Expect: taaFrameIndex +1 a frame, fps rising with lower quality.

## 0.2.3 run: user: "better; the picture shakes from quality to quality"; asks for the mod's menu (game binds clash with F9-F11)
Log (scene, model): game alone 72-76 fps; NR at native 52-55; Quality ~60; Balanced 67; Ultra Performance 81. The double render is gone (fps scales). Shake: jitter now at full scale; by derivation DLSS should be told (-x, +y) of HDRP's taaJitter in our flipped copies, which is what is sent; not resolved by thought, so the window has both sign switches.
## 0.3.0
Menu.cs: IMGUI settings window (key Insert; other keys unset by default, config section [Keys]); own buttons/sliders (GUI.Button is not known to survive stripping here; DrawTexture/Label do). Not run in game.

## 0.3.0 run: user: "with jitter off it is better but still trembles; Quality is much softer than DLAA; which DLSS; add DLSS presets, remove DLSS debug items"
Hypothesis: HDRP's "no motion" mark (x = 2.0 in the motion vector buffer, measured earlier in this game) from renderers set to ForceNoMotion is read by NR and DLSS as a motion of two screens: no history there, so soft and trembling whatever the jitter.
## 0.3.1
Motion.cs: every 3 s renderers with ForceNoMotion are set to Object (+ skinnedMotionVectors), restored on stop; log "Motion:" tells how many and their names. DLSS preset choice (config Preset, window: Auto/K/L/M/E/F; context reopened on change). Window: debug items removed (jitter signs, mip bias, views stay in the config). DLSS in use: nvngx_dlss.dll 310.7.128 (from the reference plugin), NGX picked K for DLAA/Quality/Balanced, M for Performance, L for Ultra Performance. Not run in game.

## 0.3.1 run: user: "DLAA does not work (no anti-aliasing); presets L and M look broken, blur on the wall behind"
Log: every "Speed:" line says "no jitter": the user's config still had Jitter=false from the window, so all presets were judged without jitter (blur is what DLSS gives then). DLAA was "no DLSS at all" by my own design. Motion: 119 renderers were ForceNoMotion (Plane.*, build*, WindowEmisive: the room and the buildings, i.e. the wall behind) and now write their own motion.
## 0.3.2
DLAA runs DLSS at the screen's size (jittered). Jitter always on with DLSS (config key renamed to ProjectionJitter so the saved "false" is dropped; removed from the window). Not run in game.

## 0.3.2 run (no complaint about shake reported); user asks: transformers only, English only, DLSS in a menu of its own and usable without NR, optimise, then frame generation
Log of that run: Performance 155 fps, Quality 138 (light scene); DLAA with NR 50-57; Quality with NR 50-63 in the heavy scene.
## 0.4.0
- NR and DLSS independent (Active = either). Compose: Buffers() (motion, depth) -> NR if on -> DLSS if on (fed the NR output, or the frame itself with a flipped blit) -> shown. m_nrFailed separate from m_failed.
- One full-screen copy fewer: the overlay RawImage shows the final texture directly (uvRect flipped), no m_present unless a diagnostic view is on. Motion scan: FindObjectsOfType every 5 s.
- Window in English, three pages: DLSS (on/off, quality, preset Auto/K/L/M), Neural Rendering, Frame Generation.
- FrameGen.cs: vdn_fg_open / set_textures (the unflipped slices) / set_frame (matrices column by column as Unity has them, GetGPUProjectionMatrix(cam.projectionMatrix, true)) / pacing / multiplier / enable. Off by default. NOT run in game; the reference plugin allowed it only at DLAA.
Backup of 0.3.2 sources: ..\CMADlssNr_bak_032.

## 0.4.0 run: frame generation WORKS (user's report + log: generating x2, 1129 of 2260 shown frames made, 59 rendered a second; driver advertises a ceiling of 3 with nvngx_dlssg 310.2.1). User: the pointer vanishes while generating; wants dynamic generation (RTX 50); feels input delay, asks about Reflex.
Log also: "the plugin's own calls took 9.9 ms a frame of the main thread" with FG on (1 ms without): GetNativeTexturePtr() makes the main thread wait for the render thread, which FG holds back for pacing.
## 0.4.1
- Latency.cs: native pointers cached per texture (Ptr/Forget); Reflex through nvapi64.dll (nvapi_QueryInterface ids 0x0150E828 Initialize, 0xAC1CA9E0 D3D_SetSleepMode, 0x852CD1D2 D3D_Sleep, from memory, UNVERIFIED; device from ID3D11DeviceChild::GetDevice of the white texture; Sleep called at the top of Update); software pointer drawn in OnGUI while generating.
- Dynamic multiplier (config FrameGen/Multiplier 0): ceil(target / rendered fps) within 2..ceiling, target = refresh rate or FrameGen/DynamicTarget, 1 s hysteresis. The native half has no dynamic mode of its own; x4 and more would need a newer nvngx_dlssg.dll.
Not run in game.

## 0.4.2
User: replaced nvngx_dlss.dll and nvngx_dlssg.dll in ngx\ with 310.9.1; asked to "rewrite for 310.9.1" and to remove the old name everywhere. The native half is a binary without source here: it cannot be rewritten, only renamed (DlssBridge.dll; nothing in it looks its own file name up) and its strings left as they are. Managed side: names and comments cleaned; the frame generation multipliers offered are whatever the driver allows (2..6). Not run in game with the new NVIDIA files yet.

## 0.4.2 run: crash ("DLAA with generation on crashes on preset L")
Windows event log 20:57:44-45: access violation 0xc0000005 in _nvngx.dll (driver 32.0.16.1714), the same second NGX's log shows the DLSS feature being created with preset L after the context was shut down and opened again (main thread) while frame generation was running (render thread, present hook). The same change to M a moment earlier survived; L at DLAA without generation had worked in 0.4.0. So: a race between remaking DLSS's NGX context and the generator, not the preset itself (inferred, not proven).
Also from that run: nvngx_dlssg 310.9.1 opens and generates with the native half as it is; the driver advertises a ceiling of 5.
## 0.4.3
Preset change with generation on: generation is stopped, 8 frames pass, the context is remade (vdn_sr_open now inside vdn_rebuild_begin/end), generation resumes after 30 frames. Generation also stands back 30 frames whenever the network's or DLSS's features are remade (size, quality). Not run in game.

## 0.4.3 run: crash again, only on L (user: "L crashes, the others do not")
Event log 21:02:46: the same access violation at the same offset (0xa35c0) in _nvngx.dll, the second the DLSS feature is created with preset L at DLAA; K and M were remade at DLAA in the same run with generation on, without trouble. So it is preset L at DLAA with nvngx_dlss 310.9.1 (it had worked with 310.7.128 and no generation), not the race guessed in 0.4.3 (that guard stays: harmless).
## 0.4.4
PresetFor: L at DLAA -> K; a line in the window says so. Not run in game. Unknown: whether L crashes at other qualities with 310.9.1 (the user tried DLAA).

## 0.4.5
User asks: can DLSS's auto exposure sharpen Quality, or add Ultra Quality; wants good fps with good quality. Auto exposure is already on (vdn_sr_configure autoExposure 1) and does nothing for sharpness here: DLSS is handed a tone-mapped frame (isHdr 0). Added Ultra Quality: 77 % each way (config DLSS/UltraQualityScale), run as DLSS's Quality mode with a larger extent, clamped to the mode's max from vdn_sr_query. Not run in game; unknown whether the native half accepts an extent other than the mode's optimal one (log line "DLSS: Ultra Quality:" and any "refused"/"failed").

## 0.5.0
- Sharpen.cs: sharpness slider. Unsharp mask without a shader of our own: frame -> RGB111110Float copy (alpha reads 1) -> half-size copy -> back up into ARGBHalf (the blur) -> Graphics.DrawTexture of the frame over it with colour (0.5,0.5,0.5,(1+k)/2): the interface's texture shader doubles its colour, so alpha is 1+k, unclamped in a float target: frame*(1+k) - blur*k. A four-pixel self-test (expects 1.25 / -0.25, detects a vertical flip) decides whether it is used; log "Sharpening: tried on four pixels".
- Frame limit by the driver (Reflex minimumIntervalUs), window: Off/60/72/90/120/144.
Not run in game. Everything here rests on assumptions the self-test and the log check.

## 0.6.0 (user asked: several passes, motion vectors, a mask for the network)
- Passes 1/2/3 in the window (the view is initialised anew on change).
- Motion vectors: Jitter.cs now sets the camera whenever the plugin runs: FrameSettingsField.MotionVectors (10) and ObjectMotionVectors (11) forced on through the camera's custom frame settings (config Motion/CameraMotionVectors); jitter/TAA only with DLSS as before; only the touched bits are restored. Window: both motion switches, and a View choice (Final, Motion (multiplied by 12 through Hidden/BlitCopy's _Color), Depth, Mask, Split).
- Mask.cs: control mask for the feature. All SkinnedMeshRenderers, classified by material/renderer name into Skin / Hair / Clothing, drawn by a CommandBuffer (DrawRenderer with 'Unlit/Color' or 'Hidden/Internal-Colored', SetViewProjectionMatrices of the main camera) into ARGB32 linear + 16-bit depth; scene colour from a 1x1 texture blit; softened by a 1/6 downsample; flipped like the other inputs. RGB = share of intensity / tone / structure against the largest region value (which is what vdn_set_params gets), A = 1 body, 0.5 clothing, 0 scene. vdn_view_init wantMask 1, vdn_set_textures mask, vdn_set_params controlMask 1. Window page "Mask": per region Effect (0..1) and Detail (0..2).
All of it NOT run in game. Unknowns: whether the plain shader exists and draws skinned meshes right in this HDRP build (see "View: Mask"), the name-based classification (log line "Mask: drawn with"), no occlusion by the scene.
Backup of 0.5.0 sources: ..\CMADlssNr_bak_050.

## 0.6.0 run: user: "the mask works very crookedly; sharpening does not work; frame generation is smooth only at x2"
Log: Reflex found and on (ids were right); camera settings applied; mask drawn with 'Hidden/Internal-Colored' (no 'Unlit/Color' in the build), 56 parts: 10 skin, 1 hair, 45 clothing (props such as UltraSonido.*, Enema * are skinned meshes and fall into Clothing); what "crooked" looks like is unknown. Sharpening: the self-test threw ObjectCollectedException ("Object was garbage collected in IL2CPP domain") and was given up for good. Frame generation: monitor 280 Hz; my dynamic mode ran x3 -> x5 -> x6 while rendered fps fell 61 -> 42 -> 33 (a spiral); fixed x3 showed 93 rendered a second; the driver's ceiling 5 comes out of the native half as 6.
## 0.6.1
- Sharpening: self-test retried (up to 8 times) when an object was collected; per-frame failures are transient (given up after 30 in a row).
- Mask: softening by 1/3 instead of 1/6. "Save diagnostic pictures" button (Diag.cs): frame, shown, mask as drawn, mask handed over, depth, motion written as PNG to plugins\CMADlssNr\diag: to be READ next time instead of guessing.
- Dynamic generation: one step per 2 s, a step up that costs over a fifth of the rendered fps is taken back for 30 s, never above x4.
Not run in game.

## 0.6.1 run: pictures saved by the user (diag\), READ:
- 3_mask_as_drawn: the body is in the right place, but in patches of black / green / yellow on a white scene: Hidden/Internal-Colored multiplies by the mesh's own vertex colours. Hair and the dress are not in it (hair is not a skinned mesh here).
- 6_motion: the model and the hero's arms have motion vectors, the still background has none: fine.
- Sharpening: the four-pixel self-test threw ObjectCollectedException all 8 times: deterministic, from Graphics.DrawTexture or the read-back; never got to run.
- vdn_fg.log: "pacing ON at 30 rendered/s", "pacing OFF (below the floor at 18..29 rendered/s, floor is 30): frames go out back to back", "multi-frame count 1 -> 3; rebuilding the feature, since the count is fixed at creation": the user's scene renders ~30-50 a second, and every step up costs rendered frames, so above x2 pacing switches off: that is the "only x2 is smooth".
- User: double pointer; wants it fixed without a menu item.
## 0.6.2
- Mask redrawn with blending only (never the shader's own colour): depth of all parts (blend Zero/One, ZWrite), per region parts again with Zero/Zero (alpha 0 marks the region) + a full rectangle blended (OneMinusDstAlpha, DstAlpha) in the region's colour, clothing last with alpha 0.5, then a rectangle at the far depth for the scene (alpha 0). Culling mask of the camera respected; plain MeshRenderers named like hair join as Hair. 24-bit depth.
- Sharpening through a CommandBuffer.DrawMesh of a rectangle with a copy of Canvas.GetDefaultCanvasMaterial(), _Color alpha = 1 + k, over the blur in ARGBHalf. No self-test; orientation assumed the same as Blit (check 2_shown.png).
- Pointer: Cursor.SetCursor(arrow, ForceSoftware) while generation is on, back to Auto after; own drawn pointer and its menu item removed.
- Dynamic: 5 s between steps, no step up under 45 rendered a second, a step down under 34.
Not run in game.

## 0.6.2 run: user: sharpening works now (CommandBuffer + the interface's material: VERIFIED by the user). The pointer vanishes exactly when Frame Generation is switched On or Off; wants no pointer of the plugin's. Generation still good only at x2.
vdn_fg.log: with pacing on the rendered rate settles at exactly 30 ("pacing ON at 30 rendered/s: spreading 6 frames across 33.1 ms", then "pacing OFF (below the floor ...)", and round again); at x2 97 -> 51 rendered. Reading: the native half holds the made frames inside the game's own present call on the render thread, where this plugin's network and DLSS also wait for the GPU, so the hold adds to the frame time, the measured rate falls, the hold grows: down to the floor of 30, where pacing switches off and on. Stable only at x2. Not fixable from the managed side.
## 0.6.3
Pointer: nothing drawn or set by the plugin any more (software pointer of 0.6.2 undone once). Instead, five times a second, while Cursor.visible, not locked and focused: user32 GetCursorInfo; if Windows has it hidden, ShowCursor(true) until the count is at zero or above; if shown without a picture, the arrow. Log line "Pointer:" tells what was found. Not run in game.

## 0.6.3 run: user: the pointer still vanishes; remove the generation options that are broken, keep x2.
Log: no "Pointer:" line at all: Windows reported the pointer as shown with a picture while the user saw none. So not the show count.
## 0.6.4
- Frame generation: x2 only (multiplier choice and dynamic mode removed from the window and from the code path; config keys left unused).
- Pointer: for 3 s after generation is switched or its generator starts/stops: ShowCursor(false) + ShowCursor(true) to >= 0, SetCursor(arrow), SetCursorPos(same place), five times a second; one log line with what Windows and Unity say at that moment (flags, picture, visible, lock, focus). Third attempt, cause still unknown. Not run in game.

## 0.6.4 run: user: "now it is always on the screen; the pointer comes back after opening the game's own menus, but in your mod's window it is not seen when generation is switched"
Log "Pointer: generation switched ...": every time "the game has it hidden, Confined". FACT: this game keeps the system pointer hidden (Cursor.visible false, Confined) and draws its own pointer in its interface. The plugin's IMGUI window is drawn over that interface and covers it. Everything before was aimed at the wrong pointer; the forced ShowCursor of 0.6.4 left the system pointer on for good.
## 0.6.5
Unity's Cursor.visible is no longer touched (only a Locked pointer is unlocked while the window is open). The system pointer is asked for from Windows (ShowCursor to >= 0, arrow) only while the mouse is over the plugin's window, and sent back (count to -1) when it leaves or the window closes. No forced kicks. Not run in game.

## 0.6.6
User: halos round the model, present with neural rendering off (so: DLSS or the camera settings the plugin makes; sharpness 0 and mask off in the config). No pictures of it yet. Diagnostics now also save themselves twice a session (30 s and 90 s after frames begin), frame and shown at full size, with the settings in the log line. Nothing else changed: cause unknown, to be read from the pictures. Suspects: alpha-tested hair shimmering under jitter and accumulated by DLSS into a haze; an HDRP effect that expects TAA to resolve the jitter.

## 0.6.6 run: pictures of 2026-10-08 18:39 READ (Ultra Quality, NR on, jitter on, sharpness 0)
- 2_shown, left hand: a pale haze beside the wrist and the sunlit edge of the hand, some 10 px wide: that is the user's "halo on the palms".
- 1_frame (the game's frame before the network and DLSS): the sunlit skin edge already has a thin bright rim, and there is a thin reddish line along the edges of the arm and of the dress. So the fringe starts in the game's frame; the network / DLSS widen it. Not known whether the rim is the game's own look or comes with the jitter and the anti-aliasing pass being off.
- 6_motion: body, arms, hands, hair all have motion vectors (cloth has a patchy pattern). Not a missing-vectors problem.
- Motion vector and jitter signs re-derived on paper: consistent with what is sent. Not the cause by reasoning (unproven).
## 0.6.7
Diagnostics take two more sets by themselves after the first: 60 frames with the projection not jittered (7_frame_no_jitter, 8_shown_no_jitter), then 60 frames with the network off (9_shown_network_off); both changes undone after. No fix yet: the cause is to be read off these.

## 0.6.7 run: three sets of pictures READ (notes\halo_*_2026-10-08.png: frame / frame no jitter / shown / shown no jitter / shown network off)
The halo round the hands is in the GAME'S OWN FRAME, with and without jitter alike: a soft reddish-brown glow some 6-8 px wide outside the silhouette of sunlit skin, on the dark floor. Not DLSS, not the network, not the jitter: they only carry it on (the network turns it into a darker outline). Most likely the game's bloom; possibly its subsurface scattering.
## 0.6.8
Camera settings block also switches off, per setting, the game's own effects through the camera's frame settings: Bloom 84, SubsurfaceScattering 46, DepthOfField 81, MotionBlur 82, ChromaticAberration 86, Vignette 87, FilmGrain 89 (config [GameEffects], all left on by default; window page "Game"). Built, NOT installed (game was running), not run.

## 0.6.8 run (2026-10-08 19:01): user: "Bloom removed the halo"; pictures READ with every game effect switched off
- Log confirms the frame settings took: "Of the game's effects switched off: bloom subsurface scattering depth of field motion blur chromatic aberration vignette film grain". Edges of the hands are clean in 1_frame and 2_shown. VERIFIED: the halo round sunlit skin was the game's Bloom; switching effects off through the camera's custom frame settings works.
- Seen in passing: the dress has a blocky mosaic in the game's own frame (its texture); the network makes it darker and more contrasty (2_shown), DLSS alone leaves it mild (9_shown_network_off). The remedy is the mask: Clothing detail down.
- Motion vectors of the dress are a patchy grid (cloth simulation), of body and hands smooth.

## 0.6.8 run with the mask on (19:04): pictures READ
Log: mask on, 86 parts (28 skin, 9 hair, 49 clothing); names look right (Slit Minidress, stockings, shoes, bracelet = Clothing; Nails, Skin_Head = Skin; PubicHair = Hair; the hero's suit = Clothing). 3_mask_as_drawn is plain white: every region is set to 1, so RGB is (1,1,1) everywhere and the regions differ only in alpha, which the RGB picture does not show. So the redrawn mask can be neither confirmed nor faulted from it. 2_shown looks sound (nothing broken by the mask).
## 0.6.9
Diagnostics draw the mask once more in telling colours for the picture (3_mask_regions_...: scene grey, skin red, hair green, clothing blue). Built, NOT installed (game running).

## 0.6.9 run (19:21): pictures READ: the mask is RIGHT
3_mask_regions: scene grey, skin red (arms, shoulders, legs), dress and its straps and the bracelet blue, silhouettes on the model exactly. With Clothing detail 0.3 the dress in 2_shown is smooth, the mosaic of the fabric no longer amplified. VERIFIED: the blend-only drawing of the mask and the per-region control work. Open: the long hair over her shoulders is not in the mask (no green) at this moment's scan: its meshes are not found by name.
## 0.6.10
Mask: hair words widened (Spanish names of this game: pelo, cabello, ceja, pestaña; polytail, ponytail, braid...), plain meshes taken as hair when anything up to five parents above is named as hair; the list of parts is logged again whenever its count changes (up to 6 times), since the first look happens before she is dressed. Built, NOT installed (game running).

## 0.6.11
User (screenshot of the main menu): "the interface is processed by DLSS". The main menu is drawn by the main camera (a panel in the 3D scene), so it is in the frame: lettering soft and smeared. No way to take it out of the frame without rendering the interface separately. Done instead: the plugin stands by (does nothing, camera renders to the screen) while no enabled SkinnedMeshRenderer exists in the scene, looked at once a second, two looks in a row to change (config General/StandByWithoutCharacters, window page Game). The network's own interface hint (uiCorrection) on by default under a new key (General/InterfaceCorrection). Built, NOT installed (game running), not run. Unknown: whether the menu scene really has no skinned mesh; in-game interface drawn by the camera is still in the frame.

## Plant and pot (user: "can the sharpness of objects be improved?"), pictures of 19:30 READ (notes\plant_2026-10-08.png: frame / frame no jitter / shown / shown network off; Ultra Quality)
- Game frame at 77 %: thin leaves broken into dots (alpha-tested, thinner than a pixel); the pot's white stripes carry a checker stipple (the game's own dithering of something: also seen on hair ends).
- DLSS only: leaves and pot edges come out with a regular saw-tooth / comb; stipple stays. That comb is what a wrongly told jitter looks like: the signs sent were only reasoned, never checked.
- With the network: smooth, but soft; the brick wall loses texture.
- Config found: Sharpness 0, MipBias false (the user had switched it off while it was in the window; no switch there now).
## 0.6.12
Diagnostics go on, network off, through the four pairs of jitter signs (90 frames each after a history reset): j1..j4_dlss_only_jitter_x(plus|minus)_y(plus|minus).png. Mip bias under a new key (DLSS/SharperTextures, on). Built, NOT installed (game running). Also still not installed: 0.6.11 (stand-by in menus).

## 0.6.12 run (19:36): the four pairs of jitter signs READ (notes\jitter_signs_*_2026-10-08.png), DLSS only, Ultra Quality, still scene
A door's corner at 6x: only "x plus, y minus" (HDRP's taaJitter.x as it is, .y negated) gives a smooth sloping edge and a crisp upright one; the other three leave stair-steps; the plant's thin leaves are clean in the same one. So the right signs are the OPPOSITE of what I had reasoned on both axes (I sent -x, +y; the user's config had -x, -y). Every run until now had DLSS told a wrong jitter: the softness on Quality, the comb on thin things.
## 0.6.13
Jitter signs under new keys with the found values: DLSS/JitterNegateX false, DLSS/JitterNegateY true (frame generation's are derived from them as before). Built, NOT installed (game running). Motion vector signs were never tried the same way (needs a moving scene): open.

## 0.6.13 run (19:39): pictures READ: the jitter fix is CONFIRMED
Log: "DLSS is told (0.250, 0.389)" for HDRP's (0.250, -0.389): x as it is, y negated, in use. DLSS only (9_shown_network_off): the plant's thin leaves are whole and smooth, no comb; the other sign pairs in the same run (j1, j2, j4) still ragged, j3 (the same pair as in use) clean. With the network (2_shown) the plant is clean and sharper than before. notes\plant_after_jitter_fix_2026-10-08.png.
Left: the checker stipple on the pot's white stripes (the game's own dithered transparency), untouched by DLSS.

## 0.6.14 (user: check the motion vectors' signs the same way; a good default sharpness; DLSS flashes on for a few seconds at start, "just leave it on")
- Diagnostics sweep goes on after the jitter pairs: the plugin swings the main camera itself (1.5 deg, 1 Hz, yaw and 0.6 of it in pitch; applied in beginCameraRendering, put back in endCameraRendering) and gives each of the four pairs of motion vector signs 120 frames from a fresh history, DLSS only, taking the picture as the swing passes through the middle: m1..m4_dlss_only_moving_x(plus|minus)_y(plus|minus).png. In use: x minus, y plus (reasoned only).
- Sharpness default 0.3 (new key DLSS/Sharpen, so the saved 0 is dropped).
- Stand-by in menus off by default (new key General/StandByInMenus): it made the plugin run for the first seconds and then stop, which the user disliked; the price is the main menu's lettering going through DLSS again.
Built, NOT installed (game running).

## 0.6.14 run (19:48): the four pairs of motion vector signs READ (notes\motion_signs_*_2026-10-08.png)
The swing works (the views differ from the still one; the game does not undo it). Plant: x plus (m1, m3) clearly ghosted, x minus (m2, m4) much cleaner: X negated is right, as in use. Y: m2 (y plus, in use) and m4 (y minus) hard to tell apart. BUT in both of them the light switch's three buttons, sharp in the still picture, are smeared into a grid of repeated edges, and a thin highlight on the door is repeated several times: with neither pair is the moving picture sharp. So besides the sign something else is off: the size of the vectors, or what they are measured against.
## 0.6.15
At each moving capture a log line "Motion numbers:" gives the camera's turn since the last frame as a part of the frame, and the median of the pipeline's motion vectors in the middle of the frame, with their ratio. Built, NOT installed (game running).

## 0.6.15 run: "Motion numbers" READ
Eight lines (two presses). Camera turned right and down; stored vectors x negative, y positive, i.e. current minus previous with y up, as assumed. But the ratio stored / expected is not 1: 1.81, 1.75, 1.63, 1.69, then 1.35, 1.42, 1.44, 1.02, x and y always alike within 5 %. So a common factor between 1 and 2 that changes from capture to capture: either my estimate of the turn is off (the estimate is the weak side: it is my own arithmetic on the swing), or the vectors do not describe one frame's motion (e.g. measured against a frame other than the last one shown). Not settled by this.
## 0.6.16
Ground truth instead of arithmetic: after the four moving sets, 18 frames with the swing at 6 deg and no jitter; each frame the middle 160x160 of the game's frame is read back and matched against the frame before at every shift up to 14 px (sum of absolute differences): log lines "Motion truth: the picture moved dx, dy px ...; the motion vectors there hold ... px; the camera was turned ...". Built, NOT installed (game running).

## 0.6.16 run: "Motion truth" READ: the swing did not move the picture
18 lines: picture moved 0 px, vectors exactly 0, while the swing's own numbers ran (0.3 to 0.7 deg a frame). So turning the camera in beginCameraRendering does not reach that frame's render (HDRP has taken the camera's transform by then). The earlier "moving" pictures and "Motion numbers" were therefore not what they were taken for: whatever small motion they showed (vectors 0.0005..0.0009, the smeared switch) did not come from the swing as computed. The X-sign reading from them is unsafe again.
## 0.6.17
Swing applied in Application.onBeforeRender instead (after all scripts, before the pipeline reads the camera), only in the frame the plugin has set the camera up; put back as before. Built, NOT installed (game running).

## 0.6.17 run: user: "DLSS stopped working altogether"
Log: "Capture: failed before the frame (NotSupportedException: Method unstripping failed)" every time: Application.add_onBeforeRender is stripped from this build, and the call sat in the subscription block of LateUpdate, so the whole plugin switched itself off. My fault: an untried engine call put on the main path without a guard of its own.
## 0.6.18
That call removed. The swing hangs on the game's own GlobalUpdater (stage lateUpdateAfterCameraController), subscribed only while a diagnostic swing is wanted and inside its own try/catch; taken off the camera at endCameraRendering, with a safety at the top of Update (not LateUpdate: the game's stage may come before the plugin's LateUpdate). Installed, not run.
Lesson: anything new on the main path gets its own try/catch and must not be able to take capture down.

## 0.6.18 run (20:08): the motion vectors are RIGHT (measured and seen)
- DLSS works again. The swing hung on GlobalUpdater's last stage does move the picture.
- "Motion truth", 17 frames: picture moved (+11, -6) px by block matching; vectors x size = (+10.74, -6.44) px; every frame agrees to within half a pixel. So HDRP's motion vectors are current minus previous, as parts of the frame, x right and y up, size 1: exactly what was assumed.
- Four sign pairs with the real swing (notes\motion_signs_switch_real_swing_2026-10-08.png): only "x minus, y plus" (the pair in use) keeps the light switch's three buttons sharp in motion; the other three smear them. Nothing to change.
- The earlier "smeared with either sign" came from pictures taken while the swing was not reaching the render (0.6.14-0.6.16).

## 0.6.19 (built, NOT installed: game running; not run in game): hair
- Pictures of 20:12 (Ultra Quality, NR on, mask on): the game's hair is stippled (dithered) in its own frame; DLSS alone resolves it into smooth strands; with NR before it the ends over the shoulder keep a dotted pattern. In the mask only the scalp cap was marked: the long hair ("Hair (Instance)", renderer "Render") shows nothing with the plain shader.
- The hair names also caught the office chairs ("chair" holds "hair"). Now excluded by a case-sensitive "chair|Chair|CHAIR" ("PubicHair" must stay hair).
- Hair parts are now also drawn with their own material, by its depth pass (DepthForwardOnly / DepthOnly / TransparentDepthPrepass / ShadowCaster, first found; logged "Mask: hair's own material for its depth"), then the hair colour is laid wherever depth is nearer than the far end (test Greater), before the other parts. FindPass is guarded by itself.
- To check in game: mask picture with long hair in frame; log lines above; then Hair effect / detail lowered so NR leaves the stipple to DLSS.

## 0.6.19 run (20:19): the long hair IS in the mask now
- Log: "Hair (Instance): 'HDRP/Hair.14.0.7.Lit' pass DepthForwardOnly not there; DepthOnly found". Mask picture: the whole of the long hair green, strands over the arm too; the chairs are scene again.
- Hair effect / detail were both 1, so the look itself is unchanged by this. Crops (scratch k_ends, k_zoom): game frame stippled at the ends; NR+DLSS and DLSS alone both leave some dots at the ends over the shoulder; NR's strands are a little fuller and smoother, not worse.
- Left over: small medical tools on the desk are marked Clothing (skinned meshes); harmless so far.

## 0.6.20 (installed, not run in game)
- Logs once per hair material its shader's keywords and properties with values ("Hair material '...'"), to find what makes the stipple (alpha clip / dither) and whether it can be turned down.

## 0.6.20 run: what the long hair really is
- 'Hair (Instance)', shader 'HDRP/Hair.14.0.7.Lit', queue 2225: OPAQUE, _AlphaCutoffEnable 0, no dither. Strands are real geometry: _StandWidth 0.001, _TavoDefaultStandWidth 0.001, _WidthScale 0.22, _DensityMul 4, _DetailMul 9.2, _AddPrecomputedVelocity 1. So the "stipple" is sub-pixel-thin strands, not dithering. (Scalp, brows, lashes, pubic hair are ordinary HDRP/Lit transparent alpha-tested.)

## 0.6.21 (built; not run in game)
- Hair.cs: Game page sliders "Hair: strand width / width scale / density" multiply _StandWidth+_TavoDefaultStandWidth, _WidthScale, _DensityMul on materials whose shader starts with "HDRP/Hair"; originals restored at 1/1/1 and on teardown. Which of the three really widens a strand is NOT known yet: to be seen in game.

## 0.6.22 (installed if "installed" below; not run in game): hair in motion
- User: strand sliders not wanted (removed, 0.6.21 Hair.cs gone); the real fault is "in motion the hair gets artifacts as if gloss appears".
- Pictures 20:25/20:26 (DLAA, swing): m1..m4 all four sign pairs leave the long hair flat, strands gone, highlight spread into a glossy patch, while with the pair in use (m2) wall and face are sharp. So the hair's own motion vectors are wrong whatever the sign (scratch n_lock.png, n_m4.png).
- New Hair.cs: Motion/HairMotionVectors 0 game's, 1 without _ADD_PRECOMPUTED_VELOCITY, 2 (default) renderer set to MotionVectorGenerationMode.Camera. Game page: "Long hair's motion vectors". Logs "Hair: ... its motion vectors were ...".
- Open: was the hair ForceNoMotion before Motion.cs turned it to Object? The log line shows the mode after Motion.cs.

## 0.6.22 run (20:31, Ultra Quality, mask off): Camera-only hair vectors
- User: stability better; but "noise on her chest". Log: hair renderer 'Render' was Object with precomputed velocity on.
- Pictures (scratch p_chest2.png): while SHE moves, the lock lying over her breast is a smeared blob with sparkle, in NR+DLSS and in DLSS alone: camera-only vectors say the hair stands still while it rides on the body.
- 0.6.23: default now 1 (Object motion without _ADD_PRECOMPUTED_VELOCITY), config key renamed Motion/HairMotion so the new default takes. Not known yet whether the shader's object motion without the precomputed part is right; no per-pixel "trust the new frame" mask is wired in the native half (vdn_sr_set_textures has none).

## 0.6.23 run: user: "Camera only looks best, but there is room to grow"
- So the shader's object motion is wrong even without the precomputed velocity. Default back to 2.

## 0.6.24 (built; not run in game): hair "Follow body" (Motion/HairMotion = 3), experimental
- Hair.cs HairFollow, on the unflipped slice of the pipeline's motion vectors: (a) copy, bodies/clothes (skinned, queue < 2500) drawn over it by their own materials' "MotionVectors" pass with an own depth; (b) second copy, hair's own DepthOnly into its depth, then (a) copied over wherever hair is by the UI default material with unity_GUIZTestMode 5 (Greater) on the far rectangle; (b) flipped into m_mvec.
- NOT KNOWN: whether DrawRenderer with the MotionVectors pass gets the previous frame's skinned positions and matrices outside the pipeline; whether the UI shader honours unity_GUIZTestMode set on the material and keeps negative values. Check picture 6_motion (hair over the body should take the body's colour) and the log line "Hair: follows the body".

## 0.6.24 run (20:45, DLAA, Follow body): did NOT work
- Log: "follows the body: 1 part(s) of hair, 53 part(s) of bodies..."; no failure. Picture 6_motion (scratch q_mv.png): saturated red patches (x = HDRP's "no motion" mark, 2.0) exactly where hair lies on the body. So cmd.DrawRenderer with a material's MotionVectors pass gets no per-object motion data (unity_MotionVectorsParams 0 -> forceNoMotion). The UI material with unity_GUIZTestMode 5 DID copy only at hair pixels: that half works.
## 0.6.25 (installed if "installed" below; not run in game)
- Bodies' motion now drawn with the pipeline's own means: cam.TryGetCullingParameters, context.Cull, context.CreateRendererList(RendererListDesc "MotionVectors", opaque, PerObjectData.MotionVectors), cmd.DrawRendererList, context.ExecuteCommandBuffer + Submit, with the context of endCameraRendering. None of these calls tried in this game before; all inside HairFollow's guard. Mode 3 is reset to 2 at every start (a native crash there must not lock the game out).

## 0.6.25 run (20:52, DLAA, Follow body)
- No failure in the log, 0.58 ms a frame for the plugin's calls (was 0.25). Picture 6_motion: no red "no motion" patches any more; hair and body one colour. Whether it LOOKS better is the user's to say (not said yet).
## 0.6.26 (installed if "installed" below; not run in game)
- User: (1) NR after DLSS as an option; (2) no diagnostics by themselves; (3) Mask's Hair changes her forehead, not her hair.
- General/NeuralAfterDlss (Neural page "After DLSS, at full size"): DLSS on the game's frame first, the network at output size over it, with motion vectors/depth blitted up (m_mvecBig/m_depthBig), mask built at output size. Run() now takes its source and buffers.
- Auto diagnostics at 30/90 s removed: only the button.
- Mask: hair-named skinned parts with a transparent material (queue >= 2500: scalp cap, brows, lashes, pubic) are Skin now; Hair = strand hair and plain hair meshes.

## 0.7.0 (menu made ready for use; not run in game)
- User: keep Camera for the hair; no diagnostic items, no mention of motion vectors in the menu.
- Removed from the window: hair motion choice (forced 2 = Camera at start; Follow body code left unreachable), "Motion vectors" checks, View choice, Save diagnostic pictures button. At start Show=0, ZeroMotion=false, ObjectMotion=true, CameraMotion=true are forced. Texts shortened. Diagnostics code stays in Diag.cs, with nothing calling DiagAsk.

## 0.7.1 (not run in game)
- "Pause in menus" removed from the window and forced off at start. Defaults: DLSS Quality 5 (DLAA), Preset 13 (M).

## 0.8.0 (release clean-up; installed; NOT run in game)
- Backup of 0.7.1 sources: ..\CMADlssNr_bak_071.
- Removed: Diag.cs (pictures, sweeps, camera swing on GlobalUpdater, Motion truth), view modes (Show), Amplifier/Sample/Measure, CamerasOnce, stand-by, frame rate limit, hair modes other than Camera and HairFollow, HairTell, MaskPaintRegions, "off" speed log. Config entries gone: Diagnostics/*, Keys/Show, DLSS/Model, DLSS/ObjectMotion, FrameGen/Multiplier, FrameGen/DynamicTarget, Latency/FpsLimit, Motion/*, General/StandByInMenus (old lines stay in the user's cfg as orphans, harmless).
- Fixed: Teardown skipped when the camera's settings were set without jitter (tested m_jitOn, now m_camOn) or only hair was changed; hair/no-motion renderers restored in the right order when switched off; VerboseLog description had "\n" in the path; Fail message spoke of a key.
- "Speed:" log lines limited to 6 after each (re)start of the network; mask part list logged twice at most.
- README.md written. 4048 -> 2929 lines.

## 0.8.0 run: preset L at Quality crashes too
- With FG on, NR on, Quality: switched M -> L; NGX log ends at "(Quality) Using App hint Preset L" after the context was made anew; game gone. Same as at DLAA. Otherwise 0.8.0 ran (DLAA and Quality with M, FG generating).
## 0.8.1 (not run in game)
- Preset L removed: menu Auto/K/M; PresetFor never returns 12 (a saved 12 runs K; Auto = K, M for Performance and Ultra Performance).

## 0.8.2 (not run in game)
- Presets E (5) and F (6), the convolutional models, back in the menu for RTX 20/30 (Auto/K/M/E/F). Not known whether DLSS 310.9.1 still honours them (NGX log line "Using App hint Preset ..." tells). Archive release\CMADlssNr-0.8.2.zip.

## 0.8.3 (not run in game)
- User asked for a quality control of NR. The native half has NGX parameter DLSSNR.Hint.Render.Preset (vdn_set_params renderPreset, always 0 so far) and DLSSNR.ScalingRatio (no entry in the API). Neural page: "Network preset (experimental)" Default/1/2/3/4 -> General/NetworkPreset, view made anew on change. What 1..4 are is NOT known. Crash guard: General/NetworkPresetTrying is saved before the view is made and cleared after 300 frames; found set at start -> preset back to 0.
- Multi frame generation: asked to think it over, nothing changed (see the reply).

## 2026-10-09: own native library decided
- User: "write your own native library". MSVC is already installed (VS 18 Community, 14.51; test DLL built and its export read with dumpbin). Borrowed bridge: 80 vdn_* exports (48 imported by the plugin), calls _nvngx.dll NVSDK_NGX_D3D11_*/D3D12_* directly, own D3D12 device. Nothing written yet: waiting for the go-ahead to fetch NVIDIA NGX headers.

## 0.9.0: own native library, stage 1 (DLSS on D3D11) - built, tested outside the game, installed; NOT run in game
- SDK: native\ngx_sdk = include\ + lib\nvsdk_ngx_s.lib of github.com/NVIDIA/DLSS commit 3749594 (310.9.1). (My slip: `git ls-tree -l` on the blobless clone pulled ~255 MB of the repo's objects, its DLLs among them, into .git; .git was deleted at once, nothing of it kept or used.)
- native\src\bridge.cpp -> native\out\CmaBridge.dll (native\build.cmd; /MT, depends on d3d11/dxgi/kernel only). Same vdn_* names, ABI 4, 49 exports. DLSS through NVSDK_NGX_D3D11_* on the game's own device: Init_with_ProjectID on the main thread, CreateFeature/Evaluate in the render event on the render thread, with a device context state of its own swapped in round NGX (SwapDeviceContextState) so Unity's state is untouched, SEH guard round create/evaluate, textures AddRef'd. NR and FG entries are stubs (view_open -1, fg_open -1).
- native\test\test.cpp (out\test.exe <folder with nvngx_dlss.dll> [quality] [preset]): own D3D11 device, same calls as the plugin. On this machine (RTX 5080): Quality/DLAA/Performance/Ultra Performance with presets M, K, E, F and L all make the feature and run, colour comes out the other end. NGX's log: "Using App hint Preset F" (so E/F are still honoured by 310.9.1) and "Preset L" WITHOUT a crash at Quality, DLAA and Ultra Performance: the L crash belongs to the borrowed bridge's path, not to the driver as such.
- NGX's feature list in its log runs in the order of NVSDK_NGX_Feature: ... dlssg(11), deepdvc(12), dlssd(13), truehdr, latewarp, vsr, fgx, dlssnr -> neural rendering is very likely feature 18 (Reserved18). To be tried in the test program in stage 2.
- Managed: General/OwnNativeLibrary (Game page "Own native library (test; restart the game)") loads plugins\CMADlssNr\CmaBridge.dll instead of nvngx.dll\DlssBridge.dll. Default off.

## 2026-10-09: stage 2 probe (native\test\probe_nr.cpp) - neural rendering cannot be had through NGX with the DLL at hand
- NVSDK_NGX_D3D11_CreateFeature for features 14..18 with nvngx_dlssnr.dll in the path list: all 0xBAD0000B (feature not found). NGX's log: "nvLoadSignedLibraryW() failed on snippet ...nvngx_dlssnr.dll missing or corrupted" (the same line is in the game's ngx\vdn.log on every start).
- Get-AuthenticodeSignature: nvngx_dlssnr.dll (310.8.0, "NVIDIA DLSSNR - DVS PRODUCTION") = HashMismatch, signer NVIDIA Corporation: the file was changed after NVIDIA signed it. nvngx_dlss.dll and nvngx_dlssg.dll (310.9.1) = Valid.
- So the borrowed bridge does not get NR through NGX's loader: it loads that DLL itself (vdn_view_init takes its full path) and calls its exports (NVSDK_NGX_D3D11_/D3D12_ Init_Ext, PopulateParameters_Impl, CreateFeature, EvaluateFeature are exported by the snippet), which goes round NGX's signature check.
- Not done and not to be done without the user's decision: writing the same side-loading into the mod's own library. What was changed in the DLL, and by whom, is not known. Reported to the user; the release archive contains this file.

## 2026-10-09: "try with the file there is" - tried, the DLL itself says no
- probe_nr2 (D3D11) and probe_nr3 (D3D12): nvngx_dlssnr.dll loaded with LoadLibrary, its own NVSDK_NGX_*_Init_Ext called with the core's capability parameters and NVSDK_NGX_Version_API: 0xBAD00002 (platform error) on both; PopulateParameters_Impl and CreateFeature the same. So calling its exported entry points is not enough.
- The borrowed bridge's own messages: "NGX init rejected the snippet (arch gate, caller check, or API version)", "Init_Ext(sdkVersion=0x%X)", and for frame generation "multi-frame: ... gates ... the provider may already have been patched" (it patches nvngx_dlssg in memory, VirtualProtect). So that bridge gets NR and the higher multipliers by defeating NVIDIA's checks (caller check / architecture gates), with a DLL that was itself changed (HashMismatch).
- Decision (mine, told to the user): the mod's own library does not reproduce any of that. NR stays with the borrowed bridge for as long as the user wants NR; the own library does DLSS now and frame generation next through NGX's ordinary, signed path (the driver advertises x5 on this RTX 5080 without any patching).
- NR parameter names seen in the DLL, for the record: Color, Output, Depth, MVec, ControlMask, UI, UIAlpha, Backbuffer, BidirectionalDistortionField (each with Subrect BaseX/BaseY/Width/Height), Intensity, LocalToneStrength, LocalStructureStrength, SkinStructureStrength, Style, ScalingRatio, Hint.Render.Preset, UICorrection, UseAutoMask, Reset, Enabled, Width, Height, MVecScaleX/Y, DepthInverted.
## 0.9.1 (installed; not run in game): both libraries side by side
- With General/OwnNativeLibrary on, vdn_sr_* (and its render event and rebuild lock) go to CmaBridge.dll; neural rendering and frame generation stay with DlssBridge.dll. Off: everything with DlssBridge.dll as before. Not known: whether NGX's core is content with two users in the process (D3D11 from the own library, D3D12 from the other).

## 2026-10-09: the user plays game version 0.66 now (C:\Games\Corrupted Modeling Agency 0.66), plugins copied there by the user (mod 0.8.2 from the archive ran there: NR, DLSS, FG, hair all in the log). csproj GameDir -> 0.66; 0.9.1 built against its interop without errors and installed there with CmaBridge.dll. The 0.65 folder still has 0.9.1 too.

## 0.8.4 (installed in 0.66 and 0.65; not run in game)
- User: go back to the previous version and work on that; next is changing NR. The own native library is set aside: not loaded, its switch and config entry gone, CmaBridge.dll removed from both game folders; sources stay in native\. Same as 0.8.3 otherwise (Network preset experimental on the Neural page).

## 0.8.5 (installed in 0.66 if "installed" below; not run in game): multi frame generation again, on the borrowed bridge
- User: network presets changed nothing -> removed (menu, config, crash guard). Wants the multi frame generator, as smooth as can be.
- FrameGen/Frames 0 auto, 2, 3, 4 and FrameGen/MatchRefreshRate (on). New idea against the 0.5.x failure (rate never still, pacing floor 30, on/off): the rendered frames are HELD at (refresh rate x 0.97) / multiplier by the driver (Reflex sleep minimumIntervalUs, restored in Latency.cs), so the bridge's pacing has an even rate to work from. Auto = fewest frames that fill the refresh rate: starts x2, goes up when rendered < 93 % of what is asked for 3 s (never to a multiplier whose rate would be under 34), tries one fewer after 30 s of holding, doubling that wait up to 300 s when it fails. Each change of multiplier makes the bridge rebuild its feature.
- NOT KNOWN: whether x3/x4 at a held rate is smooth in this game. To read after the run: "Frame generation:" lines, ngx\vdn_fg.log ("pacing ON/OFF").

## 0.8.5 run (game 0.66): "works very badly, x2 is the best"
- Screen is 280 Hz: the cap (refresh x 0.97 / multiplier = 136 / 91 / 68) was above what the game renders (60-73 light scene, ~43 heavy), so nothing was ever held; Auto ran x2 -> x3 -> x4 within seconds (each a rebuild). The idea of 0.8.5 was therefore not even tried.
- vdn_fg.log, x3 in the heavy scene: "pacing ON at 31 rendered/s: spreading 3 frames across 32.6 ms" / "pacing OFF (below the floor at 30 ...)" alternating, 29 on / 28 off: the flapping at the bridge's floor of 30. Rendered: x2 43, x3 31 there; light scene x2 73, x3 66, x4 63.
## 0.8.6 (built; NOT installed: game running; not run in game)
- Default back to x2 (FrameGen/Multiplier 2; 0 = auto, 3, 4).
- Cap follows the game, not the screen: 2 s uncapped after each change, then held at 93 % of what was measured (never over refresh x 0.97 / multiplier); lowered to 93 % of the rendered rate after 2 s short of it, raised 4 % after 15 s of holding it.
- Auto: up only when held rate >= 60 and 20 s since the last change (and 120 s after a step down); down when rendered < 40.
- Still unproven: that an even rate makes x3/x4 smooth with this bridge.

## 0.8.6 run (0.66, Ultra Quality + NR)
- x2: 35 rendered a second uncapped, held at 33-35 (steady). Elsewhere (lighter, probably NR off) x3 80 / x2 79 / x4 69 uncapped. Back in the heavy scene x4 rendered 31, then my cap chased it down: 64 -> 29 -> 21 ("held at 21"), a spiral of my own making. vdn_fg.log: pacing on/off at the floor again at x3/x4 (5 off / 6 on).
- Reading: with NR on the game renders ~35 at x2 here; x3/x4 bring that to ~31/21. More than x2 cannot be smooth at that base with this bridge.
## 0.8.7 (installed if "installed" above; not run in game)
- Cap no longer lowered to under the rendered rate: when the game falls short of it for 2 s the cap is let go and measured anew (0.5 s + 2 s). Held at 95 % (was 93 %).
- Auto: a multiplier above x2 that measures under 45 rendered a second is left at once.

## 0.8.8 (installed in 0.66; not run in game): frame generation is x2 only again; work on the mod paused by the user
- User: "leave x2, take the multi generator out, that is all for this mod for now". Multiplier choice, auto mode and the cap on rendered frames removed (FrameGen.cs, Latency.cs, config FrameGen/Multiplier and MatchRefreshRate). Same as 0.8.4 for frame generation.
- State left behind: mod 0.8.8 on the borrowed DlssBridge.dll; own native library (stage 1, DLSS on D3D11, tested outside the game only) in native\, not loaded; release\CMADlssNr-0.8.2.zip is OLD and holds the changed nvngx_dlssnr.dll and the borrowed DlssBridge.dll - not to be published as it is.

## 0.1.15 and after (2026-10-17)
- Archive release\CMADlssNr-0.1.15.zip built 09:28 (libraries updated, see CHANGELOG). Commits after it, not in that archive: dead code removed and one source for the version, scalp cap depth, menu pointer taken from Input.mousePosition (clicks used to land lower than the button). Not run in game.
- notes\DlssBridge_map.md: map of the decompiled DlssBridge.dll (subsystems, NGX parameters).
