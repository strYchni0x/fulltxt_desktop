using System.Windows;
using System.Windows.Media;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Models;

namespace Fulltxt.App.Views;

public partial class AddNextcloudWindow : Window
{
    public FileSource? CreatedSource { get; private set; }

    public AddNextcloudWindow()
    {
        InitializeComponent();
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadInput(out var serverUrl, out var username, out var appPassword, out var error))
        {
            SetStatus(error!, isError: true);
            return;
        }

        TestConnectionButton.IsEnabled = false;
        SetStatus("Verbindung wird geprüft …", isError: false);
        try
        {
            using var client = new NextcloudWebDavClient(serverUrl, username, appPassword);
            await client.TestConnectionAsync();
            SetStatus("Verbindung erfolgreich.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Verbindung fehlgeschlagen: {ex.Message}", isError: true);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text))
        {
            SetStatus("Bitte einen Anzeigenamen angeben.", isError: true);
            return;
        }
        if (!TryReadInput(out var serverUrl, out var username, out var appPassword, out var error))
        {
            SetStatus(error!, isError: true);
            return;
        }

        var rootPath = string.IsNullOrWhiteSpace(RootPathBox.Text) ? "/" : RootPathBox.Text.Trim();

        CreatedSource = new FileSource
        {
            Type = SourceType.Nextcloud,
            DisplayName = DisplayNameBox.Text.Trim(),
            RootPath = rootPath,
            ServerUrl = serverUrl,
            Username = username,
            ProtectedCredential = SecretProtector.ProtectString(appPassword),
            CreatedUtc = DateTime.UtcNow,
        };
        DialogResult = true;
    }

    private bool TryReadInput(out string serverUrl, out string username, out string appPassword, out string? error)
    {
        serverUrl = ServerUrlBox.Text.Trim();
        username = UsernameBox.Text.Trim();
        appPassword = AppPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(appPassword))
        {
            error = "Bitte Server-Adresse, Benutzername und App-Passwort ausfüllen.";
            return false;
        }
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out _))
        {
            error = "Server-Adresse ist keine gültige URL.";
            return false;
        }
        error = null;
        return true;
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError ? Brushes.IndianRed : Brushes.Gray;
    }
}
