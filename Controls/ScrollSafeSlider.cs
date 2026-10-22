// VoiceDucker - Copyright (c) 2026 Umberto Giacobbi. MIT license.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace VoiceDucker.Controls;

/// <summary>
/// Allows wheel scrolling past a slider and preserves active pointer or keyboard edits during rendering.
/// </summary>
public sealed class ScrollSafeSlider : Slider
{
    #region Private fields

    private bool _pointerEditing;
    private bool _keyboardEditing;

    #endregion

    #region Initialization

    /// <summary>
    /// Observes handled input events so every completed gesture releases model-value rendering.
    /// </summary>
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
            if (IsEditKey(args.Key))
            {
                SetKeyboardEditing(true);
            }
        }), true);
        AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, args) =>
        {
            if (IsEditKey(args.Key))
            {
                SetKeyboardEditing(false);
            }
        }), true);
        LostFocus += (_, _) => SetKeyboardEditing(false);
    }

    #endregion

    #region Public methods and events

    /// <summary>Gets whether a pointer gesture or held editing key currently controls the value.</summary>
    public bool IsEditing => _pointerEditing || _keyboardEditing;

    /// <summary>Raised when the last active pointer or keyboard edit finishes.</summary>
    public event EventHandler? EditingCompleted;

    /// <summary>
    /// Applies a model snapshot only while the user is not actively editing the slider.
    /// </summary>
    public void ApplyModelValue(double value)
    {
        if (!IsEditing && Value != value)
        {
            Value = value;
        }
    }

    #endregion

    #region Implementation

    /// <summary>
    /// Tracks a pointer gesture and announces completion after all input editing has stopped.
    /// </summary>
    private void SetPointerEditing(bool editing)
    {
        var wasEditing = IsEditing;
        _pointerEditing = editing;

        if (wasEditing && !IsEditing)
        {
            EditingCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Tracks a held editing key and announces completion after all input editing has stopped.
    /// </summary>
    private void SetKeyboardEditing(bool editing)
    {
        var wasEditing = IsEditing;
        _keyboardEditing = editing;

        if (wasEditing && !IsEditing)
        {
            EditingCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Identifies the navigation keys that change a slider's value.
    /// </summary>
    private static bool IsEditKey(VirtualKey key) => key is
        VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
        VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown;

    /// <summary>
    /// Leaves wheel input available to the containing scroll viewer without editing this slider.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs args)
    {
        // Leave the event unhandled for the containing ScrollViewer. Pointer
        // dragging and keyboard editing retain the normal Slider behavior.
    }

    #endregion
}
