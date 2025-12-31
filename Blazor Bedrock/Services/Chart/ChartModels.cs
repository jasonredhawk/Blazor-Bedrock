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

public class ChartConfiguration
{
    public string Title { get; set; } = string.Empty;
    public string? XAxisTitle { get; set; }
    public string? YAxisTitle { get; set; }
    public ChartType ChartType { get; set; } = ChartType.LineMarkers;
    public bool ShowLegend { get; set; } = true;
    public string? LegendPosition { get; set; } = "Right"; // Right, Left, Top, Bottom
    public List<string> XAxisColumns { get; set; } = new();
    public List<string> YAxisColumns { get; set; } = new();
    public int? DataStartRow { get; set; } // 1-based row number where data starts
    public bool IncludeDataAnalysis { get; set; } = true;
}

public class ChartCreationRequest
{
    public int DocumentId { get; set; }
    public string? SheetName { get; set; }
    public ChartConfiguration Configuration { get; set; } = new();
}

public class ChartCreationResult
{
    public byte[] ExcelFileBytes { get; set; } = Array.Empty<byte>();
    public string? DataAnalysis { get; set; }
    public string FileName { get; set; } = "chart.xlsx";
}
