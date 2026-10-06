using System;
using Utils.Framework;

namespace PhinixClient.Framework
{
    /// <summary>Acquire on the main thread during Activate; the host resolves registered module ownership.</summary>
    public interface IClientLocalizationService
    {
        IClientLocalizer ForModule(IPhinixExtensionModule module);
    }

    /// <summary>Package-scoped text. Resolve labels while drawing, not once during registration.</summary>
    public interface IClientLocalizer : IDisposable
    {
        /// <summary>Requested game locale; individual missing texts may use another supplied language.</summary>
        string Locale { get; }
        event Action LanguageChanged;
        string Text(string key, string fallback = null);
        /// <summary>Numbered {0} parameters, no alignment/specifiers. Invalid formatting returns the template.</summary>
        string Format(string key, params object[] arguments);
    }
}
