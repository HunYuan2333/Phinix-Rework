using System.Collections.Generic;

namespace PhinixClient.Framework
{
    public enum ClientApiProviderState { Available, Registered, Unavailable }
    public interface IClientApiDirectory
    {
        // Main-thread snapshot. An empty list means no provider is registered for this exact contract type.
        IReadOnlyList<IClientApiProvider<T>> Capture<T>() where T:class;
    }
    public interface IClientApiProvider<T> where T:class
    {
        string OwnerId { get; }
        long RegistrationId { get; }
        ClientApiProviderState State { get; }
        // Revalidates the exact registration and current lifecycle. The returned API is borrowed.
        bool TryGet(out T api);
    }
}
