using CommunityToolkit.Mvvm.ComponentModel;

namespace ShaftStockDistributor.Desktop.ViewModels;

/// <summary>
/// Single switch the shell sets from playback. Editors bind <see cref="IsConfigurationLocked"/>
/// and mutating commands use <see cref="CanMutate"/>.
/// </summary>
public partial class ConfigurationLock : ObservableObject
{
    [ObservableProperty] private bool _isConfigurationLocked;

    public bool CanMutate => !IsConfigurationLocked;

    public string? Hint => IsConfigurationLocked ? UiText.ConfigurationLocked : null;

    public void SetLocked(bool locked)
    {
        if (IsConfigurationLocked == locked)
        {
            return;
        }

        IsConfigurationLocked = locked;
    }

    partial void OnIsConfigurationLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(Hint));
    }
}
