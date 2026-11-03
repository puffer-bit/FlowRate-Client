# Project conventions

- Write all project documentation in English, including instruction files and code comments.

## Client interface

- New and modified menus must strictly match the existing flyout style, including icons, colors, spacing, dimensions, corner radii, and hover, pressed, focus, and disabled states.
- Use the shared `Flyout` and `MenuItemButton` components and the existing styles in `src/Client/Client.Avalonia.Styles/Styles/FlyoutStyle.axaml` and `src/Client/Client.Avalonia.Controls/Controls/Menus/MenuItemButton.axaml`. Do not replace them with standard `ContextMenu` / `MenuItem` controls or separate styling.
- Use icons from the shared `src/Client/Client.Avalonia.Assets/IconImages.axaml` collection. Add new icons to that collection following its conventions; neutral colors must follow the theme through shared brush resources.
- Before implementing a menu, consult existing client flyouts, such as `src/Client/Client.Avalonia.Desktop/Views/Sessions/Chat/ChatMessageFlyout.axaml`.
