using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using XboxImageExtractorMobile.Services;

namespace XboxImageExtractorMobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : Microsoft.Maui.MauiAppCompatActivity
{
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        AndroidDocumentPicker.OnActivityResult(requestCode, resultCode, data);
    }
}
