using Blazor_Bedrock.Services.Document;
using Blazor_Bedrock.Services.ChatGpt;
using Blazor_Bedrock.Data;
using Blazor_Bedrock.Data.Models;
using Blazor_Bedrock.Services;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using System.Text;
using System.Linq;
using System.Text.Json;

namespace Blazor_Bedrock.Services.Chart;

public interface IChartService
{
    Task<ChartCreationResult> CreateChartAsync(ChartCreationRequest request, string userId, int tenantId);
    Task<string> AnalyzeDataAsync(List<List<string>> data, List<string> headers, ChartCreationRequest request, string userId, int tenantId, string? prompt = null);
    Task<int> SaveChartAsync(ChartCreationRequest request, string name, string? description, string userId, int tenantId, int? savedChartId = null);
    Task<List<SavedChart>> GetSavedChartsAsync(string userId, int tenantId);
    Task<SavedChart?> GetSavedChartByIdAsync(int id, string userId, int tenantId);
    Task<bool> DeleteSavedChartAsync(int id, string userId, int tenantId);
    Task<ChartCreationRequest?> LoadSavedChartAsync(int id, string userId, int tenantId);
}

public class ChartService : IChartService
{
    private readonly IDocumentService _documentService;
    private readonly IChatGptService _chatGptService;
    private readonly ApplicationDbContext _context;
    private readonly IDatabaseSyncService _dbSync;
    private readonly IPromptService _promptService;

