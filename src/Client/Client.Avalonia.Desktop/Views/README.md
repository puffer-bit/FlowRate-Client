# Desktop views

Views compose the Desktop UI. Application workflows and platform services remain in their existing layers. The controls and style includes in this directory apply only to Desktop; Android continues to use its own views and the existing shared presentation resources.

## Shared presentation patterns

- `Common/Styles/UiResources.axaml` defines spacing, typography, surface radii, navigation sizes, and session header/sidebar dimensions. Load these resources before the Desktop styles in `App.axaml`.
- `DialogLayout` provides five rows: header, separator, body, separator, and footer. Put a `ScrollViewer` in the body when content can exceed the available height. Keep the action buttons outside that scroller.
- `DialogHeader` displays an icon, title, optional subtitle, optional close command, and optional status content. The `compact` class gives settings dialogs a tiled icon and section typography. Its content inherits the view's data context.
- Small forms use `MaxWidth` instead of forcing a width. `DialogOverlay` supplies that preferred width when space allows and constrains it to the host window. Large settings dialogs retain the existing fill-window behavior.
- `ActionLabel` supplies the existing 16px icon/text arrangement inside buttons. Keep the command, default/cancel behavior, busy state, and accessible name on the button itself.
- The `iconButton` class supplies the standard 32px hit target and centered content. Smaller/larger controls with a specific role retain their own dimensions. Use localized `AutomationProperties.Name` and tooltips for icon-only actions. Preserve the existing keyboard focus adorner.
- The `navigationRow` class supplies shared settings navigation visuals. Search results, subitem guides, folding, and experimental-icon animation remain scoped to settings navigation.
- `StatePanel` supplies icon/progress, title, description, and optional custom content for list states. It accepts custom localized inline content without changing translation order.

## Settings layout

Desktop styles enable `SettingsRowLayout.IsAdaptive` on shared settings rows. Editors move below their labels when the row is narrower than 600 logical pixels. Checkboxes, switches, and toggle buttons stay beside their labels, and explicit `ContentPlacement="Below"` is preserved. Resizing or changing the row content re-evaluates placement.

Audio device selectors and volume sliders use `settingsDevice` and `settingsVolume` styles. Their preferred widths apply in wide rows; in stacked rows they fit the available width. Avoid fixed-width horizontal panels for editor/value pairs: use a grid with a flexible editor column and an automatic value column.

## Chat composition

`ChatSessionControl` owns the timeline, scrolling/selection, composer, sidebars, and platform interactions. `ChatImageViewer` owns its visual overlay. `ChatComposer` retains text input, keyboard handling, clipboard/emoji behavior, and message submission. It composes `ChatMentionSuggestions`, `ChatReplyBar`, and `ChatAttachmentList`; composer styles are scoped through `Sessions/Chat/Styles/ChatComposer.axaml`.

Keep the input focused while navigating suggestions. Selection is reported from the suggestion view to the composer, which performs text completion. Preserve reply folding, attachment commands, image-viewer Escape handling, and focus restoration when changing these components.

## UI verification

Build Desktop to validate compiled bindings and XAML. Also check layout at narrow/wide sizes, long translations, runtime light/dark and language switching, disabled animations, keyboard focus/default/cancel actions, chat suggestions/replies/attachments, and image viewer actions. Headless rendering can verify control layout and resources; native window behavior, accessibility tools, and media surfaces still require platform checks.
