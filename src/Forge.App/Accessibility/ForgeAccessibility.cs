using DevExpress.Maui.Core;
using DevExpress.Maui.Editors;
using Microsoft.Maui.Handlers;

#if ANDROID
using Android.Views.Accessibility;
using AndroidView = Android.Views.View;
using AndroidViewGroup = Android.Views.ViewGroup;
using AndroidEditText = Android.Widget.EditText;
using AndroidImageButton = Android.Widget.ImageButton;
using AccessibilityAction = Android.Views.Accessibility.Action;
using SystemAction = System.Action;
#endif

namespace Forge.App.Accessibility;

/// <summary>
/// Closes the accessibility gaps that DevExpress controls cannot close from XAML.
/// </summary>
/// <remarks>
/// <para>
/// Two defects found on a device by <c>tools/smoke</c> are fixed here, and neither could be fixed
/// in markup, because both concern native views that XAML never names.
/// </para>
/// <para>
/// <b>Buttons announce without a role and expose no click action.</b> A <c>DXButton</c> reaches
/// Android as a bare <c>android.view.ViewGroup</c>. With <c>SemanticProperties.Description</c> set
/// it becomes focusable and TalkBack reads its label, but the node still reports
/// <c>clickable=false</c> and keeps the <c>ViewGroup</c> class name, so it is announced as
/// anonymous content rather than as a button and advertises no way to activate it. Ten controls
/// were reported this way in the baseline run.
/// </para>
/// <para>
/// <b>Composite editors label only their outer container.</b> A <c>ComboBoxEdit</c> renders as a
/// container holding an <c>EditText</c> and an <c>ImageButton</c>. Setting
/// <c>SemanticProperties.Description</c> puts a content description on the container only; the two
/// inner views are independently focusable and entirely anonymous, so a screen reader stops on
/// "edit box" and then "button" with no indication of what either does. The goal wizard's
/// "Primary goal" field is the case the harness caught, and the shape is identical for every combo
/// box and date field in the app.
/// </para>
/// <para>
/// Both are fixed centrally rather than page by page, so a screen nobody edited still benefits and
/// a future page cannot forget. Everything is best-effort: an accessibility enhancement must never
/// be the reason the app falls over, so failures are swallowed and leave the control exactly as
/// DevExpress rendered it.
/// </para>
/// <para>
/// Android only. iOS is in product scope but cannot be verified from this environment, and an
/// unverified accessibility claim is worse than an honest gap. See
/// <c>docs/accessibility/README.md</c>.
/// </para>
/// </remarks>
public static class ForgeAccessibility
{
    private const string MappingKey = "ForgeAccessibility";

    private static bool installed;

    /// <summary>Registers the platform accessibility fixes. Safe to call more than once.</summary>
    public static void Install()
    {
        if (installed)
        {
            return;
        }

        installed = true;

        // Appended to the shared view mapper rather than to each control's own mapper, because
        // every handler chains from this one. That is what makes the fix reach controls this file
        // never mentions.
        ViewHandler.ViewMapper.AppendToMapping(MappingKey, static (handler, view) =>
        {
            try
            {
                Apply(handler, view);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Forge accessibility mapping failed: {ex}");
            }
        });
    }

#if ANDROID
    private static void Apply(IViewHandler handler, IView view)
    {
        if (handler.PlatformView is not AndroidView platformView)
        {
            return;
        }

        // DevExpress builds an editor's inner views during layout, so the EditText and the
        // drop-down button do not exist yet when the handler is first connected. Posting defers
        // the pass to after the next layout, by which point they do.
        switch (view)
        {
            case DXButton button:
                platformView.Post(() => Guarded(() => ExposeButton(platformView, button)));
                break;

            case EditBase editor:
                platformView.Post(() => Guarded(() => LabelEditorParts(platformView, editor)));
                break;
        }
    }

