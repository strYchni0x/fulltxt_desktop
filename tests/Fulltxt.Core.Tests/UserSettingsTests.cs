using Fulltxt.Core.Settings;

namespace Fulltxt.Core.Tests;

public sealed class UserSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "fulltxt-settings-" + Guid.NewGuid());

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Defaults_WhenFileMissing()
    {
        var settings = UserSettings.Load(Path.Combine(directory, "settings.json"));

        Assert.Equal(100, settings.MaxSearchResults);
        Assert.Equal("System", settings.Theme);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsBothValues()
    {
        var path = Path.Combine(directory, "settings.json");
        var settings = UserSettings.Load(path);
        settings.MaxSearchResults = 500;
        settings.Theme = "Dark";
        settings.Save();

        var reloaded = UserSettings.Load(path);

        Assert.Equal(500, reloaded.MaxSearchResults);
        Assert.Equal("Dark", reloaded.Theme);
    }

    [Fact]
    public void Load_ReadsFileFromEarlierVersion_WithOnlyTheme()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{\"Theme\":\"Light\"}");

        var settings = UserSettings.Load(path);

        Assert.Equal("Light", settings.Theme);
        Assert.Equal(100, settings.MaxSearchResults);
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults_AndInvalidLimitIsReset()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{ kaputt");
        Assert.Equal(100, UserSettings.Load(path).MaxSearchResults);

        File.WriteAllText(path, "{\"MaxSearchResults\":-5}");
        Assert.Equal(100, UserSettings.Load(path).MaxSearchResults);
    }
}
