namespace OrderApi.Application.Common;

public interface ICurrentUserAccessor
{
    /// <summary>Authenticated subject, or "system" when there is no user in context.</summary>
    string UserName { get; }
}
