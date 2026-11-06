# FlowRate monitor identity assets

FR inside a monitor based on the application's screen sharing button. Accent: `#6C63FF`.

This kit is independent of the headphones assets and does not replace application resources.

## Contents

- `svg/app-icon.svg`: rounded purple app icon.
- `svg/app-icon-square.svg`: full square background for consumers that apply a mask; not an Android adaptive icon resource.
- `svg/mark-purple.svg`, `mark-white.svg`, `mark-dark.svg`: transparent marks for light, dark and monochrome uses.
- `svg/tray-purple.svg`, `tray-white.svg`, `tray-dark.svg`: compact geometry with heavier lettering for small sizes.
- `svg/favicon.svg`: compact white mark on a purple tile.
- `svg/wordmark-on-light.svg`, `wordmark-on-dark.svg`: horizontal logos with outlined lettering and no font dependency.
- `png/`: app exports at 16, 24, 32, 48, 64, 128, 256, 512 and 1024 px; square app icon at 1024 px; transparent marks at 64, 128, 256 and 512 px; tray exports at 16, 20, 24, 32 and 48 px; favicon at 16, 32 and 48 px; horizontal logos at 1700 × 512 px.
- `flowrate.ico`: Windows icon with seven frames from 16 to 256 px.
- `favicon.ico`: browser favicon with 16, 32 and 48 px frames.
- `preview.svg`, `preview.png`: comparison sheet on light and dark backgrounds.

All artwork SVGs are self-contained shapes and paths. Preview backgrounds are for presentation only.

## Rebuild and validation

Run `python3 src/Client/Client.Avalonia.Assets/AppIcons/Monitor/build.py` from the client repository root. Requires PyGObject with Rsvg 2.0, Pycairo, Pillow and Noto Sans Bold for outlining the wordmark. The builder includes monitor geometry and has no dependency on the original concept files.

SVG XML parsing, librsvg rendering, PNG decoding and ICO frame counts were checked. The preview was visually reviewed. Appearance in a running operating-system tray has not been checked.
