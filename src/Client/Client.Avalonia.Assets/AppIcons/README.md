# FlowRate application icons

The selected application icon is **FR between headphone ear cups**, in `Headphones/`.
Use this set for application icons, tray icons, favicons and logos. The `Monitor/` set is retained as an alternative design.

Two separate identity kits share the application accent color `#6C63FF`:

- `Headphones/`: FR between headphone ear cups.
- `Monitor/`: FR inside a monitor matching the screen sharing icon.

Each kit contains SVG artwork, PNG exports, Windows and browser ICO files, horizontal wordmarks, previews and an independent build script. See each kit's README for sizes and intended uses.

Only the files the client shows are embedded as Avalonia resources: `Headphones/flowrate.ico` (window icon) and `Headphones/png/tray-purple-48.png` (tray). To load another file by URI, add it to `Client.Avalonia.Assets.csproj` first.

Example resource URIs:

```text
avares://Client.Avalonia.Assets/AppIcons/Headphones/flowrate.ico
avares://Client.Avalonia.Assets/AppIcons/Headphones/png/tray-purple-48.png
```

SVG resources require an SVG-capable image loader. PNG files can be used directly with Avalonia images; ICO files can be loaded as window icons.

The headphones kit is integrated into desktop window icons, the Windows executable, the tray outside voice sessions and Windows/Linux release packaging. Voice session status glyphs remain in use while connected. Android launcher resources are linked from `Headphones/Android/`, including legacy launcher PNGs, adaptive icons and a monochrome layer for themed icons.
