namespace Textzy.Api.Services;

public class RbacService(AuthContext auth)
{
    public bool HasAnyRole(params string[] roles)
    {
        if (!auth.IsAuthenticated) return false;
        return roles.Any(r => string.Equals(r, auth.Role, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasPermission(string permission)
    {
        if (!auth.IsAuthenticated) return false;
        return auth.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}
