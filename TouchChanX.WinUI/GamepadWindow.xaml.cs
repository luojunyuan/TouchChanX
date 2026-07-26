using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using R3;
using R3.ObservableEvents;

namespace TouchChanX.WinUI;

public sealed partial class GamepadWindow : Window
{
    public GamepadWindow()
        : this(
        [
            ("D-pad Left", "Left"),
            ("D-pad Up", "Up"),
            ("D-pad Right", "Right"),
            ("D-pad Down", "Down"),
            ("A", "Enter"),
            ("B", "Space"),
            ("X", "-"),
            ("Y", "-"),
            ("LB", "-"),
            ("RB", "Ctrl"),
            ("Start", "-"),
            ("Back", "Show mapping"),
            ("Left stick", "-"),
            ("Right stick", "-"),
        ])
    {
    }

    public GamepadWindow(IReadOnlyList<(string Button, string Key)> mappings)
    {
        InitializeComponent();
        PopulateMappings(mappings);
        CloseButton.Events().Click.Subscribe(_ => Close());

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }
    }

    public void ShowWithoutActivation() => this.AppWindow.Show(activateWindow: false);

    private void PopulateMappings(IReadOnlyList<(string Button, string Key)> mappings)
    {
        foreach (var mapping in mappings)
        {
            var row = new Grid
            {
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 2),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new TextBlock
            {
                Text = mapping.Button,
                FontSize = 17,
            });
            var keyText = new TextBlock
            {
                Text = mapping.Key,
                FontSize = 17,
            };
            Grid.SetColumn(keyText, 1);
            row.Children.Add(keyText);
            MappingItems.Items.Add(row);
        }
    }
}
