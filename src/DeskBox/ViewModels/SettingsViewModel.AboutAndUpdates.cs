using System.Reflection;
using DeskBox.Services;
using Microsoft.UI.Xaml;

namespace DeskBox.ViewModels;

public partial class SettingsViewModel
{
    public string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0] ??
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ??
        "1.0.2";
    public string DistributionChannelText => _localizationService.T(IsStoreDistribution
        ? "Settings.About.Channel.Store"
        : "Settings.About.Channel.Direct");
    public string OfficialWebsiteDisplayText => OfficialWebsiteUrl.Replace("https://", string.Empty).TrimEnd('/');
    public string OfficialWebsiteLink => OfficialWebsiteUrl;
    public string MicrosoftStoreLink => MicrosoftStoreUrl;
    public string MicrosoftStoreAppLink => MicrosoftStoreAppUrl;
    public Visibility StoreSupportCardVisibility => IsDirectInstallerDistribution ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Donation QR assets ship only in Direct builds; Store policy forbids
    /// payment QR codes in the package, so every surface that shows one must
    /// collapse under the Store channel.
    /// </summary>
    public Visibility DonationQrCodeVisibility => IsDirectInstallerDistribution ? Visibility.Visible : Visibility.Collapsed;
    public string OpenSourceRepositoryDisplayText =>
        _localizationService.Format(
            "Settings.About.Developer",
            RepositoryUrl.Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/'));

    private static bool IsStoreDistribution => AppDistributionService.Current.IsMicrosoftStore;
    private static bool IsDirectInstallerDistribution => !AppDistributionService.Current.IsMicrosoftStore;
}
