using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OfficeOpenXml;
using System.Text;

namespace Blazor_Bedrock.Infrastructure.ExternalApis;

public interface IDocumentProcessor
{
    Task<string> ExtractTextFromWordAsync(Stream fileStream);
    Task<string> ExtractTextFromExcelAsync(Stream fileStream);
    Task<string> ExtractTextAsync(Stream fileStream, string contentType);
}

public class DocumentProcessor : IDocumentProcessor
{
    public async Task<string> ExtractTextFromWordAsync(Stream fileStream)
    {
        var text = new StringBuilder();
        
        using (var wordDoc = WordprocessingDocument.Open(fileStream, false))
        {
            var body = wordDoc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    var paraText = paragraph.InnerText;
                    if (!string.IsNullOrWhiteSpace(paraText))
                    {
                        text.AppendLine(paraText);
                    }
                }
            }
        }
        
        return text.ToString();
    }

    public async Task<string> ExtractTextFromExcelAsync(Stream fileStream)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        var text = new StringBuilder();
        
        using (var package = new ExcelPackage(fileStream))
        {
            foreach (var worksheet in package.Workbook.Worksheets)
            {
                text.AppendLine($"Sheet: {worksheet.Name}");
                
                for (int row = worksheet.Dimension?.Start.Row ?? 1; 
                     row <= (worksheet.Dimension?.End.Row ?? 1); 
                     row++)
                {
                    var rowText = new List<string>();
                    for (int col = worksheet.Dimension?.Start.Column ?? 1; 
                         col <= (worksheet.Dimension?.End.Column ?? 1); 
                         col++)
                    {
                        var cellValue = worksheet.Cells[row, col].Value?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(cellValue))
                        {
                            rowText.Add(cellValue);
                        }
                    }
                    
                    if (rowText.Any())
                    {
                        text.AppendLine(string.Join(" | ", rowText));
                    }
                }
                
                text.AppendLine();
            }
        }
        
        return text.ToString();
    }

    public async Task<string> ExtractTextAsync(Stream fileStream, string contentType)
    {
        return contentType.ToLower() switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => await ExtractTextFromWordAsync(fileStream),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => await ExtractTextFromExcelAsync(fileStream),
            "application/msword" => await ExtractTextFromWordAsync(fileStream),
            "application/vnd.ms-excel" => await ExtractTextFromExcelAsync(fileStream),
            _ => throw new NotSupportedException($"Content type {contentType} is not supported")
        };
    }
}

