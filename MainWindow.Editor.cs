using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LemieszeDesktop;

public partial class MainWindow
{
    private void PartNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitPartNumber(box);
    }

    private void PartNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Enter) { CommitPartNumber(box); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            e.Handled = true;
        }
    }

    private void CommitPartNumber(TextBox box)
    {
        if (box.DataContext is not Part part) return;
        int? value = null;
        if (!string.IsNullOrWhiteSpace(box.Text))
        {
            if (!int.TryParse(box.Text, out int number) || number <= 0)
            {
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                MessageBox.Show(this, "Wpisz dodatnią liczbę całkowitą lub pozostaw puste pole.", "Nieprawidłowa wartość");
                return;
            }
            value = number;
        }
        if ((string?)box.Tag == "Thickness") part.Thickness = value;
        else if ((string?)box.Tag == "PrintOrder") part.PrintOrder = value ?? 999;
        else part.PiecesPerSheet = value;
        UpdateAll();
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
}
