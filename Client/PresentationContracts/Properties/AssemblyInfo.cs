using System.Runtime.CompilerServices;

// Shared native shell uses the existing internal ID validator, not a second validation rule.
[assembly: InternalsVisibleTo("ClientExtensionAbstractions")]
