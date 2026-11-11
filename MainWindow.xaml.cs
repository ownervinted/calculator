using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LemieszeDesktop;

public abstract class ObservableItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public class Model : ObservableItem
{
    public int Id { get; set; }
    private string name = "";
    private string category = "Małe lemiesze";
    private int quantity;
    public string Name { get => name; set { if (name == value) return; name = value; Changed(); } }
    public string Category { get => category; set { if (category == value) return; category = value; Changed(); } }
    public int Quantity { get => quantity; set { if (quantity == value) return; quantity = value; Changed(); } }
}
public class Part : ObservableItem
{
    public int Id { get; set; }
    private string name = "";
    private int? thickness;
    private int? piecesPerSheet;
    public int PrintOrder { get; set; } = 999;
    public int? PiecesPerSheet { get => piecesPerSheet; set { if (piecesPerSheet == value) return; piecesPerSheet = value; Changed(); } }
    public string Name { get => name; set { if (name == value) return; name = value; Changed(); Changed(nameof(Display)); } }
    public int? Thickness { get => thickness; set { if (thickness == value) return; thickness = value; Changed(); Changed(nameof(Display)); } }
    public string Display => $"{Name} ({(Thickness.HasValue ? Thickness + " mm" : "—")})";
    [JsonIgnore] public string ThicknessLabel => Thickness?.ToString() ?? "—";
}
public class Recipe : ObservableItem
{
    public int ModelId { get; set; }
    private int partId;
    private int quantity = 1;
    public int PartId { get => partId; set { if (partId == value) return; partId = value; Changed(); } }
    public int Quantity { get => quantity; set { if (quantity == value) return; quantity = value; Changed(); } }
    [JsonIgnore]
    public string PartName { get; set; } = "";
    [JsonIgnore] public string ThicknessLabel { get; set; } = "—";
    [JsonIgnore] public int SortThickness { get; set; } = int.MaxValue;
}
public class Catalog
{
    public int SchemaVersion { get; set; }
    public List<Model> Models { get; set; } = [];
    public List<Part> Parts { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
}
public class Result { public int PrintOrder { get; set; } = 999; public string Name { get; set; } = ""; public string ThicknessLabel { get; set; } = "—"; public long Quantity { get; set; } public int? PiecesPerSheet { get; set; } public long? Sheets => PiecesPerSheet is > 0 ? Quantity / PiecesPerSheet.Value + (Quantity % PiecesPerSheet.Value == 0 ? 0 : 1) : null; public string QuantityLabel => Sheets.HasValue ? $"{Quantity} szt. · {Sheets} ark." : $"{Quantity} szt."; public string DisplayName => $"{Name}  ·  {ThicknessLabel}"; }
public class ResultGroup { public string ThicknessLabel { get; set; } = ""; public ObservableCollection<Result> Items { get; } = []; }

public class SmoothScrollViewer : ScrollViewer
{
    private double target;
    private double position;
    private bool animating;
    private TimeSpan lastFrame;
    public SmoothScrollViewer()
    {
        PanningMode = PanningMode.VerticalOnly;
        CanContentScroll = false;
        Loaded += (_, _) => { position = VerticalOffset; target = VerticalOffset; };
        Unloaded += (_, _) => StopAnimation();
        ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
            {
                target = Math.Clamp(target, 0, ScrollableHeight);
                position = Math.Clamp(position, 0, ScrollableHeight);
            }
            if (!animating) { position = VerticalOffset; target = VerticalOffset; }
        };
    }
    private void StopAnimation()
    {
        if (!animating) return;
        CompositionTarget.Rendering -= RenderFrame;
        animating = false;
        lastFrame = TimeSpan.Zero;
    }
    private void RenderFrame(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame) return;
        var elapsed = lastFrame == TimeSpan.Zero ? 0.016 : Math.Clamp((frame.RenderingTime - lastFrame).TotalSeconds, 0.001, 0.05);
        lastFrame = frame.RenderingTime;
        var factor = 1 - Math.Exp(-elapsed * 13);
        position += (target - position) * factor;
        if (Math.Abs(target - position) < 0.35) { position = target; ScrollToVerticalOffset(position); StopAnimation(); }
        else ScrollToVerticalOffset(PixelOffset(position));
    }
    private double PixelOffset(double offset)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        return Math.Clamp(Math.Round(offset * scale) / scale, 0, ScrollableHeight);
    }
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (ScrollableHeight <= 0) { base.OnPreviewMouseWheel(e); return; }
        if (!animating) position = VerticalOffset;
        target = Math.Clamp(target - e.Delta / 120.0 * 96, 0, ScrollableHeight);
        if (!animating) { animating = true; lastFrame = TimeSpan.Zero; CompositionTarget.Rendering += RenderFrame; }
        e.Handled = true;
    }
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private string Storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LemieszeCalculator", "catalog.json");
    private Catalog catalog = new();
    private bool initializing = true;
    private bool saving;
    private bool refreshPending;
    private Model? selectedModel;
    private Recipe? choosingRecipe;
    private Button? lastPickerButton;
    private bool pickerWasOpenOnMouseDown;
    public ObservableCollection<Model> Models { get; } = [];
    public ObservableCollection<Part> Parts { get; } = [];
    public ObservableCollection<Recipe> VisibleRecipes { get; } = [];
    public ObservableCollection<Result> Results { get; } = [];
    public ObservableCollection<ResultGroup> ResultGroups { get; } = [];
    public IEnumerable<Part> SortedParts => Parts.OrderBy(p => p.Thickness ?? int.MaxValue).ThenBy(p => p.Name).ToList();
    public IEnumerable<Recipe> SortedVisibleRecipes => VisibleRecipes.OrderBy(r => r.SortThickness).ThenBy(r => r.PartName).ToList();
    public IEnumerable<Model> SmallModels => Models.Where(x => x.Category == "Małe lemiesze");
    public IEnumerable<Model> LargeModels => Models.Where(x => x.Category == "Duże lemiesze");
    public string SelectedModelTitle => selectedModel?.Name ?? "Wybierz model";
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void RefreshModelLists() { Notify(nameof(SmallModels)); Notify(nameof(LargeModels)); Notify(nameof(SelectedModelTitle)); }

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Initialize();
        SetNavigation(true);
        DataContext = this;
        RestoreProfile();
        Notify(nameof(ProfileLabel));
        catalog = Load();
        foreach (var m in catalog.Models) { Models.Add(m); Watch(m); }
        foreach (var p in catalog.Parts) { Parts.Add(p); Watch(p); }
        foreach (var r in catalog.Recipes) Watch(r);
        selectedModel = Models.FirstOrDefault();
        RefreshRecipes();
        RefreshModelLists();
        Calculate();
        initializing = false;
        AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(TrackPickerClick), true);
        Closing += (_, _) => Save();
        Loaded += async (_, _) =>
        {
            UpdateService.ConfirmStartup();
            await UpdateService.CheckAsync(this);
        };
    }
    private void Watch(ObservableItem item) => item.PropertyChanged += ItemChanged;
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (initializing || saving || refreshPending) return;
        refreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            refreshPending = false;
            RefreshModelLists();
            RefreshRecipeNames();
            Notify(nameof(SortedParts));
            Calculate();
            Save();
        }));
    }
    private static Catalog LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LemieszeDesktop.catalog.json") ?? throw new InvalidOperationException("Brak katalogu startowego");
        return JsonSerializer.Deserialize<Catalog>(stream, JsonOptions) ?? new Catalog();
    }
    private Catalog Load()
    {
        var value = File.Exists(Storage)
            ? JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Storage), JsonOptions)
                ?? throw new InvalidDataException("Nieprawidłowy katalog")
            : LoadDefault();
        if (value.SchemaVersion < 1)
        {
            if (File.Exists(Storage))
                File.Copy(Storage, Storage + ".before-cap-split-" + Guid.NewGuid().ToString("N") + ".bak");
            foreach (var part in value.Parts.Where(p => p.Name.Trim().Equals("zaslepka", StringComparison.OrdinalIgnoreCase)
                || p.Name.Trim().Equals("zaślepka", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var large = new Part {
                    Id = checked(value.Parts.Select(p => p.Id).Concat(value.Recipes.Select(r => r.PartId)).DefaultIfEmpty(0).Max() + 1),
                    Name = "Zaślepka duża", Thickness = part.Thickness
                };
                value.Parts.Add(large);
                part.Name = "Zaślepka mała";
                foreach (var recipe in value.Recipes.Where(r => r.PartId == part.Id))
                    if (value.Models.Any(m => m.Id == recipe.ModelId && m.Category == "Duże lemiesze"))
                        recipe.PartId = large.Id;
            }
            value.SchemaVersion = 1;
        }
        if (value.SchemaVersion < 2)
        {
            if (File.Exists(Storage))
                File.Copy(Storage, Storage + ".before-sheets-" + Guid.NewGuid().ToString("N") + ".bak");
            var rates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["ksztaltownik RG268 (krotki)"] = 9,
                ["ksztaltownik RG418 (dlugi)"] = 9,
                ["przod tarczy RG268 (krotki)"] = 10,
                ["przod tarczy RG418 (dlugi)"] = 10,
                ["tarcza 218-990"] = 19,
                ["zastrzal RG058"] = 77,
                ["belka RG158058(2225)"] = 34,
                ["zastrzal RG518"] = 72,
                ["belka 5370, RG218"] = 24,
                ["zastrzal"] = 36,
                ["belka RH418(4751)"] = 36,
            };
            foreach (var part in value.Parts)
            {
                if (rates.TryGetValue(part.Name.Trim(), out int rate) && part.PiecesPerSheet == null)
                    part.PiecesPerSheet = rate;
            }
            value.SchemaVersion = 2;
        }
        if (value.SchemaVersion < 3)
        {
            if (File.Exists(Storage))
                File.Copy(Storage, Storage + ".before-rg418-5mm-" + Guid.NewGuid().ToString("N") + ".bak");
            foreach (var part in value.Parts.Where(p => p.Name.Trim().Equals("przod tarczy RG418 (dlugi)", StringComparison.OrdinalIgnoreCase)))
                part.Thickness = 5;
            value.SchemaVersion = 3;
        }
        if (value.SchemaVersion < 4)
        {
            if (File.Exists(Storage))
                File.Copy(Storage, Storage + ".before-polish-names-" + Guid.NewGuid().ToString("N") + ".bak");
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tarcza 218-990"] = "Tarcza 218-990",
                ["bok"] = "Bok",
                ["zastrzal RG058"] = "Zastrzał RG058",
                ["belka RG158058(2225)"] = "Belka RG158058 (2225)",
                ["klapka mala"] = "Klapka mała",
                ["Zaślepka mała"] = "Zaślepka mała",
                ["noz RG158"] = "Nóż RG158",
                ["noz 125(maly)"] = "Nóż 125 (mały)",
                ["klapka duza"] = "Klapka duża",
                ["noz RG155"] = "Nóż RG155",
                ["ksztaltownik RG268 (krotki)"] = "Kształtownik RG268 (krótki)",
                ["przod tarczy RG268 (krotki)"] = "Przód tarczy RG268 (krótki)",
                ["noz RG268"] = "Nóż RG268",
                ["bok (z fasolka)"] = "Bok (z fasolką)",
                ["zastrzal RG518"] = "Zastrzał RG518",
                ["belka 5370, RG218"] = "Belka 5370, RG218",
                ["jajka"] = "Jajka",
                ["zastrzal"] = "Zastrzał",
                ["ksztaltownik RG418 (dlugi)"] = "Kształtownik RG418 (długi)",
                ["przod tarczy RG418 (dlugi)"] = "Przód tarczy RG418 (długi)",
                ["noz 1500"] = "Nóż 1500",
                ["belka RH418(4751)"] = "Belka RH418 (4751)",
                ["oslonka"] = "Osłonka",
                ["jezyki"] = "Języki",
                ["Zaślepka duża"] = "Zaślepka duża",
            };
            foreach (var part in value.Parts)
                if (names.TryGetValue(part.Name.Trim(), out var corrected))
                    part.Name = corrected;
            value.SchemaVersion = 4;
        }
        if (value.SchemaVersion < 5)
        {
            if (File.Exists(Storage))
                File.Copy(Storage, Storage + ".before-print-order-" + Guid.NewGuid().ToString("N") + ".bak");
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Kształtownik RG268 (krótki)"] = 1,
                ["Kształtownik RG418 (długi)"] = 2,
                ["Przód tarczy RG268 (krótki)"] = 3,
                ["Przód tarczy RG418 (długi)"] = 4,
                ["Nóż 1500"] = 5,
                ["Nóż RG268"] = 6,
                ["Tarcza 218-990"] = 7,
                ["Klapka mała"] = 8,
                ["Klapka duża"] = 9,
                ["Bok (z fasolką)"] = 10,
                ["Bok"] = 11,
                ["Nóż RG158"] = 12,
                ["Nóż 125 (mały)"] = 13,
                ["Nóż RG155"] = 14,
                ["Zastrzał RG058"] = 15,
                ["Zastrzał RG518"] = 16,
                ["Zastrzał"] = 17,
                ["Belka RH418 (4751)"] = 18,
                ["Belka 5370, RG218"] = 19,
                ["Belka RG158058 (2225)"] = 20,
                ["Osłonka"] = 21,
                ["Zaślepka mała"] = 22,
                ["Zaślepka duża"] = 23,
                ["Języki"] = 24,
            };
            foreach (var part in value.Parts)
                if (part.PrintOrder == 999 && order.TryGetValue(part.Name.Trim(), out int position))
                    part.PrintOrder = position;
            value.SchemaVersion = 5;
        }
        return value;
    }
    private void Save()
    {
        if (saving || initializing) return;
        saving = true;
        try
        {
            catalog.Models = Models.ToList();
            catalog.Parts = Parts.ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(Storage)!);
            var temp = Storage + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(catalog, JsonOptions), Encoding.UTF8);
            File.Move(temp, Storage, true);
        }
        finally { saving = false; }
    }
    private void RefreshRecipeNames()
    {
        foreach (var recipe in VisibleRecipes)
        {
            var part = Parts.FirstOrDefault(p => p.Id == recipe.PartId);
            recipe.PartName = part?.Name ?? "Wybierz część";
            recipe.ThicknessLabel = part?.ThicknessLabel ?? "—";
            recipe.SortThickness = part?.Thickness ?? int.MaxValue;
        }
        Notify(nameof(SortedVisibleRecipes));
        RecipesView.Items.Refresh();
    }
    private void RefreshRecipes()
    {
        VisibleRecipes.Clear();
        if (selectedModel != null)
            foreach (var recipe in catalog.Recipes.Where(r => r.ModelId == selectedModel.Id)) VisibleRecipes.Add(recipe);
        RefreshRecipeNames();
        Notify(nameof(SelectedModelTitle));
    }
    private void Calculate()
    {
        Results.Clear();
        ResultGroups.Clear();
        var totals = new Dictionary<int, long>();
        foreach (var recipe in catalog.Recipes)
        {
            var count = Models.FirstOrDefault(m => m.Id == recipe.ModelId)?.Quantity ?? 0;
            if (count <= 0 || recipe.Quantity <= 0) continue;
            totals[recipe.PartId] = totals.GetValueOrDefault(recipe.PartId) + (long)count * recipe.Quantity;
        }
        foreach (var entry in totals.Select(x => (part: Parts.FirstOrDefault(p => p.Id == x.Key), quantity: x.Value)).Where(x => x.part != null).OrderBy(x => x.part!.Thickness ?? int.MaxValue).ThenBy(x => x.part!.Name))
            Results.Add(new Result { Name = entry.part!.Name, ThicknessLabel = entry.part.Thickness?.ToString() ?? "—", Quantity = entry.quantity, PrintOrder = entry.part.PrintOrder, PiecesPerSheet = entry.part.PiecesPerSheet });
        foreach (var group in Results.GroupBy(r => r.ThicknessLabel))
        {
            var resultGroup = new ResultGroup { ThicknessLabel = group.Key == "—" ? "Bez grubości" : group.Key };
            foreach (var result in group) resultGroup.Items.Add(result);
            ResultGroups.Add(resultGroup);
        }
        TotalLabel.Text = $"Łącznie: {Results.Sum(r => r.Quantity)} szt. · {Results.Sum(r => r.Sheets ?? 0)} ark.";
    }
    private void UpdateAll()
    {
        RefreshModelLists();
        RefreshRecipeNames();
        Notify(nameof(SortedParts));
        Calculate();
        Save();
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void CalculatorNav_Click(object sender, RoutedEventArgs e)
    {
        UpdateAll();
        SwitchPage(true);
    }
    private void EditorNav_Click(object sender, RoutedEventArgs e) => SwitchPage(false);
    private void Quantity_Focus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box) box.SelectAll();
    }
    private void Quantity_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Escape) { box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); e.Handled = true; }
        if (e.Key == Key.Enter) { CommitQuantity(box); e.Handled = true; }
    }
    private void Quantity_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitQuantity(box);
    }
    private void CommitQuantity(TextBox box)
    {
        int minimum = box.DataContext is Recipe ? 1 : 0;
        if (int.TryParse(box.Text, out int number) && number >= minimum)
        {
            if (box.DataContext is Model model) model.Quantity = number;
            if (box.DataContext is Recipe recipe) recipe.Quantity = number;
        }
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
    private void ChangeModel(object sender, int delta)
    {
        if (sender is Button { Tag: Model model }) { model.Quantity = Math.Max(0, (int)Math.Clamp((long)model.Quantity + delta, 0, int.MaxValue)); UpdateAll(); }
    }
    private void IncreaseModel_Click(object sender, RoutedEventArgs e) => ChangeModel(sender, 1);
    private void DecreaseModel_Click(object sender, RoutedEventArgs e) => ChangeModel(sender, -1);
    private void Calculate_Click(object sender, RoutedEventArgs e) => UpdateAll();
    private void Clear_Click(object sender, RoutedEventArgs e) { foreach (var m in Models) m.Quantity = 0; UpdateAll(); }
    private string ResultText() => string.Join(Environment.NewLine, Results.GroupBy(x => x.ThicknessLabel).Select(g => $"{g.Key} mm" + Environment.NewLine + string.Join(Environment.NewLine, g.Select(x => $"{x.Name} — {x.QuantityLabel}"))));
    private void Copy_Click(object sender, RoutedEventArgs e) { if (Results.Count > 0) Clipboard.SetText(ResultText()); }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Plik tekstowy|*.txt", FileName = "lista-elementow.txt" };
        if (dialog.ShowDialog() == true) File.WriteAllText(dialog.FileName, ResultText(), Encoding.UTF8);
    }
    private void SelectModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Model model }) { selectedModel = model; RefreshRecipes(); }
    }
    private void AddModel_Click(object sender, RoutedEventArgs e)
    {
        var model = new Model { Id = Models.Select(m => m.Id).DefaultIfEmpty(0).Max() + 1, Name = "Nowy model", Category = "Małe lemiesze" };
        Models.Add(model); Watch(model); selectedModel = model; RefreshRecipes(); UpdateAll();
    }
    private void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Model model }) return;
        if (MessageBox.Show(this, $"Usunąć model {model.Name} i jego recepturę?", "Usuń model", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        catalog.Recipes.RemoveAll(r => r.ModelId == model.Id);
        Models.Remove(model);
        if (selectedModel == model) selectedModel = Models.FirstOrDefault();
        RefreshRecipes(); UpdateAll();
    }
    private void ToggleCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Model model }) return;
        model.Category = model.Category == "Małe lemiesze" ? "Duże lemiesze" : "Małe lemiesze";
        UpdateAll();
    }
    private void AddPart_Click(object sender, RoutedEventArgs e)
    {
        var part = new Part { Id = Parts.Select(p => p.Id).DefaultIfEmpty(0).Max() + 1, Name = "Nowa część" };
        Parts.Add(part); Watch(part); UpdateAll();
    }
    private void DeletePart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Part part }) return;
        if (MessageBox.Show(this, $"Usunąć część {part.Name} ze wszystkich receptur?", "Usuń część", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        catalog.Recipes.RemoveAll(r => r.PartId == part.Id);
        Parts.Remove(part); RefreshRecipes(); UpdateAll();
    }
    private void AddRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (selectedModel == null || Parts.Count == 0) return;
        var part = Parts.FirstOrDefault(p => !catalog.Recipes.Any(r => r.ModelId == selectedModel.Id && r.PartId == p.Id));
        if (part == null) return;
        var recipe = new Recipe { ModelId = selectedModel.Id, PartId = part.Id, Quantity = 1 };
        catalog.Recipes.Add(recipe); Watch(recipe); RefreshRecipes(); UpdateAll();
    }
    private void DeleteRecipe_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Recipe recipe }) return;
        catalog.Recipes.Remove(recipe); RefreshRecipes(); UpdateAll();
    }
    private void ChangeRecipe(object sender, int delta)
    {
        if (sender is Button { Tag: Recipe recipe }) { recipe.Quantity = Math.Max(1, (int)Math.Clamp((long)recipe.Quantity + delta, 1, int.MaxValue)); UpdateAll(); }
    }
    private void IncreaseRecipe_Click(object sender, RoutedEventArgs e) => ChangeRecipe(sender, 1);
    private void DecreaseRecipe_Click(object sender, RoutedEventArgs e) => ChangeRecipe(sender, -1);
    private void TrackPickerClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        while (source != null && source is not Button) source = VisualTreeHelper.GetParent(source);
        if (source is Button button && button.Tag is Recipe && PartPicker.IsOpen)
        {
            pickerWasOpenOnMouseDown = lastPickerButton == button;
        }
        else pickerWasOpenOnMouseDown = false;
    }
    private void OpenPartPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Recipe recipe } button) return;
        if (pickerWasOpenOnMouseDown || (PartPicker.IsOpen && choosingRecipe == recipe))
        {
            PartPicker.IsOpen = false;
            choosingRecipe = null;
            lastPickerButton = null;
            pickerWasOpenOnMouseDown = false;
            return;
        }
        choosingRecipe = recipe;
        lastPickerButton = button;
        PartPicker.PlacementTarget = button;
        PartPicker.IsOpen = true;
    }
    private void ChoosePart_Click(object sender, RoutedEventArgs e)
    {
        if (choosingRecipe == null || sender is not Button { Tag: Part part }) return;
        choosingRecipe.PartId = part.Id;
        PartPicker.IsOpen = false;
        RefreshRecipeNames(); UpdateAll();
    }
    private void Save_Click(object sender, RoutedEventArgs e) => UpdateAll();
}
