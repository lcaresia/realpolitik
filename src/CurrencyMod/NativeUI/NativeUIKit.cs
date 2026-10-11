using System.Collections.Generic;
using System.Linq;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using Amplitude.UI.Interactables;
using Amplitude.Mercury.UI;
using UnityEngine;

namespace CurrencyMod.NativeUI
{
    /// <summary>Common helpers to build UI from pieces cloned from the game.</summary>
    internal static class NativeUIKit
    {
        /// <summary>
        /// Clones a piece (from any window) under an inactive parent, removes scripts that would try to
        /// bind to the original window and places the clone at the destination, visible.
        /// </summary>
        public static Transform Clone(Transform donor, Transform parent, string name, params string[] stripTypes)
        {
            var stash = new GameObject("CurrencyMod_Stash");
            stash.SetActive(false);
            try
            {
                GameObject clone = Object.Instantiate(donor.gameObject, stash.transform);
                clone.name = name;
                foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && stripTypes.Contains(behaviour.GetType().Name))
                    {
                        Object.DestroyImmediate(behaviour);
                    }
                }
                clone.transform.SetParent(parent, false);
                UITransform ui = clone.GetComponent<UITransform>();
                if (ui != null)
                {
                    ui.VisibleSelf = true;
                    ui.InteractiveSelf = true;
                }
                return clone.transform;
            }
            finally
            {
                Object.Destroy(stash);
            }
        }

        /// <summary>Releases all anchors and positions by the top-left edge, relative to the parent.</summary>
        public static void Place(Transform target, float left, float top, float width, float height)
        {
            UITransform ui = target.GetComponent<UITransform>();
            ui.LeftAnchor = ui.LeftAnchor.SetAttach(false);
            ui.RightAnchor = ui.RightAnchor.SetAttach(false);
            ui.TopAnchor = ui.TopAnchor.SetAttach(false);
            ui.BottomAnchor = ui.BottomAnchor.SetAttach(false);
            ui.PivotXAnchor = ui.PivotXAnchor.SetAttach(false);
            ui.PivotYAnchor = ui.PivotYAnchor.SetAttach(false);
            ui.Pivot = Vector2.zero;
            ui.Width = width;
            ui.Height = height;
            ui.X = left;
            ui.Y = top;
        }

        /// <summary>
        /// Comparison bar cloned from diplomacy: the fill (Mask/Value) comes in the morale-state color of the source screen
        /// (it varies per session: crimson, pink...) and the separator sits in the middle. Explicit color and no separator.
        /// </summary>
        public static void StyleGauge(Transform valueGroup, Color fill)
        {
            if (valueGroup == null)
            {
                return;
            }
            Transform value = valueGroup.Find("Mask/Value");
            UISquircleImage image = value != null ? value.GetComponent<UISquircleImage>() : null;
            if (image != null && image.Color != fill)
            {
                image.Color = fill;
            }
            Transform separator = valueGroup.Find("Separator");
            if (separator != null)
            {
                NativeBankWindow.SetVisible(separator, false);
            }
            // The variation mark (DeltaGroup) sits in the middle of the bar, 6 px wide: it is diplomacy's morale "before and after".
            Transform delta = valueGroup.parent != null ? valueGroup.parent.Find("DeltaGroup") : null;
            if (delta != null)
            {
                NativeBankWindow.SetVisible(delta, false);
            }
        }

        private static readonly System.Collections.Generic.Dictionary<UITextField, object> hookedFields = new System.Collections.Generic.Dictionary<UITextField, object>();

        /// <summary>
        /// The field creates its responder (what fires TextChange) on load, after being cloned: a subscription made earlier is lost.
        /// Call every frame; it rebinds the event whenever the responder changes.
        /// </summary>
        public static void EnsureTextChange(UITextField field, System.Action<IUITextField, string> handler)
        {
            object responder = field != null ? field.TextFieldResponder : null;
            if (responder == null)
            {
                return;
            }
            if (hookedFields.TryGetValue(field, out object current) && ReferenceEquals(current, responder))
            {
                return;
            }
            field.TextChange -= handler;
            field.TextChange += handler;
            hookedFields[field] = responder;
        }

        private static readonly Dictionary<UITextField, int> fieldLengths = new Dictionary<UITextField, int>();
        private static readonly Dictionary<UITextField, int> fieldRelayouts = new Dictionary<UITextField, int>();

        /// <summary>
        /// Pasting a large text left the field "empty": UITextField positions the text by the cursor position
        /// (GetCharPosition) right after changing the label text, before the label re-lays out its lines. With the stale
        /// position, the text is pushed out of the mask. Call every frame: when the text changed by more than one
        /// character at once (paste), it repositions with the label already laid out.
        /// </summary>
        public static void FixLayoutAfterPaste(UITextField field)
        {
            if (field == null)
            {
                return;
            }
            int length = field.Text?.Length ?? 0;
            if (fieldLengths.TryGetValue(field, out int previous) && Mathf.Abs(length - previous) > 1)
            {
                fieldRelayouts[field] = 3; // a few frames: the label may take one to re-lay out its lines
            }
            fieldLengths[field] = length;
            if (fieldRelayouts.TryGetValue(field, out int left) && left > 0)
            {
                fieldRelayouts[field] = left - 1;
                if (field.IsEditing && field.label != null)
                {
                    // With the label already laid out, the game's calculation brings the cursor (and surrounding text) back into the mask.
                    field.MoveCaretToCharIndex(field.caretIndex, force: true);
                }
            }
        }

        /// <summary>
        /// Actually visible on screen: the window may stay Shown while the screens group is hidden (end of turn,
        /// diplomacy cursor), and then nothing it shows has been seen.
        /// </summary>
        public static bool IsReallyVisible(UITransform ui) => ui != null && ui.gameObject.activeInHierarchy && ui.VisibleGlobally;

        public static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
            }
        }

        public static UILabel Label(Transform target) => target != null ? target.GetComponent<UILabel>() : null;

        public static void SetText(Transform target, string text)
        {
            UILabel label = Label(target);
            if (label != null && label.Text != text)
            {
                label.Text = text;
            }
        }

        public static void Align(Transform target, HorizontalAlignment horizontal)
        {
            UILabel label = Label(target);
            if (label != null)
            {
                label.Alignment = new Alignment(horizontal, VerticalAlignment.Center);
            }
        }

        public static void SetVisible(Transform target, bool visible)
        {
            UITransform ui = target != null ? target.GetComponent<UITransform>() : null;
            if (ui != null && ui.VisibleSelf != visible)
            {
                ui.VisibleSelf = visible;
            }
        }

        /// <summary>
        /// Hover help on any element. If the piece has no UITooltip (labels, bars),
        /// the native component is added; the text uses the game's standard tooltip (title + description).
        /// </summary>
        public static void Tip(Transform target, string title, string description)
        {
            if (target == null)
            {
                return;
            }
            if (target.GetComponent<UITooltip>() == null)
            {
                target.gameObject.AddComponent<UITooltip>();
            }
            NativeBankWindow.Tip(target, string.Empty, title, description);
        }

        private const string BadgeDonor = "AllSettlementsWindow/_SettlementsList/Scrollview/Viewport/SettlementItemsTable/_SettlementItemSample/Table/Top/StatsTable/PopCount";

        /// <summary>
        /// Badge in the corner of a bar button (new letters, alert): a native card chip, reddish, hidden
        /// until it gets text. Null if the donor (the game's city list) does not exist yet.
        /// </summary>
        public static Transform CreateBadge(Transform parent, string name)
        {
            Transform donor = DevTools.FindByPath(BadgeDonor);
            if (donor == null)
            {
                return null;
            }
            Transform chip = Clone(donor, parent, name);
            Transform picto = chip.Find("Picto");
            if (picto != null)
            {
                Object.DestroyImmediate(picto.gameObject);
            }
            UILabel label = chip.GetComponent<UILabel>();
            label.Margins = new RectMargins(5f, 5f, 0f, 0f);
            label.FontSize = 13;
            label.AutoAdjustWidth = true;
            label.Alignment = new Alignment(HorizontalAlignment.Center, VerticalAlignment.Center);
            UISquircleImage background = chip.GetComponent<UISquircleImage>();
            if (background != null)
            {
                background.Color = new Color(0.72f, 0.20f, 0.16f, 0.95f);
            }
            Place(chip, 32f, -6f, 22f, 20f);
            SetVisible(chip, false);
            return chip;
        }

        /// <summary>Shows the badge with the text, or hides it if the text is empty.</summary>
        public static void SetBadge(Transform badge, string text)
        {
            if (badge == null)
            {
                return;
            }
            SetVisible(badge, !string.IsNullOrEmpty(text));
            UILabel label = Label(badge);
            if (label != null && !string.IsNullOrEmpty(text))
            {
                label.Text = text;
                label.AdjustSizesIfNecessary();
            }
        }

        /// <summary>
        /// Text field that captures the whole keyboard while the player types, like the in-match ones (rename city,
        /// chat, tech search: KeyboardOnly). Fields cloned from out-of-match screens (save notes) only captured
        /// characters, and the physical keys fired the game's shortcuts in the middle of the text.
        /// </summary>
        public static void BlockGameShortcuts(UITextField field)
        {
            if (field != null)
            {
                field.blockedEvents |= UIEventBlockingMask.KeyboardOnly;
            }
        }

        /// <summary>
        /// The player is typing in a text field. The mod's shortcuts (F8, F10) read the keyboard straight from Unity and
        /// bypass the field's capture, so they check this first.
        /// </summary>
        public static bool TypingInField()
        {
            return UIInteractivityManager.Instance?.FocusedResponder is UITextFieldResponder;
        }

        /// <summary>
        /// Releases a text field's focus when its window is hidden. While hidden, the field keeps capturing the keyboard
        /// (KeyboardOnly) and swallows ESC and the game's shortcuts until the next click.
        /// </summary>
        public static void ReleaseTextFocus()
        {
            if (TypingInField())
            {
                UIInteractivityManager.Instance.SetFocus();
            }
        }

        /// <summary>Tears down a UI object safely (unloads before destroying).</summary>
        public static void Dispose(Transform target)
        {
            if (target != null)
            {
                target.gameObject.SetActive(false);
                Object.Destroy(target.gameObject);
            }
        }
    }
}
