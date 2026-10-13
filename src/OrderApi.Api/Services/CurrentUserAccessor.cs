using OrderApi.Application.Common;

namespace OrderApi.Api.Services;

public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public const string SystemUser = "system";

    public string UserName
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true
                ? user.FindFirst("sub")?.Value ?? SystemUser
                : SystemUser;
        }
    }
}
