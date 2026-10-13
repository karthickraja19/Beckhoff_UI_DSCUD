using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.Hive;
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
    /// The scan itself follows slide 37 through <see cref="CardScan"/>: the whole badge
    /// within 200 ms, a nominal length, and no Control key or right mouse button on
    /// this screen. A scan that breaks a rule is thrown away and the reason shown. A
    /// stale partial scan is discarded too, so a keystroke from a bumped keyboard does
    /// not prefix itself onto the next genuine badge.
    /// </para>
    /// </remarks>
    public partial class LoginView : UserControl
    {
        private readonly CardScan _scan = new CardScan();

        public LoginView()
        {
            InitializeComponent();

            CardScan.BlockManualEntry(this);
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

            _scan.Clear();
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _scan.Add(e.Text);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            string refused;
            string uid = _scan.Finish(out refused);

            var vm = DataContext as LoginViewModel;
            if (vm == null)
                return;

            if (uid == null)
            {
                // Nothing scanned (a bare Enter) is ignored; a broken scan says why.
                if (refused != null && !vm.IsBusy)
                {
                    vm.ErrorMessage = refused;
                    e.Handled = true;
                }

                return;
            }

            e.Handled = true;

            // Fire and forget by design: SubmitCardAsync owns its own try/catch and
            // sets IsBusy, which is what stops a second swipe interleaving. Awaiting
            // here would mean an async void handler, whose exceptions bypass the
            // Dispatcher handler and take the process down.
            var ignored = vm.SubmitCardAsync(uid);
        }
    }
}
