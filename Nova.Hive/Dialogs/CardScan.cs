using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nova.Hive
{
    /// <summary>
    /// The input rules of Audio SW DFM v5.4 slide 37 for a badge (Operator_ID) or a
    /// scanned SN: it must come from the reader in one burst of at most 200 ms, its
    /// length must be nominal, and the Control key and the right mouse button are
    /// disabled so nothing can be pasted in. A scan that breaks a rule is thrown away.
    /// </summary>
    /// <remarks>
    /// Readers on this machine are keyboard-wedge devices: they type the characters as
    /// fast keystrokes and finish with Enter. Feed every character to <see cref="Add"/>
    /// and call <see cref="Finish"/> on Enter.
    /// </remarks>
    public sealed class CardScan
    {
        /// <summary>Slide 37: "check whether the input ... is within 200ms".</summary>
        public static readonly TimeSpan MustFinishIn = TimeSpan.FromMilliseconds(200);

        /// <summary>A gap longer than this means the characters are not from one swipe.</summary>
        private static readonly TimeSpan NewScanAfter = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Nominal badge UID length. The WPF host sets these at startup from App.config
        /// (BadgeUidLength). The defaults accept any badge the sample access file holds.
        /// </summary>
        public static int MinimumLength { get; set; } = 3;

        public static int MaximumLength { get; set; } = 32;

        private readonly StringBuilder _characters = new StringBuilder();
        private readonly bool _serialNumber;
        private readonly int _snMinimum;
        private readonly int _snMaximum;
        private DateTime _started = DateTime.MinValue;
        private DateTime _lastKey = DateTime.MinValue;

        /// <summary>A badge scan, with the badge lengths above.</summary>
        public CardScan()
        {
        }

        private CardScan(int minimumLength, int maximumLength)
        {
            _serialNumber = true;
            _snMinimum = minimumLength;
            _snMaximum = maximumLength;
        }

        /// <summary>
        /// A scanned SN (glue, valve, nozzle, material - slide 37): the same 200 ms and
        /// no-typing rules, but every printable character counts ("+", "-", "_" are part of
        /// an SN) and the nominal length is the SN's own.
        /// </summary>
        public static CardScan ForSerialNumber(int minimumLength, int maximumLength)
        {
            return new CardScan(minimumLength, maximumLength);
        }

        /// <summary>Takes characters the reader typed. Only letters and digits belong to a UID.</summary>
        public void Add(string text)
        {
            DateTime now = DateTime.Now;
            if (now - _lastKey > NewScanAfter)
            {
                _characters.Clear();
                _started = now;
            }

            _lastKey = now;

            foreach (char c in text)
            {
                bool belongs = _serialNumber ? !char.IsControl(c) && !char.IsWhiteSpace(c) : char.IsLetterOrDigit(c);
                if (belongs)
                    _characters.Append(c);
            }
        }

        /// <summary>
        /// Enter was pressed. Returns the scanned value, or null with the reason in
        /// <paramref name="refused"/> (null too when nothing was scanned at all). The
        /// buffer is cleared either way - slide 37: "Software delete the character string".
        /// </summary>
        public string Finish(out string refused)
        {
            string value = _characters.ToString();
            bool tooSlow = DateTime.Now - _started > MustFinishIn;
            Clear();

            refused = null;
            if (value.Length == 0)
                return null;

            if (tooSlow)
            {
                refused = _serialNumber
                    ? "Typing is not allowed - please scan the SN with the scanner."
                    : "Typing is not allowed - please swipe your ID card.";
                return null;
            }

            int minimum = _serialNumber ? _snMinimum : MinimumLength;
            int maximum = _serialNumber ? _snMaximum : MaximumLength;
            if (value.Length < minimum || value.Length > maximum)
            {
                refused = "That is not a valid " + (_serialNumber ? "SN" : "badge") + " (" + value.Length + " characters, expected "
                    + (minimum == maximum
                        ? minimum.ToString()
                        : minimum + " to " + maximum) + "). Please " + (_serialNumber ? "scan" : "swipe") + " again.";
                return null;
            }

            return value;
        }

        public void Clear()
        {
            _characters.Clear();
            _started = DateTime.MinValue;
            _lastKey = DateTime.MinValue;
        }

        /// <summary>
        /// Slide 37: "Software disables the keyboard Control key and the mouse right button"
        /// on the element the reader types into, so nothing can be pasted in and there is no
        /// context menu. Handled events are caught too, so a TextBox cannot act on them first.
        /// </summary>
        public static void BlockManualEntry(UIElement target)
        {
            target.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(BlockControlKey), true);
            target.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(BlockRightButton), true);
            target.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(BlockRightButton), true);
            DataObject.AddPastingHandler(target, (sender, e) => e.CancelCommand());
            ContextMenuService.SetIsEnabled(target, false);
        }

        private static void BlockControlKey(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                e.Handled = true;
        }

        private static void BlockRightButton(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
                e.Handled = true;
        }
    }
}
