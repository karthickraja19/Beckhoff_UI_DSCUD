namespace Nova.Abstractions.Data
{
    /// <summary>
    /// Marks a data source that generates its figures instead of reading recorded
    /// ones - the sources the Alarm and Data pages get when <c>SampleData=true</c>.
    /// </summary>
    /// <remarks>
    /// A page that shows figures from one of these puts a "sample data" marker on the
    /// screen, so an operator can never mistake a generated yield for a real one.
    /// </remarks>
    public interface ISampleDataSource
    {
    }
}
