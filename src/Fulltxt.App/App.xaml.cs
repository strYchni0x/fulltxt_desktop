using System.Windows;
using Fulltxt.App.Services;
using Fulltxt.App.ViewModels;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Fulltxt.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        Services = services.BuildServiceProvider();

        Services.GetRequiredService<ThemeService>().Initialize();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
