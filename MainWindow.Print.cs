using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LemieszeDesktop;

public partial class MainWindow
{
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        UpdateAll();
        if (Results.Count == 0) { MessageBox.Show(this, "Wybierz ilości modeli.", "Tabela"); return; }
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = new FlowDocument {
            PageWidth = dialog.PrintableAreaWidth, PageHeight = dialog.PrintableAreaHeight,
            PagePadding = new Thickness(24), ColumnWidth = dialog.PrintableAreaWidth,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            Foreground = Brushes.Black, Background = Brushes.White
        };
        doc.Blocks.Add(new Paragraph(new Run("Zestawienie części — " + DateTime.Now.ToString("dd.MM.yyyy"))) { FontSize = 18, FontWeight = FontWeights.Bold });
        doc.Blocks.Add(new Paragraph(new Run(string.Join(", ", Models.Where(m => m.Quantity > 0).Select(m => $"{m.Name}: {m.Quantity} szt.")))));
        var table = new Table { CellSpacing = 0 };
        foreach (double width in new[] { 3.5, 1, 1, 1, 1 })
            table.Columns.Add(new TableColumn { Width = new GridLength(width, GridUnitType.Star) });
        var rows = new TableRowGroup();
        table.RowGroups.Add(rows);
        void Row(bool header, params string[] values)
        {
            var row = new TableRow();
            foreach (var text in values)
                row.Cells.Add(new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0) }) {
                    Padding = new Thickness(6), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5),
                    FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
                    Background = header ? Brushes.LightGray : Brushes.White });
            rows.Rows.Add(row);
        }
        Row(true, "Część", "mm", "Szt.", "Szt./ark.", "Arkusze");
        foreach (var result in Results.OrderBy(r => r.PrintOrder).ThenBy(r => r.Name))
            Row(false, result.Name, result.ThicknessLabel == "—" ? "—" : $"≠ {result.ThicknessLabel}", result.Quantity.ToString(), result.PiecesPerSheet?.ToString() ?? "—", result.Sheets?.ToString() ?? "—");
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph(new Run("Arkusze według grubości: " + string.Join("; ", Results.Where(r => r.Sheets.HasValue).GroupBy(r => r.ThicknessLabel).Select(g => $"≠ {g.Key} mm: {g.Sum(r => r.Sheets ?? 0)} ark.")))));
        doc.Blocks.Add(new Paragraph(new Run(TotalLabel.Text)));
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Zestawienie części");
    }
}
