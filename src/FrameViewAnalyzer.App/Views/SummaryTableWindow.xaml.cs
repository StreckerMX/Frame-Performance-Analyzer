using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Read-only virtualized table for FrameView_Summary.csv files. Columns are
/// generated from the summary model (numeric columns right-aligned); header
/// clicks sort through the view model without mutating the source document.
/// </summary>
public partial class SummaryTableWindow : Window
{
    private readonly SummaryTableViewModel _viewModel;

    public SummaryTableWindow(SummaryTableViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        BuildColumns();
    }

    private void BuildColumns()
    {
        SummaryGrid.Columns.Clear();
        for (var i = 0; i < _viewModel.Columns.Count; i++)
        {
            var column = _viewModel.Columns[i];
            var index = i;
            var gridColumn = new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding($"[{index}]") { Mode = BindingMode.OneWay },
                CanUserSort = true,
            };
            if (column.Numeric)
            {
                gridColumn.ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters =
                    {
                        new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right),
                    },
                };
            }

            SummaryGrid.Columns.Add(gridColumn);
        }
    }

    private void SummaryGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        _viewModel.SortBy(e.Column.DisplayIndex);
    }
}
