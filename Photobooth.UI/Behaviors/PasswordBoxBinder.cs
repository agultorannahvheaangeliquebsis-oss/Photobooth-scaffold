using System.Windows;
using System.Windows.Controls;

namespace Photobooth.UI.Behaviors;

/// <summary>
/// Attached properties that make <see cref="PasswordBox.Password"/> bindable.
/// PasswordBox deliberately doesn't expose Password as a DependencyProperty
/// (so the value can't be left sitting in the property store), which normally
/// forces admin-PIN entry into code-behind. This keeps the admin overlay's PIN
/// on the ViewModel like every other field, without the window needing a
/// PasswordChanged handler of its own.
///
/// Usage requires BOTH properties:
/// <code>
/// beh:PasswordBoxBinder.Attach="True"
/// beh:PasswordBoxBinder.BoundPassword="{Binding Pin, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
/// </code>
///
/// Why a separate <see cref="AttachProperty"/> rather than subscribing from
/// BoundPassword's own PropertyChangedCallback: that callback only runs when
/// the value actually CHANGES. A ViewModel property initialised to
/// string.Empty bound to a DP whose default is also string.Empty never
/// changes, so the callback never runs, PasswordChanged is never subscribed,
/// and nothing the user types reaches the ViewModel -- the bound value stays
/// empty forever while the box visibly fills up. (Confirmed by running the
/// kiosk: the correct PIN was rejected and the box wasn't cleared on
/// failure.) Attach goes False -> True exactly once, which always fires, so
/// the subscription no longer depends on what the bound value happens to
/// start as.
///
/// The value still lives in a plain string on the ViewModel, which is the same
/// exposure AdminWindow's existing PIN check already accepts -- this is a
/// 4-digit booth PIN gating a settings panel, not a credential.
/// </summary>
public static class PasswordBoxBinder
{
    public static readonly DependencyProperty AttachProperty =
        DependencyProperty.RegisterAttached(
            "Attach", typeof(bool), typeof(PasswordBoxBinder), new PropertyMetadata(false, OnAttachChanged));

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinder),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    /// <summary>Guards the ViewModel -> control leg while the control -> ViewModel
    /// leg is mid-flight. Without it, pushing the typed value onto the ViewModel
    /// bounces straight back into PasswordBox.Password, which resets the caret to
    /// the start of the box on every keystroke.</summary>
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating", typeof(bool), typeof(PasswordBoxBinder), new PropertyMetadata(false));

    public static bool GetAttach(DependencyObject element) => (bool)element.GetValue(AttachProperty);

    public static void SetAttach(DependencyObject element, bool value) => element.SetValue(AttachProperty, value);

    public static string GetBoundPassword(DependencyObject element) =>
        (string)element.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject element, string value) =>
        element.SetValue(BoundPasswordProperty, value);

    private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        // Unsubscribe unconditionally first so flipping Attach back and forth
        // can never leave two handlers on the same box.
        box.PasswordChanged -= OnPasswordChanged;

        if (e.NewValue is true)
        {
            box.PasswordChanged += OnPasswordChanged;

            // Catch up on whatever the binding already pushed in: attribute
            // order in XAML decides whether BoundPassword was applied before
            // or after Attach, and the box must end up showing the bound value
            // either way.
            string bound = GetBoundPassword(box);
            if (!string.IsNullOrEmpty(bound) && box.Password != bound)
            {
                box.Password = bound;
            }
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box || (bool)box.GetValue(IsUpdatingProperty))
        {
            return;
        }

        // ViewModel -> control. Reached when the ViewModel assigns the property
        // itself, e.g. clearing the PIN after a failed unlock.
        box.Password = e.NewValue as string ?? string.Empty;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
