using System.Globalization;
using System.Numerics;
using Content.Shared._Serenity.Botany;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Serenity.Botany;

/// <summary>
/// The plant analyzer readout: a stats tab and a chemicals tab, rebuilt each time the server sends a new state.
/// </summary>
public sealed class PlantAnalyzerWindow : DefaultWindow
{
    private static readonly Color Water = Color.FromHex("#3a8fd6");
    private static readonly Color Nutrients = Color.FromHex("#c9812a");
    private static readonly Color Bad = Color.FromHex("#a33d3d");
    private static readonly Color Health = Color.FromHex("#4fa04f");
    private static readonly Color Potency = Color.FromHex("#8a5fc0");
    private static readonly Color Instability = Color.FromHex("#c0c03a");

    private readonly BoxContainer _stats = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };
    private readonly BoxContainer _chemicals = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(6) };

    public PlantAnalyzerWindow()
    {
        Title = Loc.GetString("plant-analyzer-title");
        MinSize = new Vector2(380, 300);
        SetSize = new Vector2(400, 520);

        var tabs = new TabContainer { VerticalExpand = true };
        tabs.AddChild(Scroll(_stats));
        tabs.AddChild(Scroll(_chemicals));
        TabContainer.SetTabTitle(tabs.GetChild(0), Loc.GetString("plant-analyzer-tab-stats"));
        TabContainer.SetTabTitle(tabs.GetChild(1), Loc.GetString("plant-analyzer-tab-chemicals"));
        ContentsContainer.AddChild(tabs);
    }

    private static ScrollContainer Scroll(Control child)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        scroll.AddChild(child);
        return scroll;
    }

    public void Populate(PlantAnalyzerUiState state)
    {
        _stats.RemoveAllChildren();
        _chemicals.RemoveAllChildren();

        AddText(_stats, state.TargetName, bold: true);

        if (state.Tray is { } tray)
            PopulateTray(tray);

        if (state.Plant is { } plant)
            PopulatePlant(plant, state);
        else if (state.Tray == null)
            AddText(_stats, Loc.GetString("plant-analyzer-no-target"));

        PopulateChemicals(state);
    }

    private void PopulateTray(PlantAnalyzerTrayData tray)
    {
        AddHeader(_stats, Loc.GetString("plant-analyzer-section-tray"));
        AddBar(_stats, Loc.GetString("plant-analyzer-water"), tray.Water, tray.MaxWater, Water);
        AddBar(_stats, Loc.GetString("plant-analyzer-nutrients"), tray.Nutrition, tray.MaxNutrition, Nutrients);
        AddBar(_stats, Loc.GetString("plant-analyzer-weeds"), tray.Weeds, tray.MaxWeeds, Bad);
        AddRaisers(tray.WeedRaisers);
        AddBar(_stats, Loc.GetString("plant-analyzer-pests"), tray.Pests, tray.MaxPests, Bad);
        AddRaisers(tray.PestRaisers);
        AddBar(_stats, Loc.GetString("plant-analyzer-toxins"), tray.Toxins, tray.MaxToxins, Bad);
        AddRaisers(tray.ToxinRaisers);
        AddText(_stats, Loc.GetString("plant-analyzer-weed-growth", ("chance", Fmt(tray.WeedGrowthChance * 100f))));
    }

    private void AddRaisers(List<string> reagents)
    {
        if (reagents.Count == 0)
            return;

        AddText(_stats, Loc.GetString("plant-analyzer-raised-by", ("reagents", string.Join(", ", reagents))), color: Bad);
    }

    private void PopulatePlant(PlantAnalyzerPlantData plant, PlantAnalyzerUiState state)
    {
        AddHeader(_stats, Loc.GetString("plant-analyzer-section-plant"));
        AddText(_stats, Loc.GetString("plant-analyzer-plant-name", ("name", plant.Name)), bold: true);

        if (plant.Dead)
            AddText(_stats, Loc.GetString("plant-analyzer-plant-dead"), color: Bad);
        else if (plant.ReadyForHarvest)
            AddText(_stats, Loc.GetString("plant-analyzer-plant-ready"), color: Health);

        if (plant.Growing)
        {
            AddBar(_stats, Loc.GetString("plant-analyzer-health"), plant.Health, plant.Endurance, Health);
            AddText(_stats, Loc.GetString("plant-analyzer-age",
                ("age", Time(plant.AgeSeconds)),
                ("lifespan", Time(plant.LifespanSeconds))));
        }
        else
        {
            AddText(_stats, Loc.GetString("plant-analyzer-age",
                ("age", Time(0)),
                ("lifespan", Time(plant.LifespanSeconds))));
        }

        AddText(_stats, Loc.GetString("plant-analyzer-maturation", ("time", Time(plant.MaturationSeconds))));
        AddText(_stats, Loc.GetString("plant-analyzer-production", ("time", Time(plant.ProductionSeconds))));
        AddText(_stats, Loc.GetString("plant-analyzer-yield", ("yield", plant.Yield)));
        AddBar(_stats, Loc.GetString("plant-analyzer-potency"), plant.Potency, 100f, Potency);
        AddBar(_stats, Loc.GetString("plant-analyzer-instability"), plant.Instability, 100f, Instability);

        AddText(_stats, state.MutatesInto.Count > 0
            ? Loc.GetString("plant-analyzer-mutates-into", ("species", string.Join(", ", state.MutatesInto)))
            : Loc.GetString("plant-analyzer-mutates-into-none"));
    }

    private void PopulateChemicals(PlantAnalyzerUiState state)
    {
        if (state.Tray != null)
        {
            AddHeader(_chemicals, Loc.GetString("plant-analyzer-soil-header"));
            if (state.Soil.Count == 0)
                AddText(_chemicals, Loc.GetString("plant-analyzer-soil-empty"));

            foreach (var reagent in state.Soil)
            {
                AddText(_chemicals, Loc.GetString("plant-analyzer-reagent-line",
                    ("name", reagent.Name), ("units", Fmt(reagent.Units))));
            }
        }

        AddHeader(_chemicals, Loc.GetString("plant-analyzer-produce-header"));
        if (state.Produce.Count == 0)
            AddText(_chemicals, Loc.GetString("plant-analyzer-produce-empty"));

        foreach (var reagent in state.Produce)
        {
            AddText(_chemicals, Loc.GetString("plant-analyzer-produce-line",
                ("name", reagent.Name), ("units", Fmt(reagent.Units)), ("percent", Fmt(reagent.Percent))));
        }
    }

    private static string Fmt(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Time(float seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalMinutes >= 1
            ? $"{(int) span.TotalMinutes}m {span.Seconds}s"
            : Loc.GetString("plant-analyzer-seconds", ("seconds", (int) span.TotalSeconds));
    }

    private static void AddText(Control parent, string text, bool bold = false, Color? color = null)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        var markup = FormattedMessage.EscapeText(text);
        if (bold)
            markup = $"[bold]{markup}[/bold]";
        if (color is { } c)
            markup = $"[color={c.ToHex()}]{markup}[/color]";

        label.SetMessage(FormattedMessage.FromMarkupOrThrow(markup));
        parent.AddChild(label);
    }

    private static void AddHeader(Control parent, string text)
    {
        parent.AddChild(new Control { MinHeight = 6 });
        AddText(parent, text, bold: true);
        parent.AddChild(new PanelContainer
        {
            MinHeight = 2,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Gray.WithAlpha(0.4f) },
        });
    }

    private static void AddBar(Control parent, string name, float value, float max, Color color)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 2) };
        row.AddChild(new Label { Text = name, MinWidth = 100 });

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = Math.Max(max, 0.001f),
            Value = Math.Clamp(value, 0f, Math.Max(max, 0.001f)),
            HorizontalExpand = true,
            MinHeight = 18,
            ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = color },
        };
        bar.AddChild(new Label
        {
            Text = $"{Fmt(value)} / {Fmt(max)}",
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Center,
        });

        row.AddChild(bar);
        parent.AddChild(row);
    }
}