    public ChartService(IDocumentService documentService, IChatGptService chatGptService, ApplicationDbContext context, IDatabaseSyncService dbSync, IPromptService promptService)
    {
        _documentService = documentService;
        _chatGptService = chatGptService;
        _context = context;
        _dbSync = dbSync;
        _promptService = promptService;
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
        
        // Apply sorting after filtering
        var sortedData = ApplySorting(filteredData, request.Sorts);
        
        // If creating multiple charts, use grouping logic
        // Check both GroupByColumns (new multi-column support) and GroupByColumn (backward compatibility)
        bool hasGroupingColumns = (request.GroupByColumns != null && request.GroupByColumns.Any()) || !string.IsNullOrEmpty(request.GroupByColumn);
        
        if (request.CreateMultipleCharts && hasGroupingColumns)
        {
            // If GroupByColumns is empty but GroupByColumn is set, convert to GroupByColumns
            if ((request.GroupByColumns == null || !request.GroupByColumns.Any()) && !string.IsNullOrEmpty(request.GroupByColumn))
            {
                request.GroupByColumns = new List<string> { request.GroupByColumn };
            }
            
            // Auto-populate GroupByColumns from filter columns if not explicitly set
            if (request.GroupByColumns == null || !request.GroupByColumns.Any())
            {
                var activeFilterColumns = request.Filters.Where(f => f.IsActive).Select(f => f.ColumnName).ToList();
                if (activeFilterColumns.Any())
                {
                    request.GroupByColumns = activeFilterColumns;
                }
            }
            
            return await CreateMultipleChartsAsync(sortedData, request, userId, tenantId);
        }
        
        // Single chart creation (existing logic) - uses filtered and sorted data
        return await CreateSingleChartAsync(sortedData, request, userId, tenantId);
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

    private Infrastructure.ExternalApis.SheetData ApplySorting(Infrastructure.ExternalApis.SheetData sheetData, List<ChartSort> sorts)
    {
        if (!sorts.Any())
        {
            return sheetData; // No sorting, return original data
        }

        var headers = sheetData.Rows[0];
        var dataRows = sheetData.Rows.Skip(1).ToList();

        IOrderedEnumerable<List<string>>? orderedRows = null;
        bool firstSort = true;

        foreach (var sort in sorts.OrderBy(s => s.SortOrder))
        {
            var columnIndex = headers.FindIndex(h => h.Equals(sort.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (columnIndex < 0) continue;

            object GetSortValue(List<string> row)
            {
                if (columnIndex >= row.Count)
                    return string.Empty;

                var cellValue = row[columnIndex]?.Trim() ?? string.Empty;
                
                // Try to parse as numeric for proper numeric sorting
                if (double.TryParse(cellValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numValue))
                    return numValue;
                
                // Try to parse as date for proper date sorting
                if (DateTime.TryParse(cellValue, out DateTime dateValue))
                    return dateValue;
                
                // String comparison
                return cellValue;
            }

            if (firstSort)
            {
                orderedRows = sort.Direction == SortDirection.Ascending
                    ? dataRows.OrderBy(GetSortValue)
                    : dataRows.OrderByDescending(GetSortValue);
                firstSort = false;
            }
            else
            {
                orderedRows = sort.Direction == SortDirection.Ascending
                    ? orderedRows!.ThenBy(GetSortValue)
                    : orderedRows!.ThenByDescending(GetSortValue);
            }
        }

        var sortedDataRows = orderedRows?.ToList() ?? dataRows;
        var result = new List<List<string>> { headers };
        result.AddRange(sortedDataRows);

        return new Infrastructure.ExternalApis.SheetData
        {
            SheetName = sheetData.SheetName,
            Rows = result
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
        List<string> groupByColumns = new List<string>();

        // Determine grouping columns - support both single and multi-column grouping
        if (request.GroupByColumns != null && request.GroupByColumns.Any())
        {
            // Use multi-column grouping
            groupByColumns = request.GroupByColumns.ToList();
        }
        else if (!string.IsNullOrEmpty(request.GroupByColumn))
        {
            // Use single column grouping (backward compatibility)
            groupByColumns = new List<string> { request.GroupByColumn };
        }
        else
        {
            // Determine grouping column based on strategy
            string? groupByColumn = null;
            switch (request.GroupingStrategy)
            {
                case ChartGroupingStrategy.ByFilterColumn:
                    // Use active filter columns
                    var activeFilters = request.Filters.Where(f => f.IsActive).ToList();
                    if (activeFilters.Any())
                    {
                        groupByColumns = activeFilters.Select(f => f.ColumnName).ToList();
                    }
                    break;
                case ChartGroupingStrategy.ByXAxis:
                    // Use the X-axis column
                    if (request.Configuration.XAxisColumns.Any())
                    {
                        groupByColumns = request.Configuration.XAxisColumns.ToList();
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
                        groupByColumns = new List<string> { fallbackFilter.ColumnName };
                    }
                    else if (request.Configuration.XAxisColumns.Any())
                    {
                        groupByColumns = new List<string> { request.Configuration.XAxisColumns.First() };
                    }
                    break;
            }
        }

        if (!groupByColumns.Any())
        {
            throw new InvalidOperationException("Grouping column(s) must be specified for multiple chart creation. Please select column(s) to group by.");
        }

        // Get indices of grouping columns
        var groupByColumnIndices = groupByColumns
            .Select(colName => headers.FindIndex(h => h.Equals(colName, StringComparison.OrdinalIgnoreCase)))
            .Where(idx => idx >= 0)
            .ToList();

        if (!groupByColumnIndices.Any() || groupByColumnIndices.Count != groupByColumns.Count)
        {
            var missingColumns = groupByColumns.Where(col => !headers.Any(h => h.Equals(col, StringComparison.OrdinalIgnoreCase)));
            throw new InvalidOperationException($"One or more grouping columns not found in data: {string.Join(", ", missingColumns)}");
        }

        // Group data by the specified columns (creating composite keys)
        // Note: sheetData is already filtered, so we're only grouping the filtered data
        var groupedData = new Dictionary<string, List<List<string>>>();
        
        for (int rowIndex = 1; rowIndex < sheetData.Rows.Count; rowIndex++)
        {
            var row = sheetData.Rows[rowIndex];
            
            // Create composite key from all grouping columns
            var keyParts = new List<string>();
            bool validRow = true;
            foreach (var colIndex in groupByColumnIndices)
            {
                if (colIndex < row.Count)
                {
                    keyParts.Add(row[colIndex]?.Trim() ?? "Unknown");
                }
                else
                {
                    validRow = false;
                    break;
                }
            }

            if (!validRow) continue;

            var groupKey = string.Join(" + ", keyParts);
            
            if (!groupedData.ContainsKey(groupKey))
            {
                groupedData[groupKey] = new List<List<string>> { headers };
            }
            groupedData[groupKey].Add(row);
        }

        // Remove any groups that only have headers (no data)
        var groupsToRemove = groupedData.Where(g => g.Value.Count <= 1).Select(g => g.Key).ToList();
        foreach (var key in groupsToRemove)
        {
            groupedData.Remove(key);
        }

        if (!groupedData.Any())
        {
            throw new InvalidOperationException("No data groups found after filtering and grouping. Please check your filters and grouping columns.");
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

        // Determine data start row (skip header row, so start from index 1)
        int dataStartRowIndex = request.Configuration.DataStartRow ?? 1;
        if (dataStartRowIndex < 1)
            dataStartRowIndex = 1;
        if (sheetData.Rows.Any() && dataStartRowIndex >= sheetData.Rows.Count)
            dataStartRowIndex = 1;

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

        // Get column indices for X, Y, and Variable columns
        var xAxisColumnIndex = GetColumnIndex(headerRow, request.Configuration.XAxisColumns.FirstOrDefault());
        var yAxisColumnIndex = GetColumnIndex(headerRow, request.Configuration.YAxisColumns.FirstOrDefault());
        var variableColumnIndex = GetColumnIndex(headerRow, request.Configuration.VariableColumn);
        
        // Check if we're using the new VariableColumn approach (grouping by variable column)
        bool useVariableColumnGrouping = variableColumnIndex >= 0 && !string.IsNullOrEmpty(request.Configuration.VariableColumn);
        
        // Create chart (will be configured in if/else blocks below)
        var chartType = ConvertToEPPlusChartType(request.Configuration.ChartType);
        OfficeOpenXml.Drawing.Chart.ExcelChart chart;
        int chartWidthColumns = 13;
        
        if (useVariableColumnGrouping)
        {
            // New approach: Group by variable column - each unique value becomes a series
            if (xAxisColumnIndex < 0 || yAxisColumnIndex < 0)
            {
                throw new InvalidOperationException("Invalid column selection. Please select X-axis (date), Y-axis (value), and Variable column.");
            }
            
            // Extract data rows
            // dataStartRowIndex is 1-based (1 = first data row after header)
            // sheetData.Rows[0] is header, sheetData.Rows[1] is first data row
            // So if dataStartRowIndex = 1, we want rows starting from index 1
            // If dataStartRowIndex = 2, we want rows starting from index 2, etc.
            var dataRows = sheetData.Rows
                .Skip(dataStartRowIndex) // Skip header (index 0) + any additional rows specified by dataStartRowIndex
                .Where(row => row != null && 
                             variableColumnIndex < row.Count && 
                             yAxisColumnIndex < row.Count && 
                             xAxisColumnIndex < row.Count &&
                             !string.IsNullOrWhiteSpace(row[variableColumnIndex])) // Ensure variable column has a value
                .ToList();
            
            if (!dataRows.Any())
            {
                throw new InvalidOperationException("No data rows found after filtering. Please check your data start row setting and data filters.");
            }
            
            // Group data by variable column values
            var groupedByVariable = dataRows
                .GroupBy(row => row[variableColumnIndex]?.Trim() ?? "Unknown")
                .ToList();
            
            if (!groupedByVariable.Any())
            {
                throw new InvalidOperationException("No data found after grouping by variable column.");
            }
            
            // Create transformed data structure for chart
            // Get all unique X-axis values (dates) from the actual data rows
            var allXValues = dataRows
                .Where(row => xAxisColumnIndex < row.Count && !string.IsNullOrWhiteSpace(row[xAxisColumnIndex]))
                .Select(row => row[xAxisColumnIndex]?.Trim() ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => 
                {
                    // Try to sort as date if possible, otherwise as string
                    if (DateTime.TryParse(x, out DateTime dateValue))
                        return dateValue.Ticks.ToString();
                    return x;
                })
                .ToList();
            
            // Create chart
            chart = worksheet.Drawings.AddChart(request.Configuration.Title, chartType);
            chart.Title.Text = request.Configuration.Title;
            chart.Title.Font.Size = 14;
            chart.Title.Font.Bold = true;
            
            // Set chart position
            chart.SetPosition(chartStartRow - 1, 0, chartStartColumn - 1, 0);
            chart.SetSize(800, 400);
            
            // Create transformed data table for chart (pivot-like structure)
            // Write transformed data starting after original data
            int transformedDataStartCol = lastDataColumn + chartWidthColumns + 5;
            int transformedDataStartRow = dataStartRowInWorksheet;
            
            // Write header row for transformed data
            worksheet.Cells[transformedDataStartRow - 1, transformedDataStartCol].Value = headerRow[xAxisColumnIndex]; // X-axis header
            int colOffset = 1;
            var variableNames = groupedByVariable.Select(g => g.Key).OrderBy(v => v).ToList();
            foreach (var varName in variableNames)
            {
                worksheet.Cells[transformedDataStartRow - 1, transformedDataStartCol + colOffset].Value = varName;
                colOffset++;
            }
            
            // Write transformed data rows
            int transformedDataEndRow = transformedDataStartRow;
            foreach (var xValue in allXValues)
            {
                // Write X-axis value - try to parse as date for proper formatting
                if (DateTime.TryParse(xValue, out DateTime xDateValue))
                {
                    worksheet.Cells[transformedDataEndRow, transformedDataStartCol].Value = xDateValue;
                    worksheet.Cells[transformedDataEndRow, transformedDataStartCol].Style.Numberformat.Format = "mm/dd/yyyy";
                }
                else
                {
                    worksheet.Cells[transformedDataEndRow, transformedDataStartCol].Value = xValue;
                }
                
                colOffset = 1;
                foreach (var varName in variableNames)
                {
                    // Find the Y value for this X value and variable
                    // Use case-insensitive comparison for both variable name and X value
                    var variableGroup = groupedByVariable.FirstOrDefault(g => 
                        g.Key.Trim().Equals(varName.Trim(), StringComparison.OrdinalIgnoreCase));
                    
                    if (variableGroup != null)
                    {
                        // Normalize X value for comparison (trim and handle case)
                        var normalizedXValue = xValue.Trim();
                        var matchingRow = variableGroup.FirstOrDefault(row => 
                        {
                            if (xAxisColumnIndex >= row.Count)
                                return false;
                            
                            var rowXValue = row[xAxisColumnIndex]?.Trim() ?? "";
                            
                            // Try exact match first
                            if (rowXValue.Equals(normalizedXValue, StringComparison.OrdinalIgnoreCase))
                                return true;
                            
                            // Try date comparison if both are dates
                            if (DateTime.TryParse(rowXValue, out DateTime rowDate) && 
                                DateTime.TryParse(normalizedXValue, out DateTime targetDate))
                            {
                                return rowDate.Date == targetDate.Date;
                            }
                            
                            return false;
                        });
                        
                        if (matchingRow != null && yAxisColumnIndex < matchingRow.Count)
                        {
                            var yValue = matchingRow[yAxisColumnIndex]?.Trim() ?? "";
                            if (!string.IsNullOrWhiteSpace(yValue))
                            {
                                if (double.TryParse(yValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numValue))
                                {
                                    worksheet.Cells[transformedDataEndRow, transformedDataStartCol + colOffset].Value = numValue;
                                }
                                else
                                {
                                    // Try to parse as date and convert to numeric if it's a date
                                    if (DateTime.TryParse(yValue, out DateTime dateValue))
                                    {
                                        worksheet.Cells[transformedDataEndRow, transformedDataStartCol + colOffset].Value = dateValue.ToOADate();
                                    }
                                    else
                                    {
                                        // If it's not a number or date, try to extract number from string
                                        var numberMatch = System.Text.RegularExpressions.Regex.Match(yValue, @"-?\d+\.?\d*");
                                        if (numberMatch.Success && double.TryParse(numberMatch.Value, out double extractedNum))
                                        {
                                            worksheet.Cells[transformedDataEndRow, transformedDataStartCol + colOffset].Value = extractedNum;
                                        }
                                        else
                                        {
                                            worksheet.Cells[transformedDataEndRow, transformedDataStartCol + colOffset].Value = yValue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    // Leave empty if no matching data (Excel will show as empty/zero)
                    
                    colOffset++;
                }
                transformedDataEndRow++;
            }
            
            // Create chart using transformed data
            var xAxisRange = worksheet.Cells[transformedDataStartRow, transformedDataStartCol, transformedDataEndRow - 1, transformedDataStartCol];
            
            // Add series - one for each unique variable value
            colOffset = 1;
            foreach (var varName in variableNames)
            {
                var seriesRange = worksheet.Cells[transformedDataStartRow, transformedDataStartCol + colOffset, transformedDataEndRow - 1, transformedDataStartCol + colOffset];
                var series = chart.Series.Add(seriesRange, xAxisRange);
                series.Header = varName;
                colOffset++;
            }
            
            // Store chartWidthColumns for analysis positioning
            chartWidthColumns = 13;
        }
        else
        {
            // Legacy approach: Use VariableColumns or YAxisColumns as direct series
            List<int> seriesColumnIndices;
            if (request.Configuration.VariableColumns != null && request.Configuration.VariableColumns.Any())
            {
                seriesColumnIndices = request.Configuration.VariableColumns
                    .Select(colName => GetColumnIndex(headerRow, colName))
                    .Where(idx => idx >= 0)
                    .ToList();
            }
            else
            {
                seriesColumnIndices = request.Configuration.YAxisColumns
                    .Select(colName => GetColumnIndex(headerRow, colName))
                    .Where(idx => idx >= 0)
                    .ToList();
            }

            if (xAxisColumnIndex < 0 || !seriesColumnIndices.Any())
            {
                throw new InvalidOperationException("Invalid column selection for chart axes. Please select an X-axis column and at least one variable column.");
            }

            // Create chart
            chart = worksheet.Drawings.AddChart(request.Configuration.Title, chartType);
            chart.Title.Text = request.Configuration.Title;
            chart.Title.Font.Size = 14;
            chart.Title.Font.Bold = true;

            // Set chart position
            chart.SetPosition(chartStartRow - 1, 0, chartStartColumn - 1, 0);
            chart.SetSize(800, 400);

            // Add X-axis data
            var xAxisRange = worksheet.Cells[dataStartRowInWorksheet, xAxisColumnIndex + 1, dataEndRow, xAxisColumnIndex + 1];
            
            // Add series - one for each variable column
            foreach (var seriesColIndex in seriesColumnIndices)
            {
                var seriesRange = worksheet.Cells[dataStartRowInWorksheet, seriesColIndex + 1, dataEndRow, seriesColIndex + 1];
                var series = chart.Series.Add(seriesRange, xAxisRange);
                series.Header = seriesColIndex < headerRow.Count ? headerRow[seriesColIndex] : $"Series {seriesColumnIndices.IndexOf(seriesColIndex) + 1}";
            }
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
                // Use filtered data for analysis - only rows and columns relevant to the chart
                dataAnalysis = await AnalyzeDataAsync(sheetData.Rows.Skip(1).ToList(), headerRow, request, userId, tenantId);
                
                // Position analysis to the right of the chart (leave 2 columns gap)
                int analysisStartColumn = chartStartColumn + chartWidthColumns + 2; // 1-based column
                int analysisStartRow = 1; // Start at row 1 to align with header
                
                // Write color-coded analysis
                WriteColorCodedAnalysis(worksheet, dataAnalysis, analysisStartRow, analysisStartColumn);
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

    public async Task<string> AnalyzeDataAsync(List<List<string>> data, List<string> headers, ChartCreationRequest request, string userId, int tenantId, string? prompt = null)
    {
        // Filter to only include columns used in the chart (x-axis, y-axis, and variable column)
        var columnsToInclude = new List<string>();
        
        // Add X-axis column
        if (request.Configuration.XAxisColumns != null && request.Configuration.XAxisColumns.Any())
        {
            columnsToInclude.AddRange(request.Configuration.XAxisColumns);
        }
        
        // Add Y-axis column
        if (request.Configuration.YAxisColumns != null && request.Configuration.YAxisColumns.Any())
        {
            columnsToInclude.AddRange(request.Configuration.YAxisColumns);
        }
        
        // Add variable column (if using new VariableColumn approach)
        if (!string.IsNullOrEmpty(request.Configuration.VariableColumn))
        {
            columnsToInclude.Add(request.Configuration.VariableColumn);
        }
        // Legacy: Add variable columns (old approach)
        else if (request.Configuration.VariableColumns != null && request.Configuration.VariableColumns.Any())
        {
            columnsToInclude.AddRange(request.Configuration.VariableColumns);
        }
        
        // Remove duplicates while preserving order
        columnsToInclude = columnsToInclude.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        
        if (!columnsToInclude.Any())
        {
            // Fallback: if no columns specified, use all columns (shouldn't happen but safety check)
            columnsToInclude = headers.ToList();
        }
        
        // Get indices of columns to include
        var columnIndices = columnsToInclude
            .Select(colName => headers.FindIndex(h => h.Equals(colName, StringComparison.OrdinalIgnoreCase)))
            .Where(idx => idx >= 0)
            .ToList();
        
        if (!columnIndices.Any())
        {
            return "Unable to analyze data: specified columns not found in the data.";
        }
        
        // Filter data to only include the relevant columns
        var filteredHeaders = columnIndices.Select(idx => headers[idx]).ToList();
        var filteredData = data
            .Select(row => columnIndices
                .Where(idx => idx < row.Count)
                .Select(idx => row[idx])
                .ToList())
            .Where(row => row.Count == columnIndices.Count) // Only include complete rows
            .ToList();
        
        // Convert filtered data to a readable format for ChatGPT
        var dataText = new StringBuilder();
        dataText.AppendLine("Data Headers (chart columns only):");
        dataText.AppendLine(string.Join(", ", filteredHeaders));
        dataText.AppendLine();
        dataText.AppendLine("Note: This analysis includes only the columns used in the chart (x-axis and y-axis columns) and only the rows that match the applied filters.");
        dataText.AppendLine();
        
        // Use filtered data for analysis, but limit to 500 rows for ChatGPT API (to avoid token limits)
        int totalRows = filteredData.Count;
        int rowsToInclude = Math.Min(500, totalRows);
        
        dataText.AppendLine($"Data Rows: {totalRows} total filtered rows (showing first {rowsToInclude} for detailed analysis):");
        
        for (int i = 0; i < rowsToInclude; i++)
        {
            dataText.AppendLine(string.Join(", ", filteredData[i]));
        }
        
        if (totalRows > rowsToInclude)
        {
            dataText.AppendLine();
            dataText.AppendLine($"Note: Showing first {rowsToInclude} of {totalRows} total filtered rows. Analysis is based on this sample.");
        }

        // Get custom prompt if specified, otherwise use default
        string? customPrompt = prompt;
        if (request.AnalysisPromptId.HasValue && request.AnalysisPromptId.Value > 0)
        {
            try
            {
                var selectedPrompt = await _promptService.GetPromptByIdAsync(request.AnalysisPromptId.Value, tenantId);
                if (selectedPrompt != null && selectedPrompt.PromptType == PromptType.Chart)
                {
                    customPrompt = selectedPrompt.PromptText;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading custom prompt: {ex.Message}");
                // Fall back to default prompt
            }
        }

        var analysisPrompt = customPrompt ?? @"Provide a comprehensive data analysis with the following sections:

## EXECUTIVE SUMMARY
A brief 2-3 sentence overview of the dataset and key findings.

## DATA OVERVIEW
- Total records analyzed
- Date/time range (if applicable)
- Key dimensions and metrics present
- Data quality assessment (completeness, outliers, anomalies)

## KEY TRENDS AND PATTERNS
Identify and describe:
- Temporal trends (if time-series data)
- Seasonal patterns or cyclical behavior
- Distribution patterns
- Relationships between variables
- Significant changes or shifts

## STATISTICAL SUMMARY
For numeric columns, provide:
- Mean, median, mode
- Min, max, range
- Standard deviation and variance
- Percentiles (25th, 50th, 75th, 90th, 95th if applicable)
- Skewness and kurtosis if relevant

## INSIGHTS AND OBSERVATIONS
- Notable data points (highest, lowest, unusual values)
- Correlations or relationships discovered
- Anomalies or outliers and their significance
- Patterns that stand out
- Contextual observations

## RECOMMENDATIONS
- Actionable insights based on the analysis
- Areas requiring attention or further investigation
- Potential next steps or follow-up analysis

Format each section clearly with section headers. Be detailed and specific, citing actual values from the data where relevant.";

        var fullPrompt = $"{analysisPrompt}\n\n{dataText}";

        try
        {
            return await _chatGptService.SendChatMessageAsync(userId, tenantId, fullPrompt, null, "You are a data analyst assistant specializing in data visualization and statistical analysis.");
        }
        catch
        {
            // Fallback to basic analysis if ChatGPT is unavailable
            return GenerateBasicAnalysis(filteredData, filteredHeaders);
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

    private void WriteColorCodedAnalysis(OfficeOpenXml.ExcelWorksheet worksheet, string analysis, int startRow, int startColumn)
    {
        // Define color scheme for different analysis sections
        var sectionHeaderColor = System.Drawing.Color.FromArgb(68, 114, 196); // Blue
        var summaryColor = System.Drawing.Color.FromArgb(237, 125, 49); // Orange
        var statisticsColor = System.Drawing.Color.FromArgb(112, 173, 71); // Green
        var insightsColor = System.Drawing.Color.FromArgb(255, 192, 0); // Yellow
        var recommendationsColor = System.Drawing.Color.FromArgb(163, 73, 164); // Purple
        var defaultColor = System.Drawing.Color.White; // White for regular text
        
        var analysisLines = analysis.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        int currentRow = startRow;
        
        // Title
        worksheet.Cells[currentRow, startColumn].Value = "Data Analysis";
        worksheet.Cells[currentRow, startColumn].Style.Font.Bold = true;
        worksheet.Cells[currentRow, startColumn].Style.Font.Size = 14;
        worksheet.Cells[currentRow, startColumn].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        worksheet.Cells[currentRow, startColumn].Style.Fill.BackgroundColor.SetColor(sectionHeaderColor);
        worksheet.Cells[currentRow, startColumn].Style.Font.Color.SetColor(System.Drawing.Color.White);
        currentRow++;
        
        // Set column width (no wrapping, so we need adequate width)
        worksheet.Column(startColumn).Width = 60;
        
        foreach (var line in analysisLines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;
            
            // Determine color based on content
            System.Drawing.Color backgroundColor = defaultColor;
            System.Drawing.Color textColor = System.Drawing.Color.Black;
            bool isBold = false;
            int fontSize = 10;
            
            var upperLine = trimmedLine.ToUpperInvariant();
            
            // Section headers (lines starting with ##, or containing specific keywords)
            if (trimmedLine.StartsWith("##") || 
                (upperLine.Contains("EXECUTIVE SUMMARY") && upperLine.Length < 50) ||
                (upperLine.Contains("DATA OVERVIEW") && upperLine.Length < 50) ||
                (upperLine.Contains("KEY TRENDS") && upperLine.Length < 50) ||
                (upperLine.Contains("STATISTICAL SUMMARY") && upperLine.Length < 50) ||
                (upperLine.Contains("STATISTICS") && upperLine.Length < 50) ||
                (upperLine.Contains("INSIGHTS") && upperLine.Length < 50) ||
                (upperLine.Contains("OBSERVATIONS") && upperLine.Length < 50) ||
                (upperLine.Contains("RECOMMENDATIONS") && upperLine.Length < 50) ||
                (upperLine.Contains("SUMMARY") && upperLine.Length < 50 && !upperLine.Contains("EXECUTIVE")))
            {
                backgroundColor = sectionHeaderColor;
                textColor = System.Drawing.Color.White;
                isBold = true;
                fontSize = 12;
                // Remove markdown formatting
                trimmedLine = trimmedLine.Replace("##", "").Trim();
            }
            else if (upperLine.Contains("MEAN") || upperLine.Contains("MEDIAN") || upperLine.Contains("AVERAGE") ||
                     upperLine.Contains("STANDARD DEVIATION") || upperLine.Contains("MIN") || upperLine.Contains("MAX") ||
                     upperLine.Contains("PERCENTILE") || upperLine.Contains("VARIANCE") || upperLine.Contains("SKEWNESS"))
            {
                backgroundColor = statisticsColor;
                textColor = System.Drawing.Color.Black;
                isBold = false;
            }
            else if (trimmedLine.StartsWith("•") || trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*") ||
                     (upperLine.Contains("INSIGHT") || upperLine.Contains("OBSERVE") || upperLine.Contains("PATTERN") ||
                      upperLine.Contains("TREND") || upperLine.Contains("CORRELATION")))
            {
                backgroundColor = insightsColor;
                textColor = System.Drawing.Color.Black;
                isBold = false;
            }
            else if (upperLine.Contains("RECOMMEND") || upperLine.Contains("ACTION") || upperLine.Contains("SUGGEST") ||
                     upperLine.Contains("SHOULD") || upperLine.Contains("CONSIDER"))
            {
                backgroundColor = recommendationsColor;
                textColor = System.Drawing.Color.White;
                isBold = false;
            }
            else if (trimmedLine.Length < 200 && (upperLine.Contains("TOTAL") || upperLine.Contains("COUNT") || 
                     upperLine.Contains("RECORDS") || upperLine.Contains("RANGE") || upperLine.Contains("OVERVIEW")))
            {
                backgroundColor = summaryColor;
                textColor = System.Drawing.Color.Black;
                isBold = false;
            }
            
            // Write the line
            worksheet.Cells[currentRow, startColumn].Value = trimmedLine;
            worksheet.Cells[currentRow, startColumn].Style.Font.Bold = isBold;
            worksheet.Cells[currentRow, startColumn].Style.Font.Size = fontSize;
            worksheet.Cells[currentRow, startColumn].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            worksheet.Cells[currentRow, startColumn].Style.Fill.BackgroundColor.SetColor(backgroundColor);
            worksheet.Cells[currentRow, startColumn].Style.Font.Color.SetColor(textColor);
            // No word wrapping - let text overflow to adjacent cells horizontally if needed
            worksheet.Cells[currentRow, startColumn].Style.WrapText = false;
            
            currentRow++;
        }
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

    public async Task<int> SaveChartAsync(ChartCreationRequest request, string name, string? description, string userId, int tenantId, int? savedChartId = null)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };
        var configurationJson = JsonSerializer.Serialize(request, jsonOptions);

        return await _dbSync.ExecuteAsync(async () =>
        {
            SavedChart savedChart;
            
            if (savedChartId.HasValue && savedChartId.Value > 0)
            {
                // Update existing chart
                savedChart = await _context.SavedCharts
                    .FirstOrDefaultAsync(c => c.Id == savedChartId.Value && c.UserId == userId && c.TenantId == tenantId);
                
                if (savedChart == null)
                    throw new InvalidOperationException("Saved chart not found.");
                
                savedChart.Name = name;
                savedChart.Description = description;
                savedChart.ConfigurationJson = configurationJson;
                savedChart.UpdatedAt = DateTime.UtcNow;
                savedChart.LastUsedAt = DateTime.UtcNow;
            }
            else
            {
                // Create new chart
                savedChart = new SavedChart
                {
                    UserId = userId,
                    TenantId = tenantId,
                    Name = name,
                    Description = description,
                    ConfigurationJson = configurationJson,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow
                };
                _context.SavedCharts.Add(savedChart);
            }

            await _context.SaveChangesAsync();
            return savedChart.Id;
        });
    }

    public async Task<List<SavedChart>> GetSavedChartsAsync(string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            return await _context.SavedCharts
                .Where(c => c.UserId == userId && c.TenantId == tenantId)
                .OrderByDescending(c => c.LastUsedAt ?? c.UpdatedAt)
                .ToListAsync();
        });
    }

    public async Task<SavedChart?> GetSavedChartByIdAsync(int id, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var chart = await _context.SavedCharts
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.TenantId == tenantId);
            
            if (chart != null)
            {
                // Update LastUsedAt
                chart.LastUsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            
            return chart;
        });
    }

    public async Task<bool> DeleteSavedChartAsync(int id, string userId, int tenantId)
    {
        return await _dbSync.ExecuteAsync(async () =>
        {
            var chart = await _context.SavedCharts
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.TenantId == tenantId);
            
            if (chart == null)
                return false;

            _context.SavedCharts.Remove(chart);
            await _context.SaveChangesAsync();
            return true;
        });
    }

    public async Task<ChartCreationRequest?> LoadSavedChartAsync(int id, string userId, int tenantId)
    {
        var savedChart = await GetSavedChartByIdAsync(id, userId, tenantId);
        if (savedChart == null)
            return null;

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<ChartCreationRequest>(savedChart.ConfigurationJson, jsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deserializing saved chart: {ex.Message}");
            return null;
        }
    }
}
