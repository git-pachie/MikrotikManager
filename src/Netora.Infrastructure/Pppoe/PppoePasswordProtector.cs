using Microsoft.AspNetCore.DataProtection;
using Netora.Application.Pppoe;

namespace Netora.Infrastructure.Pppoe;

/// <summary>
/// Encrypts PPPoE passwords with ASP.NET Core Data Protection. The key ring lives in the folder set by
/// DataProtection:KeysPath. If those keys are lost, stored passwords can no longer be decrypted, so back them up
/// together with the database.
/// </summary>
internal sealed class PppoePasswordProtector(IDataProtectionProvider provider) : IPppoePasswordProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Netora.Pppoe.Password.v1");

    public string Protect(string password) => protector.Protect(password);

    public string Unprotect(string protectedPassword) => protector.Unprotect(protectedPassword);
}
