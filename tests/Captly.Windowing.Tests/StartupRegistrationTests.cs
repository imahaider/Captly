using Microsoft.Win32;

namespace Captly.Windowing.Tests;

// Uses its own key, so the real Run key is never touched.
public class StartupRegistrationTests : IDisposable
{
    private readonly string _key = $@"Software\Captly.Tests\{Guid.NewGuid():N}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Captly.Tests", throwOnMissingSubKey: false);

    [Fact]
    public void Set_AddsAndRemovesTheEntry()
    {
        var registration = new StartupRegistration(StartupRegistration.CommandFor(@"C:\Apps\Captly.exe"), _key);
        Assert.False(registration.IsEnabled);

        registration.Set(true);
        Assert.True(registration.IsEnabled);
        using (var key = Registry.CurrentUser.OpenSubKey(_key))
            Assert.Equal("\"C:\\Apps\\Captly.exe\" --startup", key!.GetValue("Captly"));

        registration.Set(false);
        Assert.False(registration.IsEnabled);
        registration.Set(false);
    }

    [Fact]
    public void AnEntryForAnotherCopy_DoesNotCount()
    {
        new StartupRegistration(StartupRegistration.CommandFor(@"C:\Old\Captly.exe"), _key).Set(true);

        Assert.False(new StartupRegistration(StartupRegistration.CommandFor(@"C:\Apps\Captly.exe"), _key).IsEnabled);
    }
}
