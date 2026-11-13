# Android views

Views are grouped by feature using the same directory conventions as [Desktop](../../Client.Avalonia.Desktop/Views/README.md). Keep code-behind beside XAML and match namespaces to directories.

- `Main`: application shell, navigation, status bar, and startup screen.
- `Common`: shared transitions and bottom sheet; `Common/Styles` holds its template, and `Common/Media` holds the video surface and audio device picker used across features.
- `Connections`, `Dialogs`, `Profile`, and `Notifications`: connection form, prompts, profile/account screens, and toasts.
- `Settings`: settings host; `Components` contains embedded settings controls.
- `Sessions/List`, `Sessions/Create`, `Sessions/Chat`, and `Sessions/Hybrid`: session lists, creation, chat messages and players, and calls with screen viewing and statistics.

Android platform services remain in `Platform`. Shared presentation resources remain in the shared Avalonia projects. Build the Android project to validate namespaces, compiled bindings, and XAML resource paths after moving views.
