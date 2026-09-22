using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// One bar on the Home page's Error Statistics panel (Audio SW DFM v5.4 slide 43).
    /// </summary>
    /// <remarks>
    /// <see cref="Ratio"/> rather than a pixel width: the panel is a plain
    /// ItemsControl of native bars, not a chart, so the view scales each bar against
    /// the widest one with a Grid star column. That keeps the DFM's right-hand value
    /// labels ("1,928") exactly where the slide puts them, which a chart control would
    /// fight over.
    /// </remarks>
    public sealed class ErrorStatRow
    {
        public string Label { get; }

        public int Count { get; }

        /// <summary>Bar length as a fraction of the largest count on the panel, 0..1.</summary>
        public double Ratio { get; }

        /// <summary>Thousands-separated, as drawn on the slide.</summary>
        public string CountText { get; }

        /// <summary>
        /// Fraction of the track the LONGEST bar is allowed to fill.
        /// </summary>
        /// <remarks>
        /// The value label lives in the leftover column after the bar, so a bar at a
        /// full 1.0 leaves the label zero width and it vanishes - which is exactly what
        /// happens to the biggest number on the panel, the one most worth reading.
        /// Holding the longest bar short of the end keeps every label on screen and
        /// costs nothing: these bars are read against each other, not against the
        /// track.
        /// </remarks>
        private const double LongestBarFill = 0.88;

        public ErrorStatRow(string label, int count, int maxCount)
        {
            Label = label;
            Count = count;
            CountText = count.ToString("N0");

            // A panel where every category is zero would divide by zero; it renders as
            // all-empty bars instead, which is the truthful picture.
            Ratio = maxCount > 0 ? (double)count / maxCount * LongestBarFill : 0d;
        }
    }

    /// <summary>
    /// One row of the Home page Parameter Dashboard (slide 43) or the Config page Spec
    /// Limits panel (slide 58).
    /// </summary>
    /// <remarks>
    /// Slide 43: "Follow AME definition on critical params. Show OOS params at top of
    /// list in red", "Use N/A in LSL/USL if there is no spec limit", "Show current glue
    /// SN with no limits (dispenser only)". So a row has to represent three cases -
    /// in-spec, out-of-spec, and no-limit-therefore-never-out-of-spec - and
    /// <see cref="IsOutOfSpec"/> is computed rather than set, so a limitless row can
    /// never be flagged red by accident.
    /// </remarks>
    public sealed class ParameterRow
    {
        private const string NoLimit = "N/A";

        public string KeyName { get; }

        public string Value { get; }

        /// <summary>Lower spec limit, or "N/A".</summary>
        public string LowerLimitText { get; }

        /// <summary>Upper spec limit, or "N/A".</summary>
        public string UpperLimitText { get; }

        /// <summary>
        /// True only when a limit exists AND the value is numeric AND it falls outside.
        /// A non-numeric value (a glue serial number) is never out of spec.
        /// </summary>
        public bool IsOutOfSpec { get; }

        public ParameterRow(string keyName, string value, double? lowerLimit, double? upperLimit)
        {
            KeyName = keyName;
            Value = value;
            LowerLimitText = lowerLimit.HasValue ? lowerLimit.Value.ToString("0.###") : NoLimit;
            UpperLimitText = upperLimit.HasValue ? upperLimit.Value.ToString("0.###") : NoLimit;

            double numeric;
            bool isNumeric = double.TryParse(value, out numeric);

            IsOutOfSpec = isNumeric &&
                          ((lowerLimit.HasValue && numeric < lowerLimit.Value) ||
                           (upperLimit.HasValue && numeric > upperLimit.Value));
        }
    }

    /// <summary>
    /// One row of the Config page's Spec Limits panel (slide 58), editable after Edit.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ParameterRow"/>, which compares a reading against fixed
    /// limits. Limits are kept as the text the file stores, so "0.50" comes back as
    /// "0.50" rather than being reformatted. Blank means no limit and shows as "N/A"
    /// (slide 43: "Use N/A in LSL/USL if there is no spec limit"); typing "N/A" means
    /// the same.
    /// </remarks>
    public partial class SpecLimitRow : ObservableObject
    {
        private const string NoLimit = "N/A";

        /// <summary>Position of the row in the limits file; Save writes it back there.</summary>
        public int Index { get; }

        public string Parameter { get; }

        public string Unit { get; }

        /// <summary>"Parameter (unit)", as the panel's Key Name column.</summary>
        public string KeyName { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LowerLimitText))]
        private string _lowerLimit;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UpperLimitText))]
        private string _upperLimit;

        /// <summary>Why the row cannot be saved, or null. Set by <see cref="Validate"/>.</summary>
        [ObservableProperty]
        private string _error;

        public string LowerLimitText => Display(LowerLimit);

        public string UpperLimitText => Display(UpperLimit);

        public SpecLimitRow(int index, string parameter, string unit, string lowerLimit, string upperLimit)
        {
            Index = index;
            Parameter = parameter ?? string.Empty;
            Unit = unit ?? string.Empty;
            KeyName = (string.IsNullOrWhiteSpace(Parameter) ? "(unnamed)" : Parameter.Trim())
                      + (string.IsNullOrWhiteSpace(Unit) ? string.Empty : " (" + Unit.Trim() + ")");
            _lowerLimit = ToStored(lowerLimit);
            _upperLimit = ToStored(upperLimit);
        }

        /// <summary>
        /// Checks both limits are numbers (or blank) and that LSL is not above USL.
        /// Sets <see cref="Error"/> and returns true when the row can be saved.
        /// </summary>
        public bool Validate()
        {
            double lower, upper;
            bool hasLower = TryParse(LowerLimit, out lower);
            bool hasUpper = TryParse(UpperLimit, out upper);

            if (!IsBlank(LowerLimit) && !hasLower)
                Error = "LSL is not a number.";
            else if (!IsBlank(UpperLimit) && !hasUpper)
                Error = "USL is not a number.";
            else if (hasLower && hasUpper && lower > upper)
                Error = "LSL is greater than USL.";
            else
                Error = null;

            return Error == null;
        }

        /// <summary>The text to store: trimmed, and blank for no limit.</summary>
        public static string ToStored(string text)
        {
            return IsBlank(text) ? string.Empty : text.Trim();
        }

        private static bool IsBlank(string text)
        {
            return string.IsNullOrWhiteSpace(text) ||
                   string.Equals(text.Trim(), NoLimit, StringComparison.OrdinalIgnoreCase);
        }

        private static string Display(string text)
        {
            return IsBlank(text) ? NoLimit : text.Trim();
        }

        /// <summary>
        /// Invariant culture, because the file stores "0.50" whatever the panel PC's
        /// regional settings are.
        /// </summary>
        private static bool TryParse(string text, out double value)
        {
            value = 0;
            return !IsBlank(text) &&
                   double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>
    /// One row of the Setting page's Hardware Setting dialog (slide 63): a subsystem and
    /// the type chosen for it from a closed list.
    /// </summary>
    public partial class HardwareSettingRow : ObservableObject
    {
        public string Subsystem { get; }

        public IReadOnlyList<string> Types { get; }

        [ObservableProperty]
        private string _selectedType;

        public HardwareSettingRow(string subsystem, IReadOnlyList<string> types, string selectedType)
        {
            Subsystem = subsystem;
            Types = types;
            _selectedType = selectedType;
        }
    }

    /// <summary>
    /// One labelled figure in a summary block - the Home page I/O Summary and the Data
    /// page's I/O and Tossing summaries (slides 43, 59).
    /// </summary>
    /// <remarks>
    /// <see cref="Value"/> is observable and the label is not, because these rows are
    /// built once and then updated in place.
    /// <see cref="Nova.Abstractions.Services.IMachineTelemetryService.TelemetryUpdated"/>
    /// is raised on a background thread, and its contract is explicit that a handler
    /// may only set scalar INotifyPropertyChanged properties - WPF marshals those,
    /// but it does not marshal a change to a bound ObservableCollection. Clearing and
    /// refilling a collection from that handler throws
    /// NotSupportedException the moment the collection has a live binding, so the
    /// row object is the stable thing and only its value moves.
    /// </remarks>
    public partial class SummaryRow : ObservableObject
    {
        public string Label { get; }

        [ObservableProperty]
        private string _value;

        public SummaryRow(string label, string value)
        {
            Label = label;
            _value = value;
        }
    }

    /// <summary>
    /// One row of the Alarm page's Downtime Statistic (Top 10) table, and one slice of
    /// the pie beside it (slide 56).
    /// </summary>
    public sealed class DowntimeStatRow
    {
        /// <summary>Zero-based rank, which selects the DFM's Top 10 colour.</summary>
        public int Rank { get; }

        public string ErrorName { get; }

        /// <summary>Accumulated downtime in minutes, as the table's "Total Time (Min)".</summary>
        public double TotalMinutes { get; }

        public int Occurrences { get; }

        public string TotalMinutesText { get; }

        public string OccurrencesText { get; }

        public DowntimeStatRow(int rank, string errorName, double totalMinutes, int occurrences)
        {
            Rank = rank;
            ErrorName = errorName;
            TotalMinutes = totalMinutes;
            Occurrences = occurrences;
            TotalMinutesText = totalMinutes.ToString("0.#");
            OccurrencesText = occurrences.ToString("N0");
        }
    }

    /// <summary>
    /// One alarm record in the Alarm page's history table (slide 56).
    /// </summary>
    /// <remarks>
    /// <see cref="TimeInStateText"/> reads "Ongoing" for an alarm that has not cleared,
    /// which the slide shows explicitly in its first row - a still-standing alarm must
    /// not be reported as a zero-minute one.
    /// </remarks>
    public sealed class AlarmHistoryRow
    {
        public string Date { get; }

        public string StartTime { get; }

        public string EndTime { get; }

        public string TimeInStateText { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }

        public string ErrorDetail { get; }

        public AlarmHistoryRow(
            string date, string startTime, string endTime,
            string timeInStateText, string errorCode, string errorMessage, string errorDetail)
        {
            Date = date;
            StartTime = startTime;
            EndTime = endTime;
            TimeInStateText = timeInStateText;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            ErrorDetail = errorDetail;
        }
    }

    /// <summary>
    /// One produced unit in the Data page's table (slides 59, 60).
    /// </summary>
    public sealed class UnitDataRow
    {
        public string Date { get; }
        public string Shift { get; }
        public string UnitSn { get; }
        public string ComponentSn { get; }
        public string StartTime { get; }
        public string EndTime { get; }
        public string PassText { get; }
        public string CycleTimeText { get; }
        public string HiveStateText { get; }
        public string TossingText { get; }

        /// <summary>Drives the red row tint for a failing unit.</summary>
        public bool IsFail { get; }

        public UnitDataRow(
            string date, string shift, string unitSn, string componentSn,
            string startTime, string endTime, bool? pass,
            double? cycleTimeSeconds, int hiveState, int tossing)
        {
            Date = date;
            Shift = shift;
            UnitSn = unitSn;
            ComponentSn = componentSn;
            StartTime = startTime;
            EndTime = endTime;

            // A unit still in process has no verdict yet. Blank, not "FALSE" - the
            // slide's first row shows exactly that case.
            PassText = pass.HasValue ? (pass.Value ? "TRUE" : "FALSE") : string.Empty;
            IsFail = pass.HasValue && !pass.Value;

            CycleTimeText = cycleTimeSeconds.HasValue ? cycleTimeSeconds.Value.ToString("0.00") : string.Empty;
            HiveStateText = hiveState.ToString();
            TossingText = tossing.ToString();
        }
    }

    /// <summary>
    /// One CCD tile on the Vision page (slide 61).
    /// </summary>
    /// <remarks>
    /// Selection is on the row rather than in the page so the 3x2 grid can bind
    /// <c>IsSelected</c> directly; the page still owns which one is selected and clears
    /// the others.
    /// </remarks>
    public partial class CcdTile : ObservableObject
    {
        public string Name { get; }

        /// <summary>Capture metadata the detail pane lists under "Information".</summary>
        public System.Collections.Generic.IReadOnlyList<SummaryRow> Information { get; }

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// Set once a real frame is available. Until a vision adapter exists this stays
        /// false and the tile draws its placeholder, rather than showing an empty frame
        /// that looks like a camera fault.
        /// </summary>
        [ObservableProperty]
        private bool _hasImage;

        public CcdTile(string name, System.Collections.Generic.IReadOnlyList<SummaryRow> information)
        {
            Name = name;
            Information = information;
        }
    }

    /// <summary>
    /// One connection indicator under the Home page station card - the sensor and
    /// service links the DFM draws as "HIVE Connected / MES Connected / PDCA
    /// Disconnected" (slide 43).
    /// </summary>
    public partial class LinkStatusRow : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty]
        private bool _isConnected;

        public string StatusText => Name + (IsConnected ? " Connected" : " Disconnected");

        partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(StatusText));

        public LinkStatusRow(string name, bool isConnected)
        {
            Name = name;
            _isConnected = isConnected;
        }
    }
}
