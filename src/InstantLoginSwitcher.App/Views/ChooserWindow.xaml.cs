using System.Collections.ObjectModel;
using System.Windows;
using InstantLoginSwitcher.Core.Models;

namespace InstantLoginSwitcher.App.Views;

public partial class ChooserWindow : Window
{
    public ChooserWindow(ObservableCollection<SwitchTarget> targets)
    {
        InitializeComponent();
        DataContext = targets;
        if (targets.Count > 0)
        {
            TargetsList.SelectedIndex = 0;
        }
    }

    public SwitchTarget? SelectedTarget { get; private set; }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        SelectedTarget = TargetsList.SelectedItem as SwitchTarget;
        if (SelectedTarget is null)
        {
            MessageBox.Show(this, "Choose a user before continuing.", "InstantLoginSwitcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
