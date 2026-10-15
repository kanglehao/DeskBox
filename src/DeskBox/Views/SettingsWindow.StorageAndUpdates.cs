using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Services;
using DeskBox.ViewModels;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices;
using Windows.System;
using WinRT.Interop;

namespace DeskBox.Views;

public sealed partial class SettingsWindow
{
    private async void ChangeManagedStoragePathButton_Click(object sender, RoutedEventArgs e)
    {
        await ChangeManagedStoragePathAsync();
    }

    private async Task ChangeManagedStoragePathAsync()
    {
        while (true)
        {

            if (SettingsRoot.XamlRoot is null)
            {
                return;
            }

            string? folderPath = await FolderPickerService.PickFolderAsync(_hWnd);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            string normalizedPath = SettingsService.NormalizeManagedStorageRootPath(folderPath);
            if (string.Equals(normalizedPath, ViewModel.ManagedStorageRootPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // The desktop organizer sweeps the desktop on every run; a
            // storage root there would re-ingest its own managed folders,
            // so the choice is rejected before any migration is offered.
            if (ManagedStoragePathService.OverlapsDesktop(normalizedPath))
            {
                await ShowManagedStorageDesktopOverlapDialogAsync();
                continue;
            }

            if (App.Current.WidgetManager is not { } manager) return;
            var choice = await ManagedStorageMigrationDialog.ConfirmAsync(SettingsRoot.XamlRoot, _localizationService,
                manager.GetDefaultManagedStorageWidgetCount(), ViewModel.ManagedStorageRootPath, normalizedPath,
                token => manager.PreviewManagedStorageMigrationAsync(normalizedPath, token));
            if (choice is null) return;
            if (choice == StorageMigrationStartChoice.ChooseLocation)
            {
                continue;
            }
            var outcome = await ManagedStorageMigrationDialog.RunAsync(
                SettingsRoot.XamlRoot, SettingsRoot.DispatcherQueue, _localizationService,
                ViewModel.ManagedStorageRootPath, normalizedPath,
                options => manager.UpdateDefaultManagedStorageRootAsync(normalizedPath, options),
                restart: choice == StorageMigrationStartChoice.Restart);
            if (outcome.ChooseLocation)
            {
                continue;
            }
            ManagedStorageMigrationResult? result = outcome.Migration;
            if (result is not null)
            {
                ViewModel.UpdateManagedStorageRootPath(result.NewRootPath);
                try { await App.Current.ManagedStorageDesktopShortcutService.SyncAsync(result.OldRootPath); }
                catch (Exception ex) { App.Log($"[ManagedStorageMigration] Desktop shortcut sync deferred: {ex.Message}"); }
            }
            RefreshManagedStoragePathWarning();
            return;
        }
    }

    private async Task ShowManagedStorageDesktopOverlapDialogAsync()
    {
        if (SettingsRoot.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = SettingsRoot.XamlRoot,
            Title = _localizationService.T("Settings.ManagedPath.Error.DesktopOverlapTitle"),
            CloseButtonText = _localizationService.T("Common.Close"),
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = _localizationService.T("Settings.ManagedPath.Error.DesktopOverlapBody"),
                TextWrapping = TextWrapping.Wrap
            }
        };

        await dialog.ShowAsync();
    }

    private void RefreshManagedStoragePathWarning()
    {
        if (ManagedStoragePathWarningText is null)
        {
            return;
        }

        ManagedStoragePathAssessment assessment =
            ManagedStoragePathService.AssessPath(ViewModel.ManagedStorageRootPath);
        var warnings = new List<string>();
        if (ManagedStoragePathService.OverlapsDesktop(ViewModel.ManagedStorageRootPath))
        {
            warnings.Add(_localizationService.T("Settings.ManagedPath.Warning.DesktopOverlap"));
        }
        if (assessment.IsSystemDrive)
        {
            warnings.Add(_localizationService.T(assessment.HasSuitableNonSystemDrive
                ? "Settings.ManagedPath.Warning.SystemDrive"
                : "Settings.ManagedPath.Warning.SystemDriveOnly"));
        }
        if (assessment.IsCloudSynced)
        {
            warnings.Add(_localizationService.T("Settings.ManagedPath.Warning.CloudSync"));
        }
        if (assessment.DriveType == DriveType.Removable || assessment.IsTransientBusDrive)
        {
            warnings.Add(_localizationService.T("Settings.ManagedPath.Warning.Removable"));
        }
        else if (assessment.DriveType == DriveType.Network)
        {
            warnings.Add(_localizationService.T("Settings.ManagedPath.Warning.Network"));
        }

        ManagedStoragePathWarningText.Text = string.Join(Environment.NewLine, warnings);
        ManagedStoragePathWarningBorder.IsOpen = warnings.Count > 0;
    }

    private void RefreshDragOutWin10State()
    {
        // Windows 10 cannot offer a modifier switch to external targets: a
        // brokered drag there advertises a single effect only. The modifier
        // tip therefore never fires and its toggle is disabled; the note is
        // appended to the card description (the content column is an Auto
        // grid column — a wide note there overflows the card on high-scale
        // narrow windows instead of wrapping).
        bool modifiersReachExternalTargets =
            WindowsCompatibilityService.IsWindows11OrLater;
        if (DragOutActionCard is { } card)
        {
            string description = _localizationService.T("Settings.DragOutAction.Description");
            if (!modifiersReachExternalTargets)
            {
                description += "\n" + _localizationService.T("Settings.DragOutAction.Win10Note");
            }
            card.Description = description;
        }
        if (DragOutModifierTipToggle is { } toggle)
        {
            toggle.IsEnabled = modifiersReachExternalTargets;
        }
    }

    private bool _isSynchronizingManagedStorageDesktopShortcutToggle;

    private void RefreshManagedStorageDesktopShortcutState()
    {
        if (ManagedStorageDesktopShortcutToggle is not ToggleSwitch toggle)
        {
            return;
        }

        SetManagedStorageDesktopShortcutToggleState(
            toggle,
            App.Current.ManagedStorageDesktopShortcutService.HasShortcut());
    }

    private async void ManagedStorageDesktopShortcutToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizingManagedStorageDesktopShortcutToggle ||
            sender is not ToggleSwitch toggle)
        {
            return;
        }

