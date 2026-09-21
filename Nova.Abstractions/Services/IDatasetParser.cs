using System.Data;

namespace Nova.Abstractions.Services
{
    public interface IDatasetParser
    {
        DataTable GetDataFromXML(string filepath);
        DataTable GetDataFromCSV(string filepath);
        DataTable GetDataFromJSON(string filepath);
        DataTable GetDataFromXLS(string filepath);
        DataTable GetDataFromYAML(string filepath);
        DataTable GetDataFromINI(string filePath);
    }
}
