using System;
using Android.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Client.Avalonia.Controls.Text;
using Client.Avalonia.Localization;
using Client.Core.Chat;
using NativeActionMode = Android.Views.ActionMode;
using NativeRect = Android.Graphics.Rect;
using NativeView = Android.Views.View;

namespace Client.Droid.Platform;

/// <summary>Uses Android's floating editing toolbar with Avalonia's text input and selection handles.</summary>
public static class NativeTextMenu
{
    private static Session? _activeSession;

    internal static bool TryDismissActive()
    {
        if (_activeSession is not { } session)
            return false;
        session.Close();
        return true;
    }

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(NativeTextMenu));

    private static readonly AttachedProperty<Session?> SessionProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Session?>("Session", typeof(NativeTextMenu));

    static NativeTextMenu()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((target, change) =>
        {
            target.GetValue(SessionProperty)?.Dispose();
            target.SetValue(SessionProperty, change.GetNewValue<bool>() ? new Session(target) : null);
        });
    }

    public static bool GetEnabled(TextBox target) => target.GetValue(EnabledProperty);
    public static void SetEnabled(TextBox target, bool value) => target.SetValue(EnabledProperty, value);

    private sealed class Session : NativeActionMode.Callback2
    {
        private readonly TextBox _target;
        private NativeActionMode? _mode;
        private NativeView? _anchor;
        private TopLevel? _topLevel;
        private bool _showPending;
        private bool _disposed;
        private int _generation;

        public Session(TextBox target)
        {
            _target = target;
            target.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
            target.AddHandler(InputElement.HoldingEvent, OnHolding, handledEventsToo: true);
            target.PropertyChanged += OnPropertyChanged;
            target.LostFocus += OnLostFocus;
            target.DetachedFromVisualTree += OnDetached;
        }

        private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;
            QueueShow();
        }

        private void OnHolding(object? sender, HoldingRoutedEventArgs e)
        {
            // TextBox selects the held word before this instance handler runs.
            if (e.HoldingState == HoldingState.Started)
                QueueShow();
        }

        private void QueueShow()
        {
            if (_showPending || _disposed)
                return;
            _showPending = true;
            int generation = _generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation)
                    return;
                _showPending = false;
                if (_disposed || !_target.IsFocused || !_target.IsEffectivelyVisible || !_target.IsEnabled)
                    return;
                if (_mode != null)
                {
                    _mode.Invalidate();
                    _mode.InvalidateContentRect();
                    return;
                }
                TryDismissActive();
                _anchor = MainActivity.Current?.Window?.DecorView;
                _mode = _anchor?.StartActionMode(this, ActionModeType.Floating);
                if (_mode != null)
                {
                    _activeSession = this;
                    _topLevel = TopLevel.GetTopLevel(_target);
                    _topLevel?.AddHandler(InputElement.PointerPressedEvent, OnRootPointerPressed,
                        RoutingStrategies.Tunnel, handledEventsToo: true);
                }
            });
        }

        private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Keep handle dragging available; an ordinary tap dismisses the toolbar even
            // when the tapped control does not take keyboard focus from the text box.
            if (e.Source is Visual source)
            {
                foreach (var visual in source.GetSelfAndVisualAncestors())
                    if (visual is TextSelectionHandle)
                        return;
            }
            Close();
        }

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == StyledElement.DataContextProperty || e.Property == TextBox.TextProperty ||
                (e.Property == InputElement.IsEnabledProperty && !_target.IsEnabled))
                Close();
            else if (_mode != null && (e.Property == TextBox.SelectionStartProperty ||
                     e.Property == TextBox.SelectionEndProperty || e.Property == TextBox.IsReadOnlyProperty ||
                     e.Property == TextBox.CanPasteProperty))
                QueueShow();
        }

        private void OnLostFocus(object? sender, FocusChangedEventArgs e) => Close();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Close();

        public void Close()
        {
            var mode = _mode;
            ClearMode();
            mode?.Finish();
        }

        private void ClearMode()
        {
            // Cancel queued updates as well as the visible toolbar. Otherwise a selection
            // update posted just before dismissal can create a new action mode afterwards.
            _generation++;
            _showPending = false;
            _topLevel?.RemoveHandler(InputElement.PointerPressedEvent, OnRootPointerPressed);
            _topLevel = null;
            if (ReferenceEquals(_activeSession, this))
                _activeSession = null;
            _mode = null;
            _anchor = null;
        }

        public override bool OnCreateActionMode(NativeActionMode? mode, IMenu? menu)
        {
            if (menu == null)
                return false;
            Add(menu, global::Android.Resource.Id.Cut, global::Android.Resource.String.Cut);
            Add(menu, global::Android.Resource.Id.Copy, global::Android.Resource.String.Copy);
            Add(menu, global::Android.Resource.Id.Paste, global::Android.Resource.String.Paste);
            Add(menu, global::Android.Resource.Id.SelectAll, global::Android.Resource.String.SelectAll);

            if (ChatInputFormatting.GetEnabled(_target))
            {
                foreach (var format in Enum.GetValues<ChatMarkdownFormat>())
                    menu.Add(1, FormatId(format), (int)format + 10, Strings.Get(FormatKey(format)));
            }
            return true;
        }

        private static void Add(IMenu menu, int id, int title) =>
            menu.Add(0, id, menu.Size(), title)?.SetShowAsAction(ShowAsAction.IfRoom);

        public override bool OnPrepareActionMode(NativeActionMode? mode, IMenu? menu)
        {
            if (menu == null)
                return false;
            bool password = _target.PasswordChar != '\0';
            bool selected = _target.SelectionStart != _target.SelectionEnd;
            menu.FindItem(global::Android.Resource.Id.Cut)?.SetVisible(selected && !password && !_target.IsReadOnly);
            menu.FindItem(global::Android.Resource.Id.Copy)?.SetVisible(selected && !password);
            menu.FindItem(global::Android.Resource.Id.Paste)?.SetVisible(_target.CanPaste && !_target.IsReadOnly);
            menu.FindItem(global::Android.Resource.Id.SelectAll)?.SetVisible(!string.IsNullOrEmpty(_target.Text));
            menu.SetGroupVisible(1, !_target.IsReadOnly && !password);
            return true;
        }

        public override bool OnActionItemClicked(NativeActionMode? mode, IMenuItem? item)
        {
            if (item == null)
                return false;
            if (item.ItemId == global::Android.Resource.Id.SelectAll)
            {
                _target.SelectAll();
                mode?.Invalidate();
                return true;
            }

            mode?.Finish();
            switch (item.ItemId)
            {
                case global::Android.Resource.Id.Cut: _target.Cut(); break;
                case global::Android.Resource.Id.Copy: _target.Copy(); break;
                case global::Android.Resource.Id.Paste: _target.Paste(); break;
                default:
                    if (item.GroupId != 1)
                        return false;
                    ChatInputFormatting.Apply(_target, (ChatMarkdownFormat)(item.ItemId - 100));
                    break;
            }
            return true;
        }

        public override void OnDestroyActionMode(NativeActionMode? mode)
        {
            if (_mode?.Equals(mode) == true)
                ClearMode();
        }

        public override void OnGetContentRect(NativeActionMode? mode, NativeView? view, NativeRect? outRect)
        {
            if (outRect == null || _anchor == null)
                return;
            var location = new int[2];
            _anchor.GetLocationOnScreen(location);
            var topLeft = _target.PointToScreen(default);
            var bottomRight = _target.PointToScreen(new Point(_target.Bounds.Width, _target.Bounds.Height));
            outRect.Set(Math.Max(0, topLeft.X - location[0]), Math.Max(0, topLeft.Y - location[1]),
                Math.Min(_anchor.Width, bottomRight.X - location[0]), Math.Min(_anchor.Height, bottomRight.Y - location[1]));
        }

        private static int FormatId(ChatMarkdownFormat format) => 100 + (int)format;

        private static string FormatKey(ChatMarkdownFormat format) => format switch
        {
            ChatMarkdownFormat.Bold => "chat.format.bold",
            ChatMarkdownFormat.Italic => "chat.format.italic",
            ChatMarkdownFormat.Strike => "chat.format.strike",
            ChatMarkdownFormat.Code => "chat.format.code",
            ChatMarkdownFormat.CodeBlock => "chat.format.codeBlock",
            ChatMarkdownFormat.Link => "chat.format.link",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                Close();
                _target.RemoveHandler(Control.ContextRequestedEvent, OnContextRequested);
                _target.RemoveHandler(InputElement.HoldingEvent, OnHolding);
                _target.PropertyChanged -= OnPropertyChanged;
                _target.LostFocus -= OnLostFocus;
                _target.DetachedFromVisualTree -= OnDetached;
            }
            base.Dispose(disposing);
        }
    }
}