        bool requestedState = toggle.IsOn;
        toggle.IsEnabled = false;
        try
        {
            ManagedStorageDesktopShortcutService shortcutService =
                App.Current.ManagedStorageDesktopShortcutService;
            bool succeeded = requestedState
                ? await shortcutService.CreateAsync()
                : await shortcutService.RemoveAsync();
            if (succeeded)
            {
                return;
            }

            SetManagedStorageDesktopShortcutToggleState(toggle, !requestedState);
            await ShowInfoDialogAsync(
                _localizationService.T("Settings.ManagedPath.DesktopShortcut.Title"),
                _localizationService.T("Common.OperationFailedRetry"));
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private void SetManagedStorageDesktopShortcutToggleState(ToggleSwitch toggle, bool isOn)
    {
        _isSynchronizingManagedStorageDesktopShortcutToggle = true;
        try
        {
            toggle.IsOn = isOn;
        }
        finally
        {
            _isSynchronizingManagedStorageDesktopShortcutToggle = false;
        }
    }

    private void OpenManagedStoragePathButton_Click(object sender, RoutedEventArgs e)
    {
        string path = ViewModel.ManagedStorageRootPath;
        try
        {
            Directory.CreateDirectory(path);
            Win32Helper.OpenFile(path);
        }
        catch (Exception ex)
        {
            App.Log($"[SettingsWindow] Failed to open managed storage root '{path}': {ex.Message}");
            _ = ShowInfoDialogAsync(
                _localizationService.T("Settings.Dialog.OpenStorageFailedTitle"),
                _localizationService.Format("Settings.Dialog.OpenStorageFailedBody", ex.Message));
        }
    }

    private async void PinManagedStorageToQuickAccessButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_managedStorageSettingsViewModel.CanInvokeQuickAccessAction)
        {
            return;
        }

        string path = ViewModel.ManagedStorageRootPath;
        bool shouldUnpin = _managedStorageSettingsViewModel.ShouldUnpinQuickAccessAction;

        ViewModel.SetQuickAccessBusy(true);
        try
        {
            QuickAccessOperationResult result = shouldUnpin
                ? await ExplorerQuickAccessHelper.TryUnpinFolderFromQuickAccessAsync(path)
                : await ExplorerQuickAccessHelper.TryPinFolderToQuickAccessAsync(path);

            if (result.Succeeded)
            {
                ViewModel.SetQuickAccessPinState(shouldUnpin ? QuickAccessPinState.NotPinned : QuickAccessPinState.Pinned);
                await Task.Delay(500);
                await ViewModel.RefreshQuickAccessStateAsync();
                return;
            }

            App.Log($"[SettingsWindow] Quick Access operation failed: {result.Error ?? "unknown error"}");
            if (SettingsRoot.XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = SettingsRoot.XamlRoot,
                Title = shouldUnpin
                    ? _localizationService.T("Settings.Dialog.UnpinQuickAccessFailedTitle")
                    : _localizationService.T("Settings.Dialog.PinQuickAccessFailedTitle"),
                CloseButtonText = _localizationService.T("Common.Close"),
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    Text = _localizationService.T("Settings.Dialog.QuickAccessOperationFailedBody"),
                    TextWrapping = TextWrapping.Wrap
                }
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[SettingsWindow] Failed to update Quick Access pin state: {ex}");
            if (SettingsRoot.XamlRoot is not null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = SettingsRoot.XamlRoot,
                    Title = shouldUnpin
                        ? _localizationService.T("Settings.Dialog.UnpinQuickAccessFailedTitle")
                        : _localizationService.T("Settings.Dialog.PinQuickAccessFailedTitle"),
                    CloseButtonText = _localizationService.T("Common.Close"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = new TextBlock
                    {
                        Text = _localizationService.T("Settings.Dialog.QuickAccessOperationFailedBody"),
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                await dialog.ShowAsync();
            }
        }
        finally
        {
            ViewModel.SetQuickAccessBusy(false);
        }
    }

    private void OpenWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        Win32Helper.OpenFile(ViewModel.OfficialWebsiteLink);
    }

    private async void ShowStoreSupportDialogButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsRoot.XamlRoot is null ||
            ViewModel.StoreSupportCardVisibility != Visibility.Visible)
        {
            return;
        }

        SupportDeskBoxDialog.XamlRoot = SettingsRoot.XamlRoot;
        SupportDeskBoxDialog.Title = _localizationService.T("Settings.About.StoreSupportTitle");
        SupportDeskBoxDialog.CloseButtonText = _localizationService.T("Settings.Dialog.SupportClose");
        await SupportDeskBoxDialog.ShowAsync();
    }

    private async void OpenMicrosoftStoreButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool launched = await Launcher.LaunchUriAsync(new Uri(ViewModel.MicrosoftStoreAppLink));
            if (launched)
            {
                return;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[SettingsWindow] Failed to launch Microsoft Store app: {ex}");
        }

        Win32Helper.OpenFile(ViewModel.MicrosoftStoreLink);
    }
}
