using Rynat.WindowsClient.Domain;
using Rynat.WindowsClient.Platform.Dialogs;
using Rynat.WindowsClient.Services.Profiles;
using Rynat.WindowsClient.Services.Smb;
using Rynat.WindowsClient.UI.Login;
using Rynat.WindowsClient.UI.Status;

namespace Rynat.WindowsClient.UI.Shell;

public sealed class LoginCoordinator
{
    private readonly ISmbSessionService _sessionService;
    private readonly IServerProfileService _serverProfileService;
    private readonly IServerSettingsDialogService _serverSettingsDialogService;
    private readonly LoginViewModel _login;
    private readonly StatusBarViewModel _status;
    private readonly Func<ServerSession, Task> _completeLoginAsync;
    private readonly Func<Exception, string, string> _userFacingError;
    private readonly Func<bool> _isLoggedIn;

    public LoginCoordinator(
        ISmbSessionService sessionService,
        IServerProfileService serverProfileService,
        IServerSettingsDialogService serverSettingsDialogService,
        LoginViewModel login,
        StatusBarViewModel status,
        Func<ServerSession, Task> completeLoginAsync,
        Func<Exception, string, string> userFacingError,
        Func<bool> isLoggedIn
    )
    {
        _sessionService = sessionService;
        _serverProfileService = serverProfileService;
        _serverSettingsDialogService = serverSettingsDialogService;
        _login = login;
        _status = status;
        _completeLoginAsync = completeLoginAsync;
        _userFacingError = userFacingError;
        _isLoggedIn = isLoggedIn;
    }

    public async Task LoginAsync()
    {
        if (!CanLogin())
        {
            return;
        }

        _login.IsBusy = true;
        _login.Message = "正在连接...";
        _status.Message = "正在连接服务器...";

        try
        {
            var storedProfile = StoredCredentialProfileForLogin();
            var typedPassword = _login.Password;
            var hasTypedPassword = !string.IsNullOrEmpty(typedPassword);
            var useStoredCredential = storedProfile is not null && !hasTypedPassword;
            ServerProfile? loginProfile = storedProfile ?? MatchingSelectedProfile();
            if (!useStoredCredential)
            {
                loginProfile = await EnsureLoginProfileAsync();
                if (loginProfile is null)
                {
                    return;
                }
            }

            var result = useStoredCredential && storedProfile is not null
                ? await _sessionService.ConnectStoredCredentialAsync(storedProfile)
                : await _sessionService.ConnectAsync(
                    _login.ServerHost,
                    _login.Username,
                    typedPassword,
                    loginProfile?.Id
                );

            if (!result.Succeeded || result.Session is null)
            {
                _login.Message = result.Summary;
                _status.Message = result.Summary;
                return;
            }

            if (!hasTypedPassword && storedProfile is not null)
            {
                await UpdateStoredCredentialOptionsAsync(storedProfile);
            }
            else
            {
                await SaveLoginProfileAsync(loginProfile, typedPassword);
            }

            await _completeLoginAsync(result.Session);
        }
        catch (Exception ex)
        {
            _login.Message = _userFacingError(ex, "登录失败");
            _status.Message = _login.Message;
        }
        finally
        {
            _login.IsBusy = false;
        }
    }

    public async Task TryAutoLoginAsync()
    {
        var profile = StoredCredentialProfileForLogin();
        if (_isLoggedIn() || profile is null || !profile.AutoLogin)
        {
            return;
        }

        _login.IsBusy = true;
        _login.Message = "正在自动登录...";
        _status.Message = $"正在自动连接 {profile.DisplayName}...";

        try
        {
            var result = await _sessionService.ConnectStoredCredentialAsync(profile);
            if (!result.Succeeded || result.Session is null)
            {
                _login.Message = "自动登录失败，请手动登录。";
                _status.Message = result.Summary;
                return;
            }

            await _completeLoginAsync(result.Session);
        }
        catch (Exception ex)
        {
            _login.Message = "自动登录失败，请手动登录。";
            _status.Message = _userFacingError(ex, "自动登录失败");
        }
        finally
        {
            _login.IsBusy = false;
        }
    }

    public async Task OpenServerSettingsAsync()
    {
        var activeProfile = _login.SelectedProfile;
        var result = await _serverSettingsDialogService.ShowAsync(_login.ServerProfiles.ToArray(), activeProfile);
        if (result is null)
        {
            return;
        }

        _login.ReplaceServerProfiles(result.Profiles, result.ActiveProfile);
        _status.Message = "服务器设置已更新。";
    }

    public bool CanLogin()
    {
        return !_login.IsBusy
            && !string.IsNullOrWhiteSpace(_login.ServerHost)
            && !string.IsNullOrWhiteSpace(_login.Username)
            && (!string.IsNullOrEmpty(_login.Password) || StoredCredentialProfileForLogin() is not null);
    }

    private async Task<ServerProfile?> EnsureLoginProfileAsync()
    {
        var saveResult = await _serverProfileService.SaveLoginProfileAsync(
            MatchingSelectedProfile(),
            _login.ServerHost,
            _login.Username
        );

        if (saveResult.Succeeded && saveResult.Profile is not null)
        {
            _login.UpsertProfile(saveResult.Profile, preservePassword: true);
            return saveResult.Profile;
        }

        _login.Message = saveResult.Summary;
        _status.Message = saveResult.Summary;
        return null;
    }

    private async Task SaveLoginProfileAsync(ServerProfile? loginProfile, string password)
    {
        var saveResult = await _serverProfileService.SaveLoginAsync(
            loginProfile ?? MatchingSelectedProfile(),
            _login.ServerHost,
            _login.Username,
            password,
            _login.RememberPassword,
            _login.AutoLogin
        );

        if (saveResult.Succeeded && saveResult.Profile is not null)
        {
            _login.UpsertProfile(saveResult.Profile);
            return;
        }

        _status.Message = saveResult.Summary;
    }

    private async Task UpdateStoredCredentialOptionsAsync(ServerProfile profile)
    {
        var saveResult = await _serverProfileService.UpdateCredentialOptionsAsync(
            profile,
            _login.RememberPassword,
            _login.AutoLogin
        );

        if (saveResult.Succeeded && saveResult.Profile is not null)
        {
            _login.UpsertProfile(saveResult.Profile);
            return;
        }

        _status.Message = saveResult.Summary;
    }

    private ServerProfile? MatchingSelectedProfile()
    {
        var selected = _login.SelectedProfile;
        if (selected is null)
        {
            return null;
        }

        return selected.Host.Equals(_login.ServerHost.Trim(), StringComparison.OrdinalIgnoreCase)
            ? selected
            : null;
    }

    private ServerProfile? StoredCredentialProfileForLogin()
    {
        var profile = MatchingSelectedProfile();
        if (profile?.HasStoredCredential != true)
        {
            return null;
        }

        var profileUsername = profile.Username?.Trim() ?? string.Empty;
        return profileUsername.Equals(_login.Username.Trim(), StringComparison.OrdinalIgnoreCase)
            ? profile
            : null;
    }
}
