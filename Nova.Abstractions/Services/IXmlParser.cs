using System.Data;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    public interface IXmlParser
    {
        DataTable GenerateDataTableFromXml(string filePath, string requiredBaseNode, string nodeName);
        string GetRequiredNode(string filePath, string requiredNode);
        string GetAttributeValue(string filePath, string requiredNode, string attributeName);
        
        // Legacy XML.cs adaptations
        string[][] XmlDataToVariablesColumn(string filePath);
        double[][] XmlDataToVariablesColumnDouble(string filePath);
        string[][] XmlDataToVariablesRow(string filePath);
        List<List<string>> XmlDataToListColumnwise(string filePath);
        List<List<string>> XmlDataToListRowwise(string filePath);
        List<string[]> XmlDataToRowList(string filePath);
    }
}
