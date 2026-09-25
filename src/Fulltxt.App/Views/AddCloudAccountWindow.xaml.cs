using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Media;
using Fulltxt.App.Services;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Fulltxt.App.Views;

public partial class AddCloudAccountWindow : Window
{
    private CloudProviderInfo? provider;
    private CancellationTokenSource? signInCts;

    public FileSource? CreatedSource { get; private set; }

    public AddCloudAccountWindow()
    {
        InitializeComponent();
        App.Services.GetRequiredService<ThemeService>().Attach(this);
        ProviderList.ItemsSource = CloudProviders.All;
        Closed += (_, _) => signInCts?.Cancel();
    }

    private void Provider_Click(object sender, RoutedEventArgs e)
    {
        provider = (CloudProviderInfo)((FrameworkElement)sender).DataContext;
        ChooserPanel.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Visible;
        SetStatus("", isError: false);

        FormTitle.Text = provider.Name;
        var isOAuth = provider.Auth == CloudAuthKind.OAuth;
        PasswordFields.Visibility = isOAuth ? Visibility.Collapsed : Visibility.Visible;

        if (isOAuth)
        {
            FormIntro.Text = provider.Description + " Dein Passwort gibst du nur beim Anbieter im Browser ein – FullTXT sieht es nie.";
            ActionButton.Content = "Im Browser anmelden";
        }
        else
        {
            FormIntro.Text = provider.Description;
            DisplayNameBox.Text = provider.Name;
            ServerRow.Visibility = provider.ServerEditable ? Visibility.Visible : Visibility.Collapsed;
            ServerUrlBox.Text = provider.DefaultServerUrl ?? "";
            UsernameLabel.Text = provider.UsernameLabel;
            SecretLabel.Text = provider.SecretLabel;
            ActionButton.Content = "Verbinden und hinzufügen";
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        signInCts?.Cancel();
        FormPanel.Visibility = Visibility.Collapsed;
        ChooserPanel.Visibility = Visibility.Visible;
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
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
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
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
        DialogResult = true;
    }

    private async Task ConnectWithPasswordAsync(CloudProviderInfo info)
    {
        var displayName = DisplayNameBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var secret = SecretBox.Password;
        var startFolder = string.IsNullOrWhiteSpace(StartFolderBox.Text) ? "/" : StartFolderBox.Text.Trim();

        var serverUrl = info.ServerEditable ? NormalizeServerUrl(ServerUrlBox.Text) : info.DefaultServerUrl!;
        if (info.ServerEditable && serverUrl is null)
        {
            throw new InvalidOperationException("Bitte eine gültige Server-Adresse angeben (z.B. https://cloud.example.com).");
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException($"Bitte {info.UsernameLabel} und {info.SecretLabel} ausfüllen.");
        }
        if (IsInsecureRemote(new Uri(serverUrl!)))
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
        DialogResult = true;
    }

    private static string? NormalizeServerUrl(string input)
    {
        var text = input.Trim();
        if (text.Length == 0) return null;
        if (!text.Contains("://")) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? text.TrimEnd('/')
            : null;
    }

    /// <summary>Klartext-HTTP ist nur im eigenen Netz (Loopback, private Adressen, *.local, einfache Hostnamen) vertretbar.</summary>
    private static bool IsInsecureRemote(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps) return false;
        if (uri.IsLoopback || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        if (IPAddress.TryParse(uri.Host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
        }
        return true;
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "TextMutedBrush");
    }
}
