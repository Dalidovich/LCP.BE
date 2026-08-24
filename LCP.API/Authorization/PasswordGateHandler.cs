using LCP.BLL.Helpers;
using LCP.DAL.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace LCP.API.Authorization;

public class PasswordGateHandler : AuthorizationHandler<PasswordGateRequirement>
{
    private readonly IOptionsMonitor<LibrarySettings> _settings;

    public PasswordGateHandler(IOptionsMonitor<LibrarySettings> settings)
    {
        _settings = settings;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PasswordGateRequirement requirement)
    {
        if (!PasswordGate.IsEnabled(_settings.CurrentValue) || context.User.Identity?.IsAuthenticated == true)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
