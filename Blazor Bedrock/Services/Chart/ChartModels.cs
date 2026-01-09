namespace Blazor_Bedrock.Services.Chart;

public enum ChartType
{
    Line,
    LineMarkers,
    ColumnClustered,
    ColumnStacked,
    BarClustered,
    BarStacked,
    Pie,
    Scatter,
    Area,
    AreaStacked
}

public enum ChartGroupingStrategy
{
    None, // Single chart, no grouping
    ByFilterColumn, // Group by one of the filter columns (e.g., by station, by concentration)
    ByXAxis, // Group by X-axis values
    Custom // User-defined grouping
}

public class ChartFilter
{
    public string ColumnName { get; set; } = string.Empty;
    public List<string> SelectedValues { get; set; } = new();
    public bool IsActive => SelectedValues.Any();
}

public enum SortDirection
{
    Ascending = 0,
    Descending = 1
}

public class ChartSort
{
    public string ColumnName { get; set; } = string.Empty;
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
    public int SortOrder { get; set; } = 0; // Order in which to apply sorts (0 = first, 1 = second, etc.)
}

public class ChartConfiguration
{
    public string Title { get; set; } = string.Empty;
    public string? XAxisTitle { get; set; }
    public string? YAxisTitle { get; set; }
    public ChartType ChartType { get; set; } = ChartType.LineMarkers;
    public bool ShowLegend { get; set; } = true;
    public string? LegendPosition { get; set; } = "Right"; // Right, Left, Top, Bottom
    public List<string> XAxisColumns { get; set; } = new();
    public List<string> YAxisColumns { get; set; } = new(); // Legacy: kept for backward compatibility
    public List<string> VariableColumns { get; set; } = new(); // Legacy: kept for backward compatibility
    public string? VariableColumn { get; set; } // Column name (e.g., "variable") that contains values like "nitrogen", "phosphorus" - each unique value becomes a series
    public int? DataStartRow { get; set; } // 1-based row number where data starts
    public bool IncludeDataAnalysis { get; set; } = true;
}

public class ChartCreationRequest
{
    public int DocumentId { get; set; }
    public string? SheetName { get; set; }
    public ChartConfiguration Configuration { get; set; } = new();
    
    // Advanced filtering and grouping
    public List<ChartFilter> Filters { get; set; } = new();
    public List<ChartSort> Sorts { get; set; } = new(); // Sort order for data
    public ChartGroupingStrategy GroupingStrategy { get; set; } = ChartGroupingStrategy.None;
    public List<string> GroupByColumns { get; set; } = new(); // Columns to group by (supports multi-column grouping)
    public string? GroupByColumn { get; set; } // Single column to group by (backward compatibility)
    public bool CreateMultipleCharts { get; set; } = false;
    
    // AI Analysis
    public int? AnalysisPromptId { get; set; } // Selected ChatGPT prompt for analysis
}

public class ChartCreationResult
{
    public byte[] ExcelFileBytes { get; set; } = Array.Empty<byte>();
    public string? DataAnalysis { get; set; }
    public string FileName { get; set; } = "chart.xlsx";
    public int ChartsCreated { get; set; } = 1;
    public List<string> CreatedSheetNames { get; set; } = new();
}
