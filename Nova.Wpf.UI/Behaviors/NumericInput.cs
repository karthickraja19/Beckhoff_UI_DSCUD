using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nova.Wpf.UI.Behaviors
{
    /// <summary>
    /// Attached behaviour that stops a <see cref="TextBox"/> ever holding something that
    /// is not a number.
    ///
    /// Every operator input on the Manual, Recipe and Settings screens is a distance, a
    /// speed or a count, and each one is read back with double.TryParse. A TryParse that
    /// fails returns silently, so typing letters into a speed box produced a Move button
    /// that simply did nothing — no motion and no message. Blocking the character at the
    /// keyboard is the only place the operator gets immediate feedback.
    ///
    /// Filters typing, pasting, drag-drop and the space bar (which PreviewTextInput does
    /// not raise). The candidate text is assembled exactly as the edit would leave it —
    /// selection replaced, caret honoured — so partial entries a person types on the way
    /// to a valid number ("-", "1.", ".5") are allowed through while "1.2.3" or "12a"
    /// are not.
    ///
    /// Usage:
    ///   xmlns:behaviors="clr-namespace:Nova.Wpf.UI.Behaviors;assembly=Nova.Wpf.UI"
    ///   &lt;TextBox behaviors:NumericInput.Mode="Decimal"/&gt;
    /// </summary>
    public static class NumericInput
    {
        /// <summary>What a box will accept.</summary>
        public enum NumericMode
        {
            /// <summary>No restriction (the default, so the property is opt-in).</summary>
            None = 0,

            /// <summary>Digits only — counts and iterations.</summary>
            Integer,

            /// <summary>Digits and one decimal point — speeds, wait times, magnitudes.</summary>
            Decimal,

            /// <summary>As <see cref="Decimal"/> plus a leading minus — positions, which
            /// are signed on every axis with travel either side of home.</summary>
            SignedDecimal,

            /// <summary>An IPv4 address: digits and dots only, at most four groups of three
            /// digits (FAM 2 SET_05).</summary>
            IpAddress,
        }

        /// <summary>
        /// The most digits the box may hold, sign and decimal point not counted; 0 = no limit.
        /// FAM 2 MS_01 / RC_05: the Manual and Recipe boxes take up to 8 digits.
        /// </summary>
        public static readonly DependencyProperty MaxDigitsProperty =
            DependencyProperty.RegisterAttached(
                "MaxDigits",
                typeof(int),
                typeof(NumericInput),
                new PropertyMetadata(0));

        public static int GetMaxDigits(DependencyObject element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return (int)element.GetValue(MaxDigitsProperty);
        }

        public static void SetMaxDigits(DependencyObject element, int value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            element.SetValue(MaxDigitsProperty, value);
        }

        public static readonly DependencyProperty ModeProperty =
            DependencyProperty.RegisterAttached(
                "Mode",
                typeof(NumericMode),
                typeof(NumericInput),
                new PropertyMetadata(NumericMode.None, OnModeChanged));

        public static NumericMode GetMode(DependencyObject element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return (NumericMode)element.GetValue(ModeProperty);
        }

        public static void SetMode(DependencyObject element, NumericMode value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            element.SetValue(ModeProperty, value);
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = d as TextBox;
            if (box == null)
                return;

            // Detach first so re-setting the property cannot double-subscribe.
            box.PreviewTextInput -= OnPreviewTextInput;
            box.PreviewKeyDown -= OnPreviewKeyDown;
            DataObject.RemovePastingHandler(box, OnPaste);

            if ((NumericMode)e.NewValue == NumericMode.None)
                return;

            box.PreviewTextInput += OnPreviewTextInput;
            box.PreviewKeyDown += OnPreviewKeyDown;
            DataObject.AddPastingHandler(box, OnPaste);
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var box = (TextBox)sender;
            e.Handled = !IsAllowed(box, GetMode(box), e.Text);
        }

        /// <summary>Space never reaches PreviewTextInput as a normal character on every
        /// keyboard layout, and a stray space breaks TryParse just as a letter does.</summary>
        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private static void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null)
                return;

            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            var pasted = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (!IsAllowed(box, GetMode(box), pasted))
                e.CancelCommand();
        }

        /// <summary>
        /// Builds the text the box would hold once <paramref name="insert"/> lands, then
        /// tests that. Testing the inserted characters alone would wrongly accept a
        /// second "." or a minus typed in the middle of a number.
        /// </summary>
        private static bool IsAllowed(TextBox box, NumericMode mode, string insert)
        {
            if (mode == NumericMode.None)
                return true;

            if (string.IsNullOrEmpty(insert))
                return false;

            string current = box.Text ?? string.Empty;
            int start = box.SelectionStart;
            int length = box.SelectionLength;
            if (start > current.Length) start = current.Length;
            if (start + length > current.Length) length = current.Length - start;

            string candidate = current.Substring(0, start)
                + insert
                + current.Substring(start + length);

            if (!IsCandidateValid(candidate, mode))
                return false;

            int maxDigits = GetMaxDigits(box);
            if (maxDigits > 0)
            {
                int digits = 0;
                foreach (char c in candidate)
                {
                    if (char.IsDigit(c))
                        digits++;
                }

                if (digits > maxDigits)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Accepts anything on the way to a number, so an operator can still type. The
        /// view models keep their TryParse guards, which is what rejects a half-finished
        /// entry such as "-" if a button is pressed mid-edit.
        /// </summary>
        internal static bool IsCandidateValid(string candidate, NumericMode mode)
        {
            if (string.IsNullOrEmpty(candidate))
                return true;

            switch (mode)
            {
                case NumericMode.Integer:
                    return Regex.IsMatch(candidate, @"^[0-9]*$");

                case NumericMode.Decimal:
                    return Regex.IsMatch(candidate, @"^[0-9]*(\.[0-9]*)?$");

                case NumericMode.SignedDecimal:
                    return Regex.IsMatch(candidate, @"^-?[0-9]*(\.[0-9]*)?$");

                case NumericMode.IpAddress:
                    return Regex.IsMatch(candidate, @"^[0-9]{0,3}(\.[0-9]{0,3}){0,3}$");

                default:
                    return true;
            }
        }

        /// <summary>
        /// Parses a field written by one of these boxes. Invariant first because the
        /// boxes only ever accept "." as the separator, whatever the machine's regional
        /// settings say.
        /// </summary>
        public static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
