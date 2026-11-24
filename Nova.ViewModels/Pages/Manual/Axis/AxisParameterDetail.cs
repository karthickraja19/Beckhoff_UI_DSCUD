namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// One read-only parameter of the selected axis, shown on the Manual screen so the
    /// operator can see what the axis is configured with without leaving for
    /// Settings > Axis Parameters. Informational only — editing still belongs to the
    /// Settings table, which is the single place these values are written.
    /// </summary>
    public class AxisParameterDetail
    {
        public string Label { get; }
        public string Value { get; }

        public AxisParameterDetail(string label, string value)
        {
            Label = label;
            Value = string.IsNullOrWhiteSpace(value) ? "—" : value;
        }
    }
}
