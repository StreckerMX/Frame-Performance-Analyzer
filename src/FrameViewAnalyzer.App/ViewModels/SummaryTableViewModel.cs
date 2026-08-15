using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Read-only view state for a FrameView_Summary.csv table: columns, rows,
/// and numeric-aware header sorting. Sorting rebuilds the in-memory row
/// collection; the source document is never mutated.
/// </summary>
public partial class SummaryTableViewModel : ObservableObject
{
    private SummaryTableDocument _document;

    [ObservableProperty]
    private string _title = "FrameView Summary";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    private int? _sortColumn;
    private bool _sortAscending = true;

    public ObservableCollection<SummaryColumn> Columns { get; } = [];

    public ObservableCollection<string[]> Rows { get; } = [];

    public SummaryTableViewModel(SummaryTableDocument document)
    {
        _document = document;
        Title = $"FrameView Summary · {document.RowCount:N0} sessions";
        ApplyColumns();
        Rebuild();
    }

    /// <summary>Toggles a column sort (first click ascending).</summary>
    public void SortBy(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        if (_sortColumn == columnIndex)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = columnIndex;
            _sortAscending = true;
        }

        _document = SummaryTable.Sort(_document, columnIndex, _sortAscending);
        Rebuild();
    }

    private void ApplyColumns()
    {
        Columns.Clear();
        foreach (var column in _document.Columns)
        {
            Columns.Add(column);
        }
    }

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var row in _document.Rows)
        {
            Rows.Add(row);
        }

        var columns = Columns.Count;
        var name = _document.Rows.Length > 0 && _document.Rows[0].Length > 0
            ? _document.Rows[0][0]
            : string.Empty;
        SummaryText = $"{name} · {_document.RowCount:N0} sessions · {columns:N0} columns · select a header to sort";
    }
}
