using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Models;
using Fulltxt.Linux.Services;

namespace Fulltxt.Linux.Views;

public partial class AddCloudAccountWindow : Window
{
    private CloudProviderInfo? provider;
    private CancellationTokenSource? signInCts;

    public FileSource? CreatedSource { get; private set; }

    public AddCloudAccountWindow()
    {
        InitializeComponent();
        ProviderList.ItemsSource = CloudProviders.All;
        Closed += (_, _) => signInCts?.Cancel();
    }

    private void Provider_Click(object? sender, RoutedEventArgs e)
    {
        provider = (CloudProviderInfo)((Control)sender!).DataContext!;
        ChooserPanel.IsVisible = false;
        FormPanel.IsVisible = true;
        SetStatus("", isError: false);

        FormTitle.Text = provider.Name;
        var isOAuth = provider.Auth == CloudAuthKind.OAuth;
        PasswordFields.IsVisible = !isOAuth;

        if (isOAuth)
        {
            FormIntro.Text = provider.Description + " Dein Passwort gibst du nur beim Anbieter im Browser ein – FullTXT sieht es nie.";
            ActionButton.Content = "Im Browser anmelden";
        }
        else
        {
            FormIntro.Text = provider.Description;
            DisplayNameBox.Text = provider.Name;
            ServerRow.IsVisible = provider.ServerEditable;
            ServerUrlBox.Text = provider.DefaultServerUrl ?? "";
            UsernameLabel.Text = provider.UsernameLabel;
            SecretLabel.Text = provider.SecretLabel;
            ActionButton.Content = "Verbinden und hinzufügen";
        }
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        signInCts?.Cancel();
        FormPanel.IsVisible = false;
        ChooserPanel.IsVisible = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private async void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (provider is null) return;

        ActionButton.IsEnabled = false;
        try
        {
            if (provider.Auth == CloudAuthKind.OAuth) await SignInWithBrowserAsync(provider);
            else await ConnectWithPasswordAsync(provider);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Abgebrochen.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            ActionButton.IsEnabled = true;
        }
    }

    private async Task SignInWithBrowserAsync(CloudProviderInfo info)
    {
        signInCts?.Cancel();
        signInCts = new CancellationTokenSource();
        SetStatus("Bitte melde dich im geöffneten Browserfenster an … (Abbruch mit „Zurück“)", isError: false);

        var (account, tokens) = await CloudConnectorFactory.SignInAsync(info.Type,
            uri =>
            {
                Shell.Open(uri.AbsoluteUri);
                return Task.CompletedTask;
            },
            signInCts.Token);

        CreatedSource = new FileSource
        {
            Type = info.Type,
            DisplayName = $"{info.Name} ({account})",
            RootPath = "/",
            Username = account,
            ProtectedCredential = tokens,
            CreatedUtc = DateTime.UtcNow,
        };
        Close(true);
    }

    private async Task ConnectWithPasswordAsync(CloudProviderInfo info)
    {
        var displayName = DisplayNameBox.Text?.Trim() ?? "";
        var username = UsernameBox.Text?.Trim() ?? "";
        var secret = SecretBox.Text ?? "";
        var startFolder = string.IsNullOrWhiteSpace(StartFolderBox.Text) ? "/" : StartFolderBox.Text.Trim();

        var serverUrl = info.ServerEditable ? CloudAccountInput.NormalizeServerUrl(ServerUrlBox.Text ?? "") : info.DefaultServerUrl!;
        if (info.ServerEditable && serverUrl is null)
        {
            throw new InvalidOperationException("Bitte eine gültige Server-Adresse angeben (z.B. https://cloud.example.com).");
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException($"Bitte {info.UsernameLabel} und {info.SecretLabel} ausfüllen.");
        }
        if (CloudAccountInput.IsInsecureRemote(new Uri(serverUrl!)))
        {
            throw new InvalidOperationException("Bitte https:// verwenden – über http würde das Passwort unverschlüsselt durchs Internet gehen.");
        }

        SetStatus("Verbindung wird geprüft …", isError: false);
        using (var connector = CloudConnectorFactory.CreateForNewPasswordAccount(info.Type, serverUrl!, username, secret, startFolder))
        {
            await connector.TestConnectionAsync();
        }

        CreatedSource = new FileSource
        {
            Type = info.Type,
            DisplayName = string.IsNullOrEmpty(displayName) ? info.Name : displayName,
            RootPath = startFolder,
            ServerUrl = info.ServerEditable ? serverUrl : null,
            Username = username,
            ProtectedCredential = SecretProtector.ProtectString(secret),
            CreatedUtc = DateTime.UtcNow,
        };
        Close(true);
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = (IBrush?)(this.TryFindResource(isError ? "DangerBrush" : "TextMutedBrush", ActualThemeVariant, out var brush) ? brush : null);
    }
}
