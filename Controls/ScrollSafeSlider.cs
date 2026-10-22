// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace VoiceDucker.Controls;

/// <summary>Allows wheel scrolling past a slider without editing its value.</summary>
public sealed class ScrollSafeSlider : Slider
{
    private bool _pointerEditing;
    private bool _keyboardEditing;

    public bool IsEditing => _pointerEditing || _keyboardEditing;
    public event EventHandler? EditingCompleted;

    public ScrollSafeSlider()
    {
        AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => SetPointerEditing(true)), true);
        AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, _) => SetPointerEditing(false)), true);
        AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler((_, _) => SetPointerEditing(false)), true);
        AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => SetPointerEditing(false)), true);
        AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (IsEditKey(args.Key)) SetKeyboardEditing(true);
        }), true);
        AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, args) =>
        {
            if (IsEditKey(args.Key)) SetKeyboardEditing(false);
        }), true);
        LostFocus += (_, _) => SetKeyboardEditing(false);
    }

    public void ApplyModelValue(double value)
    {
        if (!IsEditing && Value != value) Value = value;
    }

    private void SetPointerEditing(bool editing)
    {
        var wasEditing = IsEditing;
        _pointerEditing = editing;
        if (wasEditing && !IsEditing) EditingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void SetKeyboardEditing(bool editing)
    {
        var wasEditing = IsEditing;
        _keyboardEditing = editing;
        if (wasEditing && !IsEditing) EditingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsEditKey(VirtualKey key) => key is
        VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
        VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown;

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs args)
    {
        // Leave the event unhandled for the containing ScrollViewer. Pointer
        // dragging and keyboard editing retain the normal Slider behavior.
    }
}