    private static void Guarded(SystemAction action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Forge accessibility pass failed: {ex}");
        }
    }

    /// <summary>
    /// Gives a DevExpress button the button role and a real activation path.
    /// </summary>
    /// <remarks>
    /// The node is enriched through an accessibility delegate rather than by setting
    /// <c>Clickable</c> on the view itself. Marking the view clickable would insert Forge into
    /// DevExpress's own touch handling and risks either swallowing taps or firing a command twice;
    /// changing only what the accessibility node reports cannot affect ordinary touch at all.
    /// </remarks>
    private static void ExposeButton(AndroidView platformView, DXButton button)
    {
        if (string.IsNullOrEmpty(platformView.ContentDescription))
        {
            var description = ResolveDescription(button);
            if (!string.IsNullOrWhiteSpace(description))
            {
                platformView.ContentDescription = description;
            }
        }

        if (string.IsNullOrEmpty(platformView.ContentDescription))
        {
            // Nothing to announce, so exposing a nameless button would be a downgrade.
            return;
        }

        platformView.SetAccessibilityDelegate(new ButtonNodeDelegate());
        platformView.ImportantForAccessibility = Android.Views.ImportantForAccessibility.Yes;
    }

    /// <summary>
    /// Names the inner text field and affordance button of a composite DevExpress editor.
    /// </summary>
    private static void LabelEditorParts(AndroidView platformView, EditBase editor)
    {
        var description = ResolveDescription(editor);
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        foreach (var child in Descendants(platformView))
        {
            switch (child)
            {
                // Mirrors what MAUI's own Entry handler does for SemanticProperties.Description:
                // the description names the field and the typed value is still announced as its
                // value, so nothing the user has entered is hidden.
                case AndroidEditText field when string.IsNullOrEmpty(field.ContentDescription):
                    field.ContentDescription = description;
                    break;

                case AndroidImageButton affordance when string.IsNullOrEmpty(affordance.ContentDescription):
                    affordance.ContentDescription = DescribeAffordance(editor, description);
                    break;
            }
        }
    }

    private static IEnumerable<AndroidView> Descendants(AndroidView root)
    {
        if (root is not AndroidViewGroup group)
        {
            yield break;
        }

        for (var i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child is null)
            {
                continue;
            }

            yield return child;

            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Reports a DevExpress button to assistive technology as a button that can be activated.
    /// </summary>
    private sealed class ButtonNodeDelegate : AndroidView.AccessibilityDelegate
    {
        public override void OnInitializeAccessibilityNodeInfo(AndroidView host, AccessibilityNodeInfo info)
        {
            base.OnInitializeAccessibilityNodeInfo(host, info);

            if (info is null)
            {
                return;
            }

            info.ClassName = "android.widget.Button";
            info.Clickable = true;
            info.Focusable = true;
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick!);
        }

        public override bool PerformAccessibilityAction(AndroidView host, AccessibilityAction action, Android.OS.Bundle? args)
        {
            if (action == AccessibilityAction.Click)
            {
                // A synthetic tap rather than an invocation of Command, deliberately. It is the
                // one activation path that behaves identically for every DevExpress button
                // whether it is driven by Command or by a Clicked handler, and it exercises the
                // control's real code path instead of a parallel one that could drift from it.
                return DispatchTap(host);
            }

            return base.PerformAccessibilityAction(host, action, args);
        }

        private static bool DispatchTap(AndroidView host)
        {
            var now = Android.OS.SystemClock.UptimeMillis();
            var x = host.Width / 2f;
            var y = host.Height / 2f;

            var down = Android.Views.MotionEvent.Obtain(now, now, Android.Views.MotionEventActions.Down, x, y, 0);
            var up = Android.Views.MotionEvent.Obtain(now, now + 1, Android.Views.MotionEventActions.Up, x, y, 0);

            try
            {
                if (down is null || up is null)
                {
                    return false;
                }

                host.DispatchTouchEvent(down);
                host.DispatchTouchEvent(up);
                return true;
            }
            finally
            {
                down?.Recycle();
                up?.Recycle();
            }
        }
    }
#else
    private static void Apply(IViewHandler handler, IView view)
    {
        // iOS is deliberately untouched. See the remarks on this class.
        _ = handler;
        _ = view;
    }
#endif

    /// <summary>
    /// Describes the affordance a composite editor places beside its text field, so the inner
    /// button announces what it does rather than announcing nothing.
    /// </summary>
    private static string DescribeAffordance(EditBase editor, string description) => editor switch
    {
        ComboBoxEdit => $"Show options for {description}",
        DateEdit => $"Choose a date for {description}",
        TimeEdit => $"Choose a time for {description}",
        _ => $"Open {description}",
    };

    /// <summary>
    /// The text a control should announce, preferring an explicit description and falling back to
    /// whatever visible labelling the control already carries.
    /// </summary>
    private static string ResolveDescription(BindableObject element)
    {
        var description = SemanticProperties.GetDescription(element);
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        if (element is EditBase editor)
        {
            if (!string.IsNullOrWhiteSpace(editor.LabelText))
            {
                return editor.LabelText;
            }

            if (!string.IsNullOrWhiteSpace(editor.PlaceholderText))
            {
                return editor.PlaceholderText;
            }
        }

        if (element is DXButton button && button.Content is string content && !string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        return string.Empty;
    }
}
