using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.ViewModels.Auth;

namespace Nova.Wpf.App.Views.Auth
{
    /// <summary>
    /// Captures the ID card reader for the login screen - Audio SW DFM v5.4 slide 44,
    /// "Swipe/Scan ID card to auto load login info".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Badge readers on this class of machine are keyboard-wedge devices: the reader
    /// types the UID as fast keystrokes and finishes with Enter. There is no device
    /// API to call, so the screen listens for those keystrokes and assembles them.
    /// </para>
    /// <para>
    /// This is the only reason the screen has code-behind. It is input plumbing for a
    /// hardware device rather than page logic - the decision about what a badge means
    /// stays in LoginViewModel, which is reachable without WPF. Nothing here is
    /// editable by hand, which is what slide 44's "Data are read only" and slide 62's
    /// "Manually key-in is NOT allowed" require: the buffer below is private, is only
    /// ever appended to by a key event, and is never rendered.
    /// </para>
    /// <para>
    /// A stale partial scan is discarded after <see cref="ScanTimeout"/>. Without it,
    /// a keystroke from a bumped keyboard would sit in the buffer and prefix itself
    /// onto the next genuine badge, turning a valid card into "card not recognised".
    /// </para>
    /// </remarks>
    public partial class LoginView : UserControl
    {
        /// <summary>
        /// A wedge reader delivers a whole badge in tens of milliseconds. A gap longer
        /// than this means the characters did not come from one scan.
        /// </summary>
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(1);

        /// <summary>Guards against a stray keypress being treated as a badge.</summary>
        private const int MinimumUidLength = 3;

        private readonly StringBuilder _buffer = new StringBuilder();
        private DateTime _lastKey = DateTime.MinValue;

        public LoginView()
        {
            InitializeComponent();
        }

        private void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            // The reader types wherever focus is, so the screen has to hold it.
            Focus();
            Keyboard.Focus(this);

            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), true);
            AddHandler(TextCompositionManager.PreviewTextInputEvent,
                new TextCompositionEventHandler(OnPreviewTextInput), true);
        }

        private void LoginView_Unloaded(object sender, RoutedEventArgs e)
        {
            RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
            RemoveHandler(TextCompositionManager.PreviewTextInputEvent,
                new TextCompositionEventHandler(OnPreviewTextInput));

            _buffer.Clear();
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ExpireStaleScan();

            foreach (char c in e.Text)
            {
                // Badge UIDs are alphanumeric. Anything else is not part of a scan.
                if (char.IsLetterOrDigit(c))
                    _buffer.Append(c);
            }

            _lastKey = DateTime.Now;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            ExpireStaleScan();

            string uid = _buffer.ToString();
            _buffer.Clear();
            _lastKey = DateTime.MinValue;

            if (uid.Length < MinimumUidLength)
                return;

            var vm = DataContext as LoginViewModel;
            if (vm == null)
                return;

            e.Handled = true;

            // Fire and forget by design: SubmitCardAsync owns its own try/catch and
            // sets IsBusy, which is what stops a second swipe interleaving. Awaiting
            // here would mean an async void handler, whose exceptions bypass the
            // Dispatcher handler and take the process down.
            var ignored = vm.SubmitCardAsync(uid);
        }

        private void ExpireStaleScan()
        {
            if (_lastKey != DateTime.MinValue && DateTime.Now - _lastKey > ScanTimeout)
                _buffer.Clear();
        }
    }
}
