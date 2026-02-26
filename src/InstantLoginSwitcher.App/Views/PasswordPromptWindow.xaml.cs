using System.Windows;

namespace InstantLoginSwitcher.App.Views;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(string qualifiedUser)
    {
        InitializeComponent();
        PromptText.Text = $"Enter the Windows account password for {qualifiedUser}.";
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string Password { get; private set; } = string.Empty;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
