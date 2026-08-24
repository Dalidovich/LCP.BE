using LCP.DAL.Configuration;

namespace LCP.BLL.Helpers;

public static class PasswordGate
{
    public static bool IsEnabled(LibrarySettings settings) =>
        !string.IsNullOrEmpty(settings.PasswordHash) && !string.IsNullOrEmpty(settings.PasswordSalt);
}
