using System;
using System.Security.Claims;

namespace API.Extensions;

public static class ClaimPrincipleExtensions
{
    public static string GetUserName(this ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.Name) ?? throw new Exception("Can not get Username ");
    }
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        return Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new Exception("can not get UserId "));
    }
}
