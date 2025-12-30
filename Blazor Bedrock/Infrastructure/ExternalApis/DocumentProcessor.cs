using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OfficeOpenXml;
using System.Text;
using System.Globalization;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace Blazor_Bedrock.Infrastructure.ExternalApis;

public interface IDocumentProcessor
{
    Task<string> ExtractTextFromWordAsync(Stream fileStream);
    Task<string> ExtractTextFromExcelAsync(Stream fileStream);
    Task<string> ExtractTextFromCsvAsync(Stream fileStream);
    Task<string> ExtractTextFromPdfAsync(Stream fileStream);
    Task<string> ExtractTextAsync(Stream fileStream, string contentType);
    Task<List<SheetData>> ExtractStructuredDataFromExcelAsync(Stream fileStream, int maxRows = 100);
    Task<List<SheetData>> ExtractStructuredDataFromCsvAsync(Stream fileStream, int maxRows = 100);
}

public class SheetData
{
    public string SheetName { get; set; } = string.Empty;
    public List<List<string>> Rows { get; set; } = new();
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
        // Reset stream position if needed
        if (fileStream.CanSeek && fileStream.Position > 0)
        {
            fileStream.Position = 0;
        }
        
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

    public async Task<string> ExtractTextFromCsvAsync(Stream fileStream)
    {
        var text = new StringBuilder();
        
        // Reset stream position if needed
        if (fileStream.CanSeek && fileStream.Position > 0)
        {
            fileStream.Position = 0;
        }
        
        using (var reader = new StreamReader(fileStream, Encoding.UTF8, leaveOpen: true))
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                // Parse CSV line (handling quoted values)
                var values = ParseCsvLine(line);
                if (values.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    text.AppendLine(string.Join(" | ", values));
                }
            }
        }
        
        return text.ToString();
    }

    private List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var currentValue = new StringBuilder();
        bool inQuotes = false;
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentValue.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    // Toggle quote state
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // End of field
                values.Add(currentValue.ToString().Trim());
                currentValue.Clear();
            }
            else
            {
                currentValue.Append(c);
            }
        }
        
        // Add last value
        values.Add(currentValue.ToString().Trim());
        
        return values;
    }

    public async Task<List<SheetData>> ExtractStructuredDataFromExcelAsync(Stream fileStream, int maxRows = 100)
    {
        // Reset stream position if needed
        if (fileStream.CanSeek && fileStream.Position > 0)
        {
            fileStream.Position = 0;
        }
        
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        var sheets = new List<SheetData>();
        
        using (var package = new ExcelPackage(fileStream))
        {
            foreach (var worksheet in package.Workbook.Worksheets)
            {
                var sheetData = new SheetData
                {
                    SheetName = worksheet.Name,
                    Rows = new List<List<string>>()
                };
                
                if (worksheet.Dimension != null)
                {
                    var endRow = Math.Min(worksheet.Dimension.End.Row, worksheet.Dimension.Start.Row + maxRows - 1);
                    
                    for (int row = worksheet.Dimension.Start.Row; 
                         row <= endRow; 
                         row++)
                    {
                        var rowData = new List<string>();
                        for (int col = worksheet.Dimension.Start.Column; 
                             col <= worksheet.Dimension.End.Column; 
                             col++)
                        {
                            var cellValue = worksheet.Cells[row, col].Value?.ToString() ?? "";
                            rowData.Add(cellValue);
                        }
                        sheetData.Rows.Add(rowData);
                    }
                }
                
                sheets.Add(sheetData);
            }
        }
        
        return sheets;
    }

    public async Task<List<SheetData>> ExtractStructuredDataFromCsvAsync(Stream fileStream, int maxRows = 100)
    {
        // Reset stream position if needed
        if (fileStream.CanSeek && fileStream.Position > 0)
        {
            fileStream.Position = 0;
        }
        
        var sheetData = new SheetData
        {
            SheetName = "Sheet1",
            Rows = new List<List<string>>()
        };
        
        using (var reader = new StreamReader(fileStream, Encoding.UTF8, leaveOpen: true))
        {
            string? line;
            int rowCount = 0;
            while ((line = await reader.ReadLineAsync()) != null && rowCount < maxRows)
            {
                var values = ParseCsvLine(line);
                sheetData.Rows.Add(values);
                rowCount++;
            }
        }
        
        return new List<SheetData> { sheetData };
    }

    public async Task<string> ExtractTextFromPdfAsync(Stream fileStream)
    {
        var text = new StringBuilder();
        
        try
        {
            using (var pdfReader = new PdfReader(fileStream))
            using (var pdfDocument = new PdfDocument(pdfReader))
            {
                var numberOfPages = pdfDocument.GetNumberOfPages();
                
                for (int page = 1; page <= numberOfPages; page++)
                {
                    var strategy = new SimpleTextExtractionStrategy();
                    var pageText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(page), strategy);
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        text.AppendLine(pageText);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error extracting text from PDF: {ex.Message}", ex);
        }
        
        return text.ToString();
    }

    public async Task<string> ExtractTextAsync(Stream fileStream, string contentType)
    {
        // Reset stream position if needed
        if (fileStream.CanSeek && fileStream.Position > 0)
        {
            fileStream.Position = 0;
        }

        return contentType.ToLower() switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => await ExtractTextFromWordAsync(fileStream),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => await ExtractTextFromExcelAsync(fileStream),
            "application/msword" => await ExtractTextFromWordAsync(fileStream),
            "application/vnd.ms-excel" => await ExtractTextFromExcelAsync(fileStream),
            "text/csv" => await ExtractTextFromCsvAsync(fileStream),
            "application/csv" => await ExtractTextFromCsvAsync(fileStream),
            "application/pdf" => await ExtractTextFromPdfAsync(fileStream),
            _ => throw new NotSupportedException($"Content type {contentType} is not supported")
        };
    }
}

