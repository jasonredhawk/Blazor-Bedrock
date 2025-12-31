using Blazor_Bedrock.Services.Document;
using Blazor_Bedrock.Services.ChatGpt;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using System.Text;
using System.Linq;

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
        // Get ALL rows (maxRows = 0 means no limit) for chart creation
        var sheetDataList = await _documentService.GetStructuredDataAsync(request.DocumentId, userId, tenantId, maxRows: 0);

        // Find the sheet to use
        var sheetData = string.IsNullOrEmpty(request.SheetName)
            ? sheetDataList.FirstOrDefault()
            : sheetDataList.FirstOrDefault(s => s.SheetName.Equals(request.SheetName, StringComparison.OrdinalIgnoreCase));

        if (sheetData == null || !sheetData.Rows.Any())
        {
            throw new InvalidOperationException("No data found in the selected sheet.");
        }

        // Apply filters to data FIRST - this ensures only filtered data is used for charts
        var filteredData = ApplyFilters(sheetData, request.Filters);
        
        // If creating multiple charts, use grouping logic
        if (request.CreateMultipleCharts && !string.IsNullOrEmpty(request.GroupByColumn))
        {
            // Set grouping strategy if not already set
            if (request.GroupingStrategy == ChartGroupingStrategy.None)
            {
                // Auto-detect strategy based on whether GroupByColumn matches a filter column
                if (request.Filters.Any(f => f.IsActive && f.ColumnName.Equals(request.GroupByColumn, StringComparison.OrdinalIgnoreCase)))
                {
                    request.GroupingStrategy = ChartGroupingStrategy.ByFilterColumn;
                }
                else if (request.Configuration.XAxisColumns.Contains(request.GroupByColumn, StringComparer.OrdinalIgnoreCase))
                {
                    request.GroupingStrategy = ChartGroupingStrategy.ByXAxis;
                }
                else
                {
                    request.GroupingStrategy = ChartGroupingStrategy.Custom;
                }
            }
            
            return await CreateMultipleChartsAsync(filteredData, request, userId, tenantId);
        }
        
        // Single chart creation (existing logic) - uses filtered data
        return await CreateSingleChartAsync(filteredData, request, userId, tenantId);
    }

    private Infrastructure.ExternalApis.SheetData ApplyFilters(Infrastructure.ExternalApis.SheetData sheetData, List<ChartFilter> filters)
    {
        if (!filters.Any(f => f.IsActive))
        {
            return sheetData; // No filters, return original data
        }

        var headers = sheetData.Rows[0];
        var filteredRows = new List<List<string>> { headers }; // Always include header

        // Get active filters
        var activeFilters = filters.Where(f => f.IsActive).ToList();

        // Filter data rows
        for (int rowIndex = 1; rowIndex < sheetData.Rows.Count; rowIndex++)
        {
            var row = sheetData.Rows[rowIndex];
            bool includeRow = true;

            // Check each active filter
            foreach (var filter in activeFilters)
            {
                var columnIndex = headers.FindIndex(h => h.Equals(filter.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (columnIndex >= 0 && columnIndex < row.Count)
                {
                    var cellValue = row[columnIndex]?.Trim() ?? string.Empty;
                    if (!filter.SelectedValues.Contains(cellValue, StringComparer.OrdinalIgnoreCase))
                    {
                        includeRow = false;
                        break;
                    }
                }
            }

            if (includeRow)
            {
                filteredRows.Add(row);
            }
        }

        return new Infrastructure.ExternalApis.SheetData
        {
            SheetName = sheetData.SheetName,
            Rows = filteredRows
        };
    }

    private async Task<ChartCreationResult> CreateSingleChartAsync(Infrastructure.ExternalApis.SheetData sheetData, ChartCreationRequest request, string userId, int tenantId)
    {
        // Create Excel file with chart
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add("Chart Output");
        
        var dataAnalysis = await CreateChartInWorksheetAsync(worksheet, sheetData, request, userId, tenantId, "Chart Output", package);
        
        // Generate filename
        var document = await _documentService.GetDocumentByIdAsync(request.DocumentId, userId, tenantId);
        var baseFileName = Path.GetFileNameWithoutExtension(document?.FileName ?? "chart");
        var fileName = $"{baseFileName}_chart_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

        return new ChartCreationResult
        {
            ExcelFileBytes = package.GetAsByteArray(),
            DataAnalysis = dataAnalysis,
            FileName = fileName,
            ChartsCreated = 1,
            CreatedSheetNames = new List<string> { "Chart Output" }
        };
    }

    private async Task<ChartCreationResult> CreateMultipleChartsAsync(Infrastructure.ExternalApis.SheetData sheetData, ChartCreationRequest request, string userId, int tenantId)
    {
        if (sheetData.Rows.Count <= 1)
        {
            throw new InvalidOperationException("No data available after filtering.");
        }

        var headers = sheetData.Rows[0];
        string? groupByColumn = request.GroupByColumn;

        // Determine grouping column based on strategy
        if (string.IsNullOrEmpty(groupByColumn))
        {
            switch (request.GroupingStrategy)
            {
                case ChartGroupingStrategy.ByFilterColumn:
                    // Use the first active filter column
                    var firstFilter = request.Filters.FirstOrDefault(f => f.IsActive);
                    if (firstFilter != null)
                    {
                        groupByColumn = firstFilter.ColumnName;
                    }
                    break;
                case ChartGroupingStrategy.ByXAxis:
                    // Use the first X-axis column
                    if (request.Configuration.XAxisColumns.Any())
                    {
                        groupByColumn = request.Configuration.XAxisColumns.First();
                    }
                    break;
                case ChartGroupingStrategy.Custom:
                    // Use explicitly specified GroupByColumn
                    break;
                default:
                    // Fallback: use first filter column or first X-axis column
                    var fallbackFilter = request.Filters.FirstOrDefault(f => f.IsActive);
                    if (fallbackFilter != null)
                    {
                        groupByColumn = fallbackFilter.ColumnName;
                    }
                    else if (request.Configuration.XAxisColumns.Any())
                    {
                        groupByColumn = request.Configuration.XAxisColumns.First();
                    }
                    break;
            }
        }

        if (string.IsNullOrEmpty(groupByColumn))
        {
            throw new InvalidOperationException("Grouping column must be specified for multiple chart creation. Please select a column to group by.");
        }

        var groupByColumnIndex = headers.FindIndex(h => h.Equals(groupByColumn, StringComparison.OrdinalIgnoreCase));
        if (groupByColumnIndex < 0)
        {
            throw new InvalidOperationException($"Grouping column '{groupByColumn}' not found in data.");
        }

        // Group data by the specified column
        // Note: sheetData is already filtered, so we're only grouping the filtered data
        var groupedData = new Dictionary<string, List<List<string>>>();
        
        // Add header row to each group as we create them
        for (int rowIndex = 1; rowIndex < sheetData.Rows.Count; rowIndex++)
        {
            var row = sheetData.Rows[rowIndex];
            if (groupByColumnIndex < row.Count)
            {
                var groupKey = row[groupByColumnIndex]?.Trim() ?? "Unknown";
                if (!groupedData.ContainsKey(groupKey))
                {
                    groupedData[groupKey] = new List<List<string>> { headers };
                }
                groupedData[groupKey].Add(row);
            }
        }

        // Remove any groups that only have headers (no data)
        var groupsToRemove = groupedData.Where(g => g.Value.Count <= 1).Select(g => g.Key).ToList();
        foreach (var key in groupsToRemove)
        {
            groupedData.Remove(key);
        }

        if (!groupedData.Any())
        {
            throw new InvalidOperationException("No data groups found after filtering and grouping. Please check your filters and grouping column.");
        }

        // Create Excel package with multiple worksheets
        using var package = new ExcelPackage();
        var createdSheets = new List<string>();

        foreach (var group in groupedData)
        {
            if (group.Value.Count <= 1) continue; // Skip groups with only header

            var groupKey = group.Key;
            var groupSheetData = new Infrastructure.ExternalApis.SheetData
            {
                SheetName = groupKey,
                Rows = group.Value
            };

            // Create worksheet name (Excel has 31 character limit)
            var sheetName = SanitizeSheetName(groupKey);
            if (string.IsNullOrEmpty(sheetName))
            {
                sheetName = $"Chart {createdSheets.Count + 1}";
            }

            // Ensure uniqueness
            int suffix = 1;
            string originalSheetName = sheetName;
            while (package.Workbook.Worksheets.Any(ws => ws.Name == sheetName))
            {
                sheetName = $"{originalSheetName.Substring(0, Math.Min(originalSheetName.Length, 28))}_{suffix}";
                suffix++;
            }

            var worksheet = package.Workbook.Worksheets.Add(sheetName);
            createdSheets.Add(sheetName);

            // Create chart title with group information
            var originalTitle = request.Configuration.Title;
            request.Configuration.Title = string.IsNullOrEmpty(originalTitle)
                ? $"{groupKey}"
                : $"{originalTitle} - {groupKey}";

            await CreateChartInWorksheetAsync(worksheet, groupSheetData, request, userId, tenantId, sheetName, package);

            // Restore original title for next iteration
            request.Configuration.Title = originalTitle;
        }

        // Generate filename
        var document = await _documentService.GetDocumentByIdAsync(request.DocumentId, userId, tenantId);
        var baseFileName = Path.GetFileNameWithoutExtension(document?.FileName ?? "chart");
        var fileName = $"{baseFileName}_charts_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

        return new ChartCreationResult
        {
            ExcelFileBytes = package.GetAsByteArray(),
            FileName = fileName,
            ChartsCreated = createdSheets.Count,
            CreatedSheetNames = createdSheets
        };
    }

    private async Task<string?> CreateChartInWorksheetAsync(OfficeOpenXml.ExcelWorksheet worksheet, Infrastructure.ExternalApis.SheetData sheetData, ChartCreationRequest request, string userId, int tenantId, string sheetName, ExcelPackage? package = null)
    {

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
        
        // Calculate the last column used by data
        var headerRow = sheetData.Rows[0];
        int lastDataColumn = headerRow.Count; // 1-based column number
        
        // Position chart to the right of the data (leave 2 columns gap)
        int chartStartColumn = lastDataColumn + 2; // 1-based, but SetPosition uses 0-based, so subtract 1
        int chartStartRow = 1; // Start at row 1 to align with header

        // Get column indices for X and Y axes (header is at index 0)
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

        // Set chart position to the right of data (SetPosition uses 0-based indices)
        chart.SetPosition(chartStartRow - 1, 0, chartStartColumn - 1, 0);
        chart.SetSize(800, 400);
        
        // Calculate approximate chart width in columns (800 pixels / ~64 pixels per column = ~12-13 columns)
        // We'll use this to position the analysis
        int chartWidthColumns = 13;

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

        // Add data analysis if requested (for both single and multiple charts)
        string? dataAnalysis = null;
        if (request.Configuration.IncludeDataAnalysis && sheetData.Rows.Count > 1)
        {
            try
            {
                // Use ALL rows for analysis (not just preview)
                dataAnalysis = await AnalyzeDataAsync(sheetData.Rows.Skip(1).ToList(), headerRow, userId, tenantId);
                
                // Position analysis to the right of the chart (leave 2 columns gap)
                int analysisStartColumn = chartStartColumn + chartWidthColumns + 2; // 1-based column
                int analysisStartRow = 1; // Start at row 1 to align with header
                
                worksheet.Cells[analysisStartRow, analysisStartColumn].Value = "Data Analysis:";
                worksheet.Cells[analysisStartRow, analysisStartColumn].Style.Font.Bold = true;
                worksheet.Cells[analysisStartRow, analysisStartColumn].Style.Font.Size = 12;
                analysisStartRow++;

                var analysisLines = dataAnalysis.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in analysisLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        worksheet.Cells[analysisStartRow, analysisStartColumn].Value = line.Trim();
                        worksheet.Cells[analysisStartRow, analysisStartColumn].Style.WrapText = true;
                        // Set column width for analysis column to ensure text is readable
                        worksheet.Column(analysisStartColumn).Width = 50;
                        analysisStartRow++;
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

        // Return data analysis for caller to use
        return dataAnalysis;
    }

    private string SanitizeSheetName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Sheet1";

        // Excel sheet name restrictions: max 31 chars, no : \ / ? * [ ]
        var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());

        if (sanitized.Length > 31)
        {
            sanitized = sanitized.Substring(0, 31);
        }

        return string.IsNullOrEmpty(sanitized) ? "Sheet1" : sanitized;
    }

    public async Task<string> AnalyzeDataAsync(List<List<string>> data, List<string> headers, string userId, int tenantId, string? prompt = null)
    {
        // Convert data to a readable format for ChatGPT
        var dataText = new StringBuilder();
        dataText.AppendLine("Data Headers:");
        dataText.AppendLine(string.Join(", ", headers));
        dataText.AppendLine();
        
        // Use ALL rows for analysis, but limit to 500 rows for ChatGPT API (to avoid token limits)
        // If more than 500 rows, include summary statistics
        int totalRows = data.Count;
        int rowsToInclude = Math.Min(500, totalRows);
        
        dataText.AppendLine($"Data Rows: {totalRows} total rows (showing first {rowsToInclude} for detailed analysis):");
        
        for (int i = 0; i < rowsToInclude; i++)
        {
            dataText.AppendLine(string.Join(", ", data[i]));
        }
        
        if (totalRows > rowsToInclude)
        {
            dataText.AppendLine();
            dataText.AppendLine($"Note: Showing first {rowsToInclude} of {totalRows} total rows. Analysis is based on this sample.");
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
