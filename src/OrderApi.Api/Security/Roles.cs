namespace OrderApi.Api.Security;

/// <summary>Binary RBAC: any authenticated caller reads, Admin is additionally required to mutate customers.</summary>
public static class Roles
{
    public const string User = "user";
    public const string Admin = "admin";
}
