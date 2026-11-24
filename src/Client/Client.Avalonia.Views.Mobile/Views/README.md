# Mobile views

Views are grouped by feature using the same directory conventions as [Desktop](../../Client.Avalonia.Views.Desktop/Views/README.md). Keep code-behind beside XAML and match namespaces to directories.

- `Main`: application shell, navigation, status bar, and startup screen.
- `Common`: shared transitions and bottom sheet; `Common/Styles` holds its template, and `Common/Media` holds the video surface and audio device picker used across features.
- `Connections`, `Dialogs`, `Profile`, and `Notifications`: connection form, prompts, profile/account screens, and toasts. `ScreenDialogPresenter` shows the shared dialogs over the whole screen in a `DialogFrame`, and Back dismisses the topmost one; `Common/Styles/Dialogs.axaml` sizes them for a finger.
- `Settings`: settings host; `Components` contains embedded settings controls.
- `Sessions/List`, `Sessions/Create`, `Sessions/Chat`, and `Sessions/Hybrid`: session lists, creation, chat messages and players, and calls with screen viewing and statistics.

The views use no platform APIs: what they need of the phone comes through `IMobilePlatform` (`Platform`), which the host implements. The Android host keeps its services in its own `Platform` folder, with `AndroidMainViewIntegration` tying the root view to notifications and the activity. Shared presentation resources remain in the shared Avalonia projects. Build the Android project to validate namespaces, compiled bindings, and XAML resource paths after moving views.
