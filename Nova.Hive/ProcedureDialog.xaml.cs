using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>One SN the procedure dialog scans.</summary>
    public sealed class ProcedureField
    {
        public ProcedureField(string key, string label, string oldValue = null)
        {
            Key = key;
            Label = label;
            OldValue = oldValue;
        }

        public string Key { get; }

        public string Label { get; }

        /// <summary>Shown as recorded; null hides the "Old SN" line.</summary>
        public string OldValue { get; }

        /// <summary>False for an SN the operator may leave unscanned (e.g. only one gantry's glue changed).</summary>
        public bool Required { get; set; } = true;

        public int MinimumLength { get; set; } = 4;

        public int MaximumLength { get; set; } = 80;

        /// <summary>Checks a scanned value; returns why it is refused, or null to accept it.</summary>
        public Func<string, string> Validate { get; set; }

        /// <summary>The scanned value; null until one is accepted.</summary>
        public string Value { get; internal set; }
    }

    /// <summary>
    /// The pop-up of a planned-downtime procedure that records a physical SN: operator
    /// badge, old SN as recorded, new SN from the scanner (slide 37: 200 ms, nominal length,
    /// no Control key or right mouse button). An optional check button (e.g. "To MES Check")
    /// must pass before OK is enabled, and the OK action may still refuse - its message is
    /// shown here and the dialog stays open (S3.1: "an error message is displayed in the
    /// popup window").
    /// </summary>
    public partial class ProcedureDialog : Window
    {
        private readonly IAccessControlService _access;
        private readonly AccessLevel _minimumLevel;
        private readonly Func<string, IList<ProcedureField>> _fieldsFor;
        private readonly CardScan _badgeScan = new CardScan();
        private readonly Dictionary<ProcedureField, CardScan> _scans = new Dictionary<ProcedureField, CardScan>();
        private readonly Dictionary<ProcedureField, TextBox> _boxes = new Dictionary<ProcedureField, TextBox>();

        private IList<ProcedureField> _fields = new List<ProcedureField>();
        private ComboBox _gantryBox;
        private TextBox _operatorBox;
        private int _badgeCheck;
        private bool _checkPassed;
        private bool _busy;

        /// <param name="header">The red line: the procedure, e.g. "PD-04 Valve Change".</param>
        /// <param name="access">Checks the badge; null accepts any badge from the reader.</param>
        /// <param name="gantries">Offered for the operator to choose; null or empty shows no gantry row.</param>
        /// <param name="fieldsFor">The SNs to scan for the chosen gantry (called with null when there is no gantry row).</param>
        public ProcedureDialog(
            string header,
            IAccessControlService access,
            AccessLevel minimumLevel,
            IList<string> gantries,
            Func<string, IList<ProcedureField>> fieldsFor)
        {
            InitializeComponent();

            HeaderText.Text = header;
            _access = access;
            _minimumLevel = minimumLevel;
            _fieldsFor = fieldsFor;

            BuildRows(gantries);
            Loaded += (sender, e) => (_operatorBox ?? (UIElement)this).Focus();
        }

        /// <summary>Label of the check button; set to show it (e.g. "To MES Check").</summary>
        public string CheckLabel
        {
            get { return CheckButton.Content as string; }
            set
            {
                CheckButton.Content = value;
                CheckButton.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>Runs on the check button; returns why it failed, or null to enable OK.</summary>
        public Func<ProcedureDialog, Task<string>> CheckAction { get; set; }

        /// <summary>Runs on OK; returns why it failed (the dialog stays open), or null to close.</summary>
        public Func<ProcedureDialog, Task<string>> OkAction { get; set; }

        /// <summary>An extra line under the SNs, e.g. the valve SN the nozzle will be linked to.</summary>
        public string Info
        {
            get { return InfoText.Text; }
            set
            {
                InfoText.Text = value;
                InfoText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>True when at least one SN must be scanned but none in particular.</summary>
        public bool AtLeastOne { get; set; }

        public string OperatorId { get; private set; }

        public AccessCheckResult Operator { get; private set; }

        public string SelectedGantry
        {
            get { return _gantryBox != null ? _gantryBox.SelectedItem as string : null; }
        }

        public IList<ProcedureField> Fields
        {
            get { return _fields; }
        }

        /// <summary>The scanned value of a field, or null.</summary>
        public string ValueOf(string key)
        {
            ProcedureField field = _fields.FirstOrDefault(f => f.Key == key);
            return field != null ? field.Value : null;
        }

        /// <summary>Shows a message under the SNs, e.g. what MES answered.</summary>
        public void ShowMessage(string text, bool error)
        {
            MessageText.Text = text;
            MessageText.Foreground = error ? System.Windows.Media.Brushes.Firebrick : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A));
        }

        private void BuildRows(IList<string> gantries)
        {
            Body.RowDefinitions.Clear();
            Body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            Body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (gantries != null && gantries.Count > 0)
            {
                _gantryBox = new ComboBox { ItemsSource = gantries, Padding = new Thickness(6, 4, 6, 4) };
                _gantryBox.SelectionChanged += (sender, e) => RebuildFields();
                AddRow("Gantry:", _gantryBox);
            }

            _operatorBox = new TextBox { IsReadOnly = true, Padding = new Thickness(6, 4, 6, 4) };
            _operatorBox.PreviewTextInput += (sender, e) => { e.Handled = true; _badgeScan.Add(e.Text); };
            _operatorBox.PreviewKeyDown += OperatorBox_PreviewKeyDown;
            CardScan.BlockManualEntry(_operatorBox);
            AddRow("Operator:", _operatorBox);

            _fieldRowStart = Body.RowDefinitions.Count;

            if (_gantryBox != null)
                _gantryBox.SelectedIndex = 0;
            else
                RebuildFields();

            ShowMessage(_minimumLevel >= AccessLevel.Level2 && _access != null
                ? "Swipe a Level " + (int)_minimumLevel + " or higher ID card, then scan the SN."
                : "Swipe your ID card, then scan the SN.", false);
        }

        private int _fieldRowStart;

        private void RebuildFields()
        {
            // Drop the SN rows of the previous gantry.
            foreach (UIElement child in Body.Children.Cast<UIElement>().Where(c => Grid.GetRow(c) >= _fieldRowStart).ToList())
                Body.Children.Remove(child);
            while (Body.RowDefinitions.Count > _fieldRowStart)
                Body.RowDefinitions.RemoveAt(Body.RowDefinitions.Count - 1);

            _scans.Clear();
            _boxes.Clear();
            _checkPassed = false;

            _fields = _fieldsFor != null ? _fieldsFor(SelectedGantry) ?? new List<ProcedureField>() : new List<ProcedureField>();
            foreach (ProcedureField field in _fields)
            {
                field.Value = null;

                if (field.OldValue != null)
                    AddRow("Old " + field.Label + ":", new TextBlock { Text = string.IsNullOrWhiteSpace(field.OldValue) ? "(none recorded)" : field.OldValue, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

                var box = new TextBox { IsReadOnly = true, Padding = new Thickness(6, 4, 6, 4) };
                CardScan scan = CardScan.ForSerialNumber(field.MinimumLength, field.MaximumLength);
                ProcedureField current = field;
                box.PreviewTextInput += (sender, e) => { e.Handled = true; scan.Add(e.Text); };
                box.PreviewKeyDown += (sender, e) => SnBox_PreviewKeyDown(current, e);
                CardScan.BlockManualEntry(box);
                _scans[field] = scan;
                _boxes[field] = box;
                AddRow("New " + field.Label + ":", box);
            }

            UpdateOk();
        }

        private void AddRow(string label, FrameworkElement control)
        {
            int row = Body.RowDefinitions.Count;
            Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock { Text = label, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(text, row);
            Body.Children.Add(text);

            control.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            Body.Children.Add(control);
        }

        private void OperatorBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            e.Handled = true;
            OperatorId = null;
            Operator = null;
            _operatorBox.Text = string.Empty;
            _badgeCheck++;
            UpdateOk();

            string refused;
            string badge = _badgeScan.Finish(out refused);
            if (badge == null)
            {
                ShowMessage(refused ?? "Swipe your ID card.", refused != null);
                return;
            }

            if (_access == null)
            {
                AcceptBadge(badge, null, "Badge read. Scan the SN.");
                return;
            }

            var ignored = CheckBadgeAsync(badge, _badgeCheck);
        }

        private async Task CheckBadgeAsync(string badge, int check)
        {
            ShowMessage("Checking the badge...", false);

            AccessCheckResult result;
            try
            {
                result = await _access.CheckAccessAsync(badge);
            }
            catch (Exception ex)
            {
                result = AccessCheckResult.Deny("The badge could not be checked: " + ex.Message);
            }

            if (check != _badgeCheck || !IsLoaded)
                return;

            if (result == null || !result.Granted)
            {
                ShowMessage((result != null ? result.FailureReason : "Access denied.") + " Please swipe again.", true);
                return;
            }

            if (result.Level < _minimumLevel)
            {
                ShowMessage(result.Name + " is Level " + (int)result.Level + ". This needs Level " + (int)_minimumLevel + " or higher.", true);
                return;
            }

            AcceptBadge(badge, result, result.Name + " (Level " + (int)result.Level + "). Scan the SN.");
        }

        private void AcceptBadge(string badge, AccessCheckResult result, string message)
        {
            OperatorId = badge;
            Operator = result;
            _operatorBox.Text = result != null ? badge + "  " + result.Name : badge;
            ShowMessage(message, false);

            TextBox next = _boxes.Values.FirstOrDefault(b => string.IsNullOrEmpty(b.Text));
            if (next != null)
                next.Focus();

            UpdateOk();
        }

        private void SnBox_PreviewKeyDown(ProcedureField field, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            e.Handled = true;
            TextBox box = _boxes[field];
            field.Value = null;
            box.Text = string.Empty;
            _checkPassed = false;

            string refused;
            string value = _scans[field].Finish(out refused);
            if (value == null)
            {
                ShowMessage(refused ?? "Scan the " + field.Label + ".", refused != null);
                UpdateOk();
                return;
            }

            string invalid = field.Validate != null ? field.Validate(value) : null;
            if (invalid != null)
            {
                // Slide 37 / S3.1: "Software delete the character string in the dialog box".
                ShowMessage(invalid, true);
                UpdateOk();
                return;
            }

            field.Value = value;
            box.Text = value;
            ShowMessage(field.Label + " read.", false);

            TextBox next = _boxes.Where(p => p.Key.Value == null).Select(p => p.Value).FirstOrDefault();
            if (next != null)
                next.Focus();

            UpdateOk();
        }

        private bool Complete
        {
            get
            {
                if (string.IsNullOrWhiteSpace(OperatorId))
                    return false;

                if (AtLeastOne)
                    return _fields.Any(f => f.Value != null);

                return _fields.All(f => !f.Required || f.Value != null);
            }
        }

        private void UpdateOk()
        {
            bool needsCheck = CheckAction != null;
            CheckButton.IsEnabled = !_busy && Complete;
            OkButton.IsEnabled = !_busy && Complete && (!needsCheck || _checkPassed);
        }

        private async void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (CheckAction == null || !Complete)
                return;

            _busy = true;
            UpdateOk();
            ShowMessage("Checking...", false);

            string failed;
            try
            {
                failed = await CheckAction(this);
            }
            catch (Exception ex)
            {
                failed = ex.Message;
            }

            _busy = false;
            _checkPassed = failed == null;
            if (failed != null)
                ShowMessage(failed, true);
            UpdateOk();
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!OkButton.IsEnabled)
                return;

            if (OkAction != null)
            {
                _busy = true;
                UpdateOk();
                ShowMessage("Working...", false);

                string failed;
                try
                {
                    failed = await OkAction(this);
                }
                catch (Exception ex)
                {
                    failed = ex.Message;
                }

                _busy = false;
                if (failed != null)
                {
                    ShowMessage(failed, true);
                    UpdateOk();
                    return;
                }
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
