using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using VidShrink.App.Localization;

namespace VidShrink.App.Playback;

internal interface IShortcutCard
{
    ShortcutTable Table { get; }

    string Heading { get; }

    bool IsOpen { get; set; }
}

/// <summary>
/// Oynatıcı ve düzenleyici kısayol panellerinin ortak tablosu: tuş kutusu, açıklama, çakışma
/// bildirimi ve varsayılana dönüş. Tekerlek satırları ve oynatıcıda sol tıkın altındaki
/// çift tık ile basılı tutma satırları değiştirilemez, düz metin kalır.
/// </summary>
internal sealed class ShortcutTable
{
    private readonly Grid _rows;
    private readonly TextBlock _notice;
    private readonly ShortcutMap _map;
    private readonly Func<string, ControlTheme?> _find;
    private string _filter = "";

    internal ShortcutTable(Grid rows, TextBlock notice, ShortcutMap map, Func<string, ControlTheme?> find)
    {
        _rows = rows;
        _rows.UseLayoutRounding = false;
        _notice = notice;
        _map = map;
        _find = find;
    }

    internal IReadOnlyList<(string Gesture, string Label)> Shown
    {
        get
        {
            var shown = new List<(string, string)>();
            for (var i = 0; i + 1 < _rows.Children.Count; i += 2)
                shown.Add((CellText(_rows.Children[i]), ((TextBlock)_rows.Children[i + 1]).Text ?? ""));
            return shown;
        }
    }

    internal IEnumerable<ShortcutKeyButton> Buttons => _rows.Children.OfType<ShortcutKeyButton>();

    internal string Filter
    {
        get => _filter;
        set
        {
            var next = (value ?? "").Trim();
            if (next == _filter) return;
            _filter = next;
            Build();
        }
    }

    internal int Matches(string query)
        => ShortcutBindings.Slots(_map).Count(slot => Fits(slot, query.Trim()))
           + (_map == ShortcutMap.Player ? Keymap.SabitSatirlar().Count(r => SabitUyar(r, query.Trim())) : 0);

    private static bool SabitUyar((string Gesture, string Label) satir, string query)
        => query.Length == 0 || SettingsSearch.Matches(satir.Label, query) || SettingsSearch.Matches(satir.Gesture, query);

    private static bool Fits(ShortcutSlot slot, string query)
        => query.Length == 0
           || SettingsSearch.Matches(slot.Label, query)
           || SettingsSearch.Matches(ShortcutKeyButton.Display(slot.Current), query);

    internal string Notice
    {
        get => _notice.Text ?? "";
        set
        {
            _notice.Text = value;
            _notice.IsVisible = value.Length > 0;
        }
    }

    internal void Attach()
    {
        ShortcutBindings.Changed -= OnBindingsChanged;
        ShortcutBindings.Changed += OnBindingsChanged;
    }

    internal void Detach() => ShortcutBindings.Changed -= OnBindingsChanged;

    internal void Reset()
    {
        ShortcutBindings.Reset(_map);
        Notice = "";
    }

    internal void Build()
    {
        _rows.Children.Clear();
        _rows.RowDefinitions.Clear();
        var index = 0;
        foreach (var slot in ShortcutBindings.Slots(_map))
        {
            if (Fits(slot, _filter)) Satir(Hucre(slot), slot.Label, index++);
            if (_map != ShortcutMap.Player || slot.Default != PlayerInput.OnPress(PlayerButton.Left)) continue;
            foreach (var sabit in Keymap.SabitSatirlar().Where(r => SabitUyar(r, _filter)))
                Satir(new KeyText { Text = Keymap.Boxed(sabit.Gesture), Theme = _find("MonoValue") }, sabit.Label, index++);
        }
    }

    private Control Hucre(ShortcutSlot slot)
    {
        if (!slot.Rebindable) return new KeyText { Text = ShortcutKeyButton.Display(slot.Current), Theme = _find("MonoValue") };
        var button = new ShortcutKeyButton(slot, _find("ShortcutKeyButton"), _find("MonoValue"));
        button.Captured += OnCaptured;
        return button;
    }

    private void Satir(Control gesture, string text, int index)
    {
        _rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(gesture, index);
        Grid.SetColumn(gesture, 0);
        var label = new SarilanMetin
        {
            Text = VidShrink.Core.Bicim.Satir.Bagla(text),
            Theme = _find("Body"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        Grid.SetRow(label, index);
        Grid.SetColumn(label, 1);
        _rows.Children.Add(gesture);
        _rows.Children.Add(label);
    }

    internal static string CellText(Control cell) => cell switch
    {
        ShortcutKeyButton button => button.Text,
        TextBlock text => text.Text ?? "",
        _ => ""
    };

    private void OnCaptured(object? sender, PlayerInput input)
    {
        if (sender is not ShortcutKeyButton button) return;
        var stolen = ShortcutBindings.Assign(button.Slot.Map, button.Slot.Id, input);
        Notice = stolen.Count == 0
            ? ""
            : Strings.Get("shortcuts.moved", ShortcutKeyButton.Display(input), button.Slot.Label, string.Join(", ", stolen.Select(s => s.Label)));
    }

    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) Build();
        else Dispatcher.UIThread.Post(Build);
    }
}
