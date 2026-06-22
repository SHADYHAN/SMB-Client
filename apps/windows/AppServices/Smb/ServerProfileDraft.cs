namespace Rynat.WindowsClient.AppServices.Smb;

public sealed record ServerProfileDraft(
    string? Id,
    string DisplayName,
    string Host,
    string? Username,
    string? AuthMode = null,
    string? DialectPreference = null,
    bool SetActive = false
);
