using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LemieszeDesktop;

public partial class MainWindow
{
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LemieszeCalculator");
    private static string ProfilesDirectory => Path.Combine(DataDirectory, "profiles");
    private string profileName = "Domyślny";
    public string ProfileLabel => "Profil: " + profileName + " · zapis automatyczny";

    private void RestoreProfile()
    {
        var active = Path.Combine(DataDirectory, "active-profile.txt");
        if (!File.Exists(active)) return;
        var id = File.ReadAllText(active).Trim();
        if (!Guid.TryParseExact(id, "N", out _)) return;
        var directory = Path.Combine(ProfilesDirectory, id);
        if (!File.Exists(Path.Combine(directory, "catalog.json"))) return;
        Storage = Path.Combine(directory, "catalog.json");
        profileName = File.Exists(Path.Combine(directory, "name.txt")) ? File.ReadAllText(Path.Combine(directory, "name.txt")) : id;
    }

    private void SwitchProfile(string path, string name, string id, Catalog? replacement = null)
    {
        Save();
        var previous = Storage;
        Catalog next;
        try { Storage = path; next = replacement ?? Load(); }
        catch { Storage = previous; throw; }
        // Persist selection before touching the current collections.
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var active = Path.Combine(DataDirectory, "active-profile.txt");
            File.WriteAllText(active + ".tmp", id);
            File.Move(active + ".tmp", active, true);
        }
        catch { Storage = previous; throw; }
        initializing = true;
        PartPicker.IsOpen = false;
        choosingRecipe = null;
        lastPickerButton = null;
        foreach (var item in Models.Cast<ObservableItem>().Concat(Parts).Concat(catalog.Recipes))
            item.PropertyChanged -= ItemChanged;
        Models.Clear();
        Parts.Clear();
        catalog = next;
        foreach (var model in catalog.Models) { Models.Add(model); Watch(model); }
        foreach (var part in catalog.Parts) { Parts.Add(part); Watch(part); }
        foreach (var recipe in catalog.Recipes) Watch(recipe);
        selectedModel = Models.FirstOrDefault();
        profileName = name;
        RefreshRecipes();
        RefreshModelLists();
        Notify(nameof(SortedParts));
        Notify(nameof(ProfileLabel));
        Calculate();
        initializing = false;
        Save();
    }

    private void Profiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = ProfileWindow(this, "Profile", 460, 740);
        dialog.SetResourceReference(BackgroundProperty, "ThemeColor0C1220");
        var panel = new StackPanel { Margin = new Thickness(20,12,20,20) };
        panel.Children.Add(new TextBlock { Text = "Twoje profile", FontSize = 24, Margin = new Thickness(0,0,0,6) });
        panel.Children.Add(new TextBlock { Text = "Kliknij profil, aby go aktywować. Zmiany zapisują się automatycznie.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });
        var choices = new System.Collections.Generic.List<(string Id, string Name, string Path)> {
            ("", "Domyślny", Path.Combine(DataDirectory, "catalog.json")) };
        if (Directory.Exists(ProfilesDirectory))
            foreach (var directory in Directory.GetDirectories(ProfilesDirectory))
                if (Guid.TryParseExact(Path.GetFileName(directory), "N", out _) && File.Exists(Path.Combine(directory, "catalog.json")))
                    choices.Add((Path.GetFileName(directory), File.Exists(Path.Combine(directory,"name.txt")) ? File.ReadAllText(Path.Combine(directory,"name.txt")) : Path.GetFileName(directory), Path.Combine(directory,"catalog.json")));
        var list = new ListBox { ItemsSource = choices.Select(c => c.Name).ToList(), SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Path == Storage)), Margin = new Thickness(0,0,0,10), MaxHeight = 220, BorderThickness = new Thickness(0) };
        list.SetResourceReference(Control.BackgroundProperty, "ThemeColor0C1220");
        list.SetResourceReference(Control.ForegroundProperty, "Text");
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12,10,12,10)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty,
            (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
              <Border x:Name="Card" CornerRadius="10" Margin="0,3" Padding="12,10" Background="{DynamicResource ThemeColor283650}" BorderThickness="1" BorderBrush="Transparent">
                <ContentPresenter/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsSelected" Value="True"><Setter TargetName="Card" Property="Background" Value="{DynamicResource ThemeColor34476E}"/><Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource ThemeColor637BA8}"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Card" Property="BorderBrush" Value="{DynamicResource Text}"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        list.ItemContainerStyle = itemStyle;
        panel.Children.Add(list);
        void Run(Action action)
        {
            try { action(); dialog.Close(); }
            catch (Exception ex) { ProfileNotice(dialog, "Nie udało się wykonać operacji.\n" + ex.Message); }
        }
        bool changingSelection = false;
        list.SelectionChanged += (_, _) =>
        {
            if (changingSelection || list.SelectedIndex < 0) return;
            var entry = choices[list.SelectedIndex];
            if (entry.Path == Storage) return;
            try { SwitchProfile(entry.Path, entry.Name, entry.Id); }
            catch (Exception ex)
            {
                changingSelection = true;
                list.SelectedIndex = choices.FindIndex(c => c.Path == Storage);
                changingSelection = false;
                ProfileNotice(dialog, "Nie udało się zmienić profilu.\n" + ex.Message);
            }
        };
        var actions = new Grid { Margin = new Thickness(0,2,0,10) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var reset = new Button { Content = "Przywróć fabryczne", Margin = new Thickness(0,0,5,0), FontSize = 13 };
        reset.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var entry = choices[list.SelectedIndex];
            if (!ConfirmProfile(dialog, $"Przywrócić profil „{entry.Name}”?\nKatalog zostanie zastąpiony fabrycznym, a ilości wyzerowane. Zapiszemy kopię zapasową.")) return;
            Run(() =>
            {
                var factory = LoadDefault();
                foreach (var model in factory.Models) model.Quantity = 0;
                Save();
                if (File.Exists(entry.Path))
                    File.Copy(entry.Path, entry.Path + ".before-factory-reset-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak");
                SwitchProfile(entry.Path, entry.Name, entry.Id, factory);
            });
        };
        actions.Children.Add(reset);
        var delete = new Button { Content = "Usuń profil", Margin = new Thickness(5,0,0,0), FontSize = 13, IsEnabled = list.SelectedIndex > 0 };
        delete.SetResourceReference(Control.BackgroundProperty, "ThemeColor513348");
        list.SelectionChanged += (_, _) => delete.IsEnabled = list.SelectedIndex > 0;
        delete.Click += (_, _) =>
        {
            if (list.SelectedIndex <= 0) return;
            var entry = choices[list.SelectedIndex];
            if (!ConfirmProfile(dialog, $"Usunąć profil „{entry.Name}”?\nZniknie z listy. Kopia danych pozostanie w archiwum lokalnym.")) return;
            Run(() =>
            {
                if (!Guid.TryParseExact(entry.Id, "N", out _)) throw new InvalidDataException("Nieprawidłowy profil.");
                var root = Path.GetFullPath(ProfilesDirectory) + Path.DirectorySeparatorChar;
                var directory = Path.GetFullPath(Path.Combine(ProfilesDirectory, entry.Id));
                if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Nieprawidłowa lokalizacja profilu.");
                if (entry.Path == Storage)
                    SwitchProfile(Path.Combine(DataDirectory, "catalog.json"), "Domyślny", "");
                var archive = Path.Combine(DataDirectory, "deleted-profiles");
                Directory.CreateDirectory(archive);
                Directory.Move(directory, Path.Combine(archive, entry.Id + "-" + Guid.NewGuid().ToString("N")));
            });
        };
        Grid.SetColumn(delete, 1);
        actions.Children.Add(delete);
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock {
            Text = "Domyślny pozostaje dostępny. Nowe profile startują z katalogiem fabrycznym.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16), FontSize = 12 });

        panel.Children.Add(new TextBlock { Text = "Nazwa nowego profilu", Margin = new Thickness(0,0,0,6) });
        var nameBox = new TextBox { MinHeight = 40, Padding = new Thickness(10,6,10,6), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 80, Margin = new Thickness(0,0,0,10) };
        panel.Children.Add(nameBox);
        var create = new Button { Content = "+  Utwórz profil" };
        create.Click += (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0 || choices.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { ProfileNotice(dialog, "Wpisz nową, niepowtarzalną nazwę profilu."); return; }
            Run(() =>
            {
                Save();
                string id = Guid.NewGuid().ToString("N");
                var directory = Path.Combine(ProfilesDirectory, id);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "name.txt"), name);
                var path = Path.Combine(directory, "catalog.json");
                var factory = LoadDefault();
                foreach (var model in factory.Models) model.Quantity = 0;
                File.WriteAllText(path, JsonSerializer.Serialize(factory, JsonOptions));
                SwitchProfile(path, name, id);
            });
        };
        panel.Children.Add(create);
        ((DockPanel)((Border)dialog.Content).Child).Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.ShowDialog();
    }

    private static Window ProfileWindow(Window owner, string title, double width, double height)
    {
        var window = new Window { Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height,
            MaxHeight = SystemParameters.WorkArea.Height, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false };
        window.SetResourceReference(BackgroundProperty, "ThemeColor0C1220");
        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(8) };
        frame.SetResourceReference(Border.BorderBrushProperty, "ThemeColor43516B");
        var layout = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(12,4,4,0) };
        var close = new Button { Content = "✕", Width = 36, Height = 36, Padding = new Thickness(0), ToolTip = "Zamknij" };
        close.Click += (_, _) => window.Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock) window.DragMove(); };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        frame.Child = layout;
        window.Content = frame;
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) window.Close(); };
        return window;
    }

    private static bool ConfirmProfile(Window owner, string message, bool confirmation = true)
    {
        var window = ProfileWindow(owner, confirmation ? "Potwierdzenie" : "Profile", 450, 300);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0,0,0,22) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        bool accepted = false;
        if (confirmation)
        {
            var cancel = new Button { Content = "Anuluj", Margin = new Thickness(0,0,10,0) };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = confirmation ? "Potwierdź" : "OK" };
        ok.Click += (_, _) => { accepted = true; window.Close(); };
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        ((DockPanel)((Border)window.Content).Child).Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.ShowDialog();
        return accepted;
    }
    private static void ProfileNotice(Window owner, string message) => ConfirmProfile(owner, message, false);
}
