using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace LemieszeDesktop;

public static class ThemeManager
{
    private static readonly string SettingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LemieszeCalculator", "theme.txt");
    private static readonly Dictionary<string, string> LightColors = new()
    {
        ["ThemeColor0C1220"] = "#F2F2F7",
        ["ThemeColor141D32"] = "#F1F5FC",
        ["ThemeColor1D2A43"] = "#F0F4FA",
        ["ThemeColor222B41"] = "#E8EEF8",
        ["ThemeColor22304C"] = "#E7EEF9",
        ["ThemeColor243A75"] = "#E4ECFC",
        ["ThemeColor263047"] = "#F2F2F7",
        ["ThemeColor283650"] = "#F2F2F7",
        ["ThemeColor283651"] = "#F2F2F7",
        ["ThemeColor283652"] = "#F2F2F7",
        ["ThemeColor293650"] = "#E6EDF7",
        ["ThemeColor293751"] = "#FFFFFF",
        ["ThemeColor293955"] = "#E7EDF8",
        ["ThemeColor34476E"] = "#E0E9F9",
        ["ThemeColor35466B"] = "#DFE8F8",
        ["ThemeColor3A4865"] = "#D5DEEC",
        ["ThemeColor3B4862"] = "#DCE4F0",
        ["ThemeColor3B5DE6"] = "#3E63E9",
        ["ThemeColor3E62ED"] = "#3E62ED",
        ["ThemeColor43516B"] = "#DCE3EE",
        ["ThemeColor513348"] = "#FCE6EB",
        ["ThemeColor596D98"] = "#A4B6D3",
        ["ThemeColor637BA8"] = "#B9C9E3",
        ["ThemeColor6488FF"] = "#456FE0",
        ["ThemeColor8DA9FF"] = "#6085E8",
        ["ThemeColorA0222B42"] = "#F9FBFF",
        ["ThemeColorAAB7D2"] = "#6B7C94",
        ["ThemeColorAFC4FA"] = "#4168B6",
        ["ThemeColorB2BED5"] = "#64748B",
        ["ThemeColorB2C2E2"] = "#64748B",
        ["ThemeColorB9CCF9"] = "#5478B7",
        ["ThemeColorBBC8E3"] = "#64748B",
        ["ThemeColorBFD0FF"] = "#5578BC",
        ["ThemeColorDB202B42"] = "#FFFFFF",
        ["ThemeColorE8EEFF"] = "#253B62",
        ["ThemeColorF2F5FF"] = "#17243B",
        ["ThemeColorF5F7FF"] = "#17243B",
        ["ThemeColorFFD3DF"] = "#B33D5A"
    };
    private static readonly Dictionary<string, Color> DarkColors = new();
    private static readonly Color[] DarkGradient = { (Color)ColorConverter.ConvertFromString("#243A75"), (Color)ColorConverter.ConvertFromString("#141D32"), (Color)ColorConverter.ConvertFromString("#0C1220") };
    private static readonly Color[] LightGradient = { (Color)ColorConverter.ConvertFromString("#E4ECFC"), (Color)ColorConverter.ConvertFromString("#F1F5FC"), (Color)ColorConverter.ConvertFromString("#F2F2F7") };

    public static void Initialize()
    {
        foreach (var key in LightColors.Keys)
            if (Application.Current.Resources[key] is SolidColorBrush brush)
                DarkColors[key] = brush.Color;
        Apply(ReadSavedTheme(), false);
    }

    public static string ReadSavedTheme()
    {
        try { return File.ReadAllText(SettingsPath).Trim().Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark"; }
        catch { return "Dark"; }
    }

    public static void Apply(string theme, bool save = true)
    {
        bool isLight = theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        var resources = Application.Current.Resources;

        foreach (var (key, darkColor) in DarkColors)
        {
            var color = isLight
                ? (Color)ColorConverter.ConvertFromString(LightColors[key])
                : darkColor;
            resources[key] = new SolidColorBrush(color);
        }

        resources["Text"] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isLight ? "#17243B" : "#F2F5FF"));

        if (resources["WindowGradient"] is RadialGradientBrush existingGradient)
        {
            var gradient = existingGradient.CloneCurrentValue();
            var colors = isLight ? LightGradient : DarkGradient;
            for (var i = 0; i < Math.Min(gradient.GradientStops.Count, colors.Length); i++)
                gradient.GradientStops[i].Color = colors[i];
            resources["WindowGradient"] = gradient;
        }

        if (!save) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, isLight ? "Light" : "Dark");
        }
        catch { }
    }

    public static void LoadThemeIcon(ShapePath path)
    {
        var locations = new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "icon", "theme.svg"), System.IO.Path.Combine(AppContext.BaseDirectory, "icons", "theme.svg") };
        foreach (var location in locations)
        {
            if (!File.Exists(location)) continue;
            try
            {
                var xml = System.Xml.Linq.XDocument.Load(location);
                var paths = xml.Descendants().Where(e => e.Name.LocalName == "path");
                var combined = string.Join(" ", paths.Select(e => (string?)e.Attribute("d")).Where(d => !string.IsNullOrWhiteSpace(d)));
                if (!string.IsNullOrWhiteSpace(combined))
                {
                    path.Data = Geometry.Parse(combined);
                    return;
                }
            }
            catch { }
        }
    }
}
