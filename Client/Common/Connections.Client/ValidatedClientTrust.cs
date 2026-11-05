#if PHINIX_RIMWORLD_17
using System;
namespace Connections;
public sealed class ValidatedClientTrust
{
    private readonly byte[] pin;
    public string ServerName { get; }
    internal ValidatedClientTrust(string name, byte[] pin) { ServerName = name; this.pin = (byte[])pin.Clone(); }
    public byte[] CopyPin() => (byte[])pin.Clone();
}
public interface IValidatedClientTransport
{
    bool TryGetValidatedTrust(out ValidatedClientTrust trust);
}
#endif
