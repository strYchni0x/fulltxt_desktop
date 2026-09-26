using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Search;
using Fulltxt.Linux.Services;
using Fulltxt.Linux.ViewModels;
using Fulltxt.Linux.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fulltxt.Linux;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Avalonia.Controls.Window CreateMainWindow()
    {
        try
        {
            Services = BuildServices();
            Services.GetRequiredService<ThemeService>().Initialize(this);
            return Services.GetRequiredService<MainWindow>();
        }
        catch (KeyringUnavailableException ex)
        {
            // Ohne Schlüsselbund kann der verschlüsselte Index nicht geöffnet werden - verständlich melden statt abstürzen.
            return new StartupErrorWindow(ex.Message);
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ =>
        {
            var keyHex = IndexKeyStore.GetOrCreateKeyHex(AppPaths.KeyFilePath);
            return new IndexDatabase(AppPaths.DatabaseFilePath, keyHex);
        });
        services.AddSingleton<SourceRepository>();
        services.AddSingleton<FileIndexRepository>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<ContentExtractorRegistry>();
        services.AddSingleton(new IndexingOptions());
        services.AddSingleton<LocalFolderIndexer>();
        services.AddSingleton<CloudConnectorFactory>();
        services.AddSingleton<CloudFolderIndexer>();
        services.AddSingleton<CloudFileService>();
        services.AddSingleton<IndexingService>();

        services.AddSingleton<ThemeService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider();

        // Schlüssel und Datenbank sofort öffnen, damit Fehler beim Start auftreten und nicht erst bei der ersten Suche.
        provider.GetRequiredService<IndexDatabase>();
        return provider;
    }
}
