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
    public List<Model> Models { get; set; } = [];
    public List<Part> Parts { get; set; } = [];
    public List<Recipe> Recipes { get; set; } = [];
}
public class Result { public string Name { get; set; } = ""; public string ThicknessLabel { get; set; } = "—"; public int Quantity { get; set; } public string DisplayName => $"{Name}  ·  {ThicknessLabel}"; }
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
        else ScrollToVerticalOffset(position);
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
    private static readonly string Storage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LemieszeCalculator", "catalog.json");
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
        DataContext = this;
        try { catalog = Load(); } catch { catalog = LoadDefault(); }
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
    private static Catalog Load() => File.Exists(Storage) ? JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Storage), JsonOptions) ?? LoadDefault() : LoadDefault();
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
        var totals = new Dictionary<int, int>();
        foreach (var recipe in catalog.Recipes)
        {
            var count = Models.FirstOrDefault(m => m.Id == recipe.ModelId)?.Quantity ?? 0;
            if (count <= 0 || recipe.Quantity <= 0) continue;
            totals[recipe.PartId] = totals.GetValueOrDefault(recipe.PartId) + count * recipe.Quantity;
        }
        foreach (var entry in totals.Select(x => (part: Parts.FirstOrDefault(p => p.Id == x.Key), quantity: x.Value)).Where(x => x.part != null).OrderBy(x => x.part!.Thickness ?? int.MaxValue).ThenBy(x => x.part!.Name))
            Results.Add(new Result { Name = entry.part!.Name, ThicknessLabel = entry.part.Thickness?.ToString() ?? "—", Quantity = entry.quantity });
        foreach (var group in Results.GroupBy(r => r.ThicknessLabel))
        {
            var resultGroup = new ResultGroup { ThicknessLabel = group.Key == "—" ? "Bez grubości" : group.Key };
            foreach (var result in group) resultGroup.Items.Add(result);
            ResultGroups.Add(resultGroup);
        }
        TotalLabel.Text = $"Łącznie: {Results.Sum(r => r.Quantity)} szt.";
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
        CalculatorPage.Visibility = Visibility.Visible;
        EditorPage.Visibility = Visibility.Collapsed;
        CalculatorNav.Background = new SolidColorBrush(Color.FromRgb(59, 93, 230));
        EditorNav.Background = new SolidColorBrush(Color.FromRgb(34, 43, 65));
        UpdateAll();
    }
    private void EditorNav_Click(object sender, RoutedEventArgs e)
    {
        CalculatorPage.Visibility = Visibility.Collapsed;
        EditorPage.Visibility = Visibility.Visible;
        CalculatorNav.Background = new SolidColorBrush(Color.FromRgb(34, 43, 65));
        EditorNav.Background = new SolidColorBrush(Color.FromRgb(59, 93, 230));
    }
    private void ChangeModel(object sender, int delta)
    {
        if (sender is Button { Tag: Model model }) { model.Quantity = Math.Max(0, model.Quantity + delta); UpdateAll(); }
    }
    private void IncreaseModel_Click(object sender, RoutedEventArgs e) => ChangeModel(sender, 1);
    private void DecreaseModel_Click(object sender, RoutedEventArgs e) => ChangeModel(sender, -1);
    private void Calculate_Click(object sender, RoutedEventArgs e) => UpdateAll();
    private void Clear_Click(object sender, RoutedEventArgs e) { foreach (var m in Models) m.Quantity = 0; UpdateAll(); }
    private string ResultText() => string.Join(Environment.NewLine, Results.GroupBy(x => x.ThicknessLabel).Select(g => $"{g.Key} mm" + Environment.NewLine + string.Join(Environment.NewLine, g.Select(x => $"{x.Name} — {x.Quantity} szt."))));
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
        if (sender is Button { Tag: Recipe recipe }) { recipe.Quantity = Math.Max(1, recipe.Quantity + delta); UpdateAll(); }
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
