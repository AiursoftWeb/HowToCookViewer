using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Authorization;

/// <summary>
/// A record to describe a permission in a structured way.
/// </summary>
/// <param name="Key">The programmatic key stored in the database (e.g., "CanReadUsers").</param>
/// <param name="Name">The user-friendly name displayed in the UI (e.g., "Read Users").</param>
/// <param name="Description">A detailed explanation of what the permission allows.</param>
[ExcludeFromCodeCoverage]
public record PermissionDescriptor(string Key, string Name, string Description);
