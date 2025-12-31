using Blazor_Bedrock.Services.Document;
using Blazor_Bedrock.Services.ChatGpt;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using System.Text;

namespace Blazor_Bedrock.Services.Chart;

public interface IChartService
{
    Task<ChartCreationResult> CreateChartAsync(ChartCreationRequest request, string userId, int tenantId);
    Task<string> AnalyzeDataAsync(List<List<string>> data, List<string> headers, string userId, int tenantId, string? prompt = null);
}

public class ChartService : IChartService
{
    private readonly IDocumentService _documentService;
    private readonly IChatGptService _chatGptService;

    public ChartService(IDocumentService documentService, IChatGptService chatGptService)
    {
        _documentService = documentService;
        _chatGptService = chatGptService;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    public async Task<ChartCreationResult> CreateChartAsync(ChartCreationRequest request, string userId, int tenantId)
    {
        // Get document file
        var documentFile = await _documentService.GetDocumentFileWithMetadataAsync(request.DocumentId, userId, tenantId);
        
        using var inputStream = new MemoryStream(documentFile.FileContent);
        var sheetDataList = await _documentService.GetStructuredDataAsync(request.DocumentId, userId, tenantId);

        // Find the sheet to use
        var sheetData = string.IsNullOrEmpty(request.SheetName)
            ? sheetDataList.FirstOrDefault()
            : sheetDataList.FirstOrDefault(s => s.SheetName.Equals(request.SheetName, StringComparison.OrdinalIgnoreCase));

        if (sheetData == null || !sheetData.Rows.Any())
        {
            throw new InvalidOperationException("No data found in the selected sheet.");
        }

        // Create Excel file with chart
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Chart Output");

        // Copy original data to the new worksheet
        int currentRow = 1;
        if (sheetData.Rows.Any())
        {
            // Write headers
            var headers = sheetData.Rows[0];
            for (int col = 0; col < headers.Count; col++)
            {
                worksheet.Cells[currentRow, col + 1].Value = headers[col];
                worksheet.Cells[currentRow, col + 1].Style.Font.Bold = true;
                worksheet.Cells[currentRow, col + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                worksheet.Cells[currentRow, col + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
            }
            currentRow++;

            // Determine data start row (skip header row, so start from index 1)
            int dataStartRowIndex = request.Configuration.DataStartRow ?? 1;
            if (dataStartRowIndex < 1)
                dataStartRowIndex = 1;
            if (dataStartRowIndex >= sheetData.Rows.Count)
                dataStartRowIndex = 1;

            // Write data rows (skip header row, so start from index 1)
            for (int rowIndex = dataStartRowIndex; rowIndex < sheetData.Rows.Count; rowIndex++)
            {
                var row = sheetData.Rows[rowIndex];
                for (int col = 0; col < row.Count; col++)
                {
                    // Try to parse as number, otherwise store as text
                    if (double.TryParse(row[col], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numValue))
                    {
                        worksheet.Cells[currentRow, col + 1].Value = numValue;
                    }
                    else
                    {
                        worksheet.Cells[currentRow, col + 1].Value = row[col];
                    }
                }
                currentRow++;
            }
        }

        int dataEndRow = currentRow - 1;
        int dataStartRowInWorksheet = 2; // Row 1 is header, data starts at row 2
        int chartStartRow = currentRow + 2;

        // Get column indices for X and Y axes (header is at index 0)
        var headerRow = sheetData.Rows[0];
        var xAxisColumnIndex = GetColumnIndex(headerRow, request.Configuration.XAxisColumns.FirstOrDefault());
        var yAxisColumnIndices = request.Configuration.YAxisColumns
            .Select(colName => GetColumnIndex(headerRow, colName))
            .Where(idx => idx >= 0)
            .ToList();


        if (xAxisColumnIndex < 0 || !yAxisColumnIndices.Any())
        {
            throw new InvalidOperationException("Invalid column selection for chart axes.");
        }

        // Create chart
        var chartType = ConvertToEPPlusChartType(request.Configuration.ChartType);
        var chart = worksheet.Drawings.AddChart(request.Configuration.Title, chartType);
        chart.Title.Text = request.Configuration.Title;
        chart.Title.Font.Size = 14;
        chart.Title.Font.Bold = true;

        // Set chart position
        chart.SetPosition(chartStartRow, 0, 0, 0);
        chart.SetSize(800, 400);

        // Add X-axis data (categories) - data starts at row 2 (row 1 is header)
        var xAxisRange = worksheet.Cells[dataStartRowInWorksheet, xAxisColumnIndex + 1, dataEndRow, xAxisColumnIndex + 1];
        
        // Add Y-axis series
        foreach (var yAxisColIndex in yAxisColumnIndices)
        {
            var yAxisRange = worksheet.Cells[dataStartRowInWorksheet, yAxisColIndex + 1, dataEndRow, yAxisColIndex + 1];
            var series = chart.Series.Add(yAxisRange, xAxisRange);
            
            var headerIndex = yAxisColIndex < headerRow.Count ? yAxisColIndex : yAxisColIndex;
            series.Header = headerIndex < headerRow.Count ? headerRow[headerIndex] : $"Series {yAxisColumnIndices.IndexOf(yAxisColIndex) + 1}";
        }

        // Configure axes
        if (!string.IsNullOrEmpty(request.Configuration.XAxisTitle))
        {
            chart.XAxis.Title.Text = request.Configuration.XAxisTitle;
            chart.XAxis.Title.Font.Bold = true;
        }

        if (!string.IsNullOrEmpty(request.Configuration.YAxisTitle))
        {
            chart.YAxis.Title.Text = request.Configuration.YAxisTitle;
            chart.YAxis.Title.Font.Bold = true;
        }

        // Configure legend
        chart.Legend.Position = ConvertLegendPosition(request.Configuration.LegendPosition ?? "Right");
        chart.Legend.Font.Size = 10;

        // Add data analysis if requested
        string? dataAnalysis = null;
        if (request.Configuration.IncludeDataAnalysis && sheetData.Rows.Count > 1)
        {
            try
            {
                dataAnalysis = await AnalyzeDataAsync(sheetData.Rows.Skip(1).ToList(), headerRow, userId, tenantId);
                
                // Add analysis to worksheet
                int analysisRow = chartStartRow + 25;
                worksheet.Cells[analysisRow, 1].Value = "Data Analysis:";
                worksheet.Cells[analysisRow, 1].Style.Font.Bold = true;
                worksheet.Cells[analysisRow, 1].Style.Font.Size = 12;
                analysisRow++;

                var analysisLines = dataAnalysis.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in analysisLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        worksheet.Cells[analysisRow, 1].Value = line.Trim();
                        worksheet.Cells[analysisRow, 1].Style.WrapText = true;
                        analysisRow++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail chart creation
                Console.WriteLine($"Error generating data analysis: {ex.Message}");
            }
        }

        // Auto-fit columns
        worksheet.Cells.AutoFitColumns();

        // Generate filename
        var document = await _documentService.GetDocumentByIdAsync(request.DocumentId, userId, tenantId);
        var baseFileName = Path.GetFileNameWithoutExtension(document?.FileName ?? "chart");
        var fileName = $"{baseFileName}_chart_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

        // Save to byte array
        var result = new ChartCreationResult
        {
            ExcelFileBytes = package.GetAsByteArray(),
            DataAnalysis = dataAnalysis,
            FileName = fileName
        };

        return result;
    }

    public async Task<string> AnalyzeDataAsync(List<List<string>> data, List<string> headers, string userId, int tenantId, string? prompt = null)
    {
        // Convert data to a readable format for ChatGPT
        var dataText = new StringBuilder();
        dataText.AppendLine("Data Headers:");
        dataText.AppendLine(string.Join(", ", headers));
        dataText.AppendLine();
        dataText.AppendLine("Data Rows (first 100 rows):");
        
        int rowCount = Math.Min(100, data.Count);
        for (int i = 0; i < rowCount; i++)
        {
            dataText.AppendLine(string.Join(", ", data[i]));
        }

        var analysisPrompt = prompt ?? @"Analyze this data and provide:
1. Key trends and patterns
2. Summary statistics (if numerical data is present)
3. Notable insights or observations
4. Any recommendations based on the data

Be concise but comprehensive. Use bullet points for clarity.";

        var fullPrompt = $"{analysisPrompt}\n\n{dataText}";

        try
        {
            return await _chatGptService.SendChatMessageAsync(userId, tenantId, fullPrompt, null, "You are a data analyst assistant specializing in data visualization and statistical analysis.");
        }
        catch
        {
            // Fallback to basic analysis if ChatGPT is unavailable
            return GenerateBasicAnalysis(data, headers);
        }
    }

    private string GenerateBasicAnalysis(List<List<string>> data, List<string> headers)
    {
        var analysis = new StringBuilder();
        analysis.AppendLine("Data Analysis Summary:");
        analysis.AppendLine($"• Total rows: {data.Count}");
        analysis.AppendLine($"• Total columns: {headers.Count}");
        analysis.AppendLine($"• Column names: {string.Join(", ", headers)}");
        
        // Try to identify numeric columns and calculate basic stats
        for (int colIndex = 0; colIndex < headers.Count && colIndex < (data.FirstOrDefault()?.Count ?? 0); colIndex++)
        {
            var numericValues = data
                .Select(row => colIndex < row.Count ? row[colIndex] : null)
                .Where(val => !string.IsNullOrEmpty(val) && double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                .Select(val => double.Parse(val!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture))
                .ToList();

            if (numericValues.Any())
            {
                analysis.AppendLine();
                analysis.AppendLine($"{headers[colIndex]} Statistics:");
                analysis.AppendLine($"  • Count: {numericValues.Count}");
                analysis.AppendLine($"  • Average: {numericValues.Average():F2}");
                analysis.AppendLine($"  • Min: {numericValues.Min():F2}");
                analysis.AppendLine($"  • Max: {numericValues.Max():F2}");
                if (numericValues.Count > 1)
                {
                    var variance = numericValues.Select(x => Math.Pow(x - numericValues.Average(), 2)).Average();
                    analysis.AppendLine($"  • Standard Deviation: {Math.Sqrt(variance):F2}");
                }
            }
        }

        return analysis.ToString();
    }

    private int GetColumnIndex(List<string> headers, string? columnName)
    {
        if (string.IsNullOrEmpty(columnName))
            return -1;

        // Try exact match first
        var index = headers.FindIndex(h => h.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            return index;

        // Try partial match
        index = headers.FindIndex(h => h.Contains(columnName, StringComparison.OrdinalIgnoreCase));
        return index;
    }

    private eChartType ConvertToEPPlusChartType(ChartType chartType)
    {
        return chartType switch
        {
            ChartType.Line => eChartType.Line,
            ChartType.LineMarkers => eChartType.LineMarkers,
            ChartType.ColumnClustered => eChartType.ColumnClustered,
            ChartType.ColumnStacked => eChartType.ColumnStacked,
            ChartType.BarClustered => eChartType.BarClustered,
            ChartType.BarStacked => eChartType.BarStacked,
            ChartType.Pie => eChartType.Pie,
            ChartType.Scatter => eChartType.XYScatter,
            ChartType.Area => eChartType.Area,
            ChartType.AreaStacked => eChartType.AreaStacked,
            _ => eChartType.LineMarkers
        };
    }

    private eLegendPosition ConvertLegendPosition(string position)
    {
        return position.ToLower() switch
        {
            "left" => eLegendPosition.Left,
            "right" => eLegendPosition.Right,
            "top" => eLegendPosition.Top,
            "bottom" => eLegendPosition.Bottom,
            _ => eLegendPosition.Right
        };
    }
}
