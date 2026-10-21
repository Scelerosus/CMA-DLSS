# DlssBridge.dll: карта по декомпилу

Источник: `C:\Tools\ghidra_dl\DlssBridge_decompiled.c` (Ghidra 12.1.4, 909 функций, 74 экспорта `vdn_*`, ABI 4).
Внутренние имена `FUN_*` ниже даны по адресам и по строкам, которые они используют. Подтверждено только то, что видно в строках.

## Подсистемы

| Что | Где | Статус в `native/src/bridge.cpp` |
|---|---|---|
| Super Resolution на D3D11 (`vdn_sr_*`) | `vdn_sr_open` 0x180007c00, `vdn_sr_query` 0x1800082a0, `vdn_sr_set_textures` 0x1800086e0; колбэк оптимальных настроек `FUN_1800160d0`; загрузка ядра NGX `FUN_180015ab0` (реестр `NGXCore\FullPath`, `_nvngx.dll`) | сделано (stage 1) |
| Neural rendering (`vdn_view_*`, `vdn_set_*`) | `vdn_view_init` 0x180008e70; параметры `DLSSNR.*` в `FUN_180012a60`; закрытый D3D12-снippet `nvngx_dlssnr.dll` через `FUN_180011e20`; общие D3D11/D3D12 текстуры и timeline-fence; обработка потери D3D12-устройства `FUN_1800052c0` | заглушки |
| Frame generation (`vdn_fg_*`) | `vdn_fg_open` 0x180006750 -> `FUN_180002f20` (хук DXGI, окно-пробник `VamDlssNrFgProbe`); сборка `FUN_180002ad0`; кадр `FUN_180003b60` (4,6 КБ, главная функция); параметры `DLSSG.*` в `FUN_18000dfe0`; доступность `FUN_18000d7f0`; ядро D3D12 `FUN_18000e960` | заглушки |
| Подмена swapchain для VR-компоситора (`vdn_vr_*`) | `FUN_18000a100`, `FUN_180009ad0` (OVR) | нет |
| Диагностика/лог | `vdn.log`, `vdn_fg.log` (`FUN_180014bc0`), `FUN_18000b960` | нет |
| Обнаружение сторонних хуков (`rtsshooks`, `afterburner`, прокси-DLL) | `FUN_180003430` | нет |

## Параметры NGX (из строк)

- DLSS-G: `DLSSG.Backbuffer/MVecs/Depth/OutputInterpolated/OutputDisableInterpolation/HUDLess`, `EnableInterp`, `MultiFrameIndex`, `MultiFrameCount(Max)`, матрицы `CameraViewToClip/ClipToCameraView/ClipToPrevClip/PrevClipToClip`, `Jitter*`, `MvecScale*`, `Camera{Pos,Up,Right,Fwd}{X,Y,Z}`, `CameraNear/Far/FOV/AspectRatio`, `DepthInverted`, `CameraMotionIncluded`, `Reset`, `MenuDetectionEnabled`, `EvalFlags`, `*Subrect*`, а также колбэки синхронизации очереди (`SyncSignal/SyncWait/QueueSubmit/SyncFlush*`).
- NR: `DLSSNR.Color/Depth/Output/ControlMask/Width/Height/Intensity/Style/LocalToneStrength/LocalStructureStrength/SkinStructureStrength/UseAutoMask/UICorrection/MVecScale*/Reset`, `DLSSNR.Hint.Render.Preset`.
- Экспорты ядра: `NVSDK_NGX_D3D12_Init_Ext/AllocateParameters/GetCapabilityParameters/DestroyParameters/CreateFeature/EvaluateFeature/ReleaseFeature/Shutdown`, плюс `PopulateParameters_Impl` из снippet.

## Что сознательно не переписывается

В оригинале есть код, который патчит в памяти модуль NVIDIA `nvngx_dlssg.dll` по жёстким смещениям, чтобы открыть множитель до 4x на картах, где драйвер этого не разрешает ("multi-frame: gates opened"), и обход проверок архитектуры/вызывающего у снippet NR. Этот обход ограничений чужого модуля в новую библиотеку не переносится: FG там будет работать в пределах того, что отдаёт драйвер (`vdn_fg_max_multiplier`).

## Порядок работ

1. FG ядро: приватное D3D12-устройство по LUID, общие текстуры/fence с D3D11, `DLSSG.*`, один множитель из драйвера.
2. Хук `Present1` + темп (`vdn_fg_set_pacing`, `unpaced_hz`, `base_hz`).
3. NR (`vdn_view_*`).
4. VR-часть и диагностика по необходимости.

Проверять каждый шаг только в игре; вне игры проверяется лишь компиляция.
