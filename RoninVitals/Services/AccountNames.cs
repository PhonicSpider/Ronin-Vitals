using System.Security.Principal;

namespace RoninVitals.Services;

/// <summary>Turns an account's security ID (the long S-1-5-21-... form Task Scheduler stores) into the name people know.</summary>
internal static class AccountNames
{
    /// <summary>The account name such as <c>PC\Sam</c>, or null when the ID doesn't belong to a known account.</summary>
    public static string? Resolve(string securityId)
    {
        try
        {
            return new SecurityIdentifier(securityId).Translate(typeof(NTAccount)).Value;
        }
        catch (Exception exception) when (exception is ArgumentException or IdentityNotMappedException)
        {
            return null;
        }
    }
}
