namespace CK.Packaging.Abstractions;

/// <summary>
/// Where an <see cref="AmbiguousDependency"/>'s resolved <see cref="CK.Core.PackageInstance.Version"/> comes
/// from: each value names the <see cref="PublishedProfile"/> property that holds it.
/// </summary>
public enum VersionSource
{
    /// <summary>
    /// This entry's own <see cref="AmbiguousDependency.Resolutions"/>: the version is the greatest
    /// of them (NuGet's highest-wins). Nothing outside the transitive packages anchors it.
    /// </summary>
    TransitiveDependencies,

    /// <summary>
    /// A <see cref="PublishedProfile.DirectDependencies"/> entry: a repository references the
    /// identifier explicitly, so NuGet's nearest-wins makes that version authoritative here.
    /// The <see cref="AmbiguousDependency.Resolutions"/> are the ones that resolved to more.
    /// </summary>
    DirectDependencies,

    /// <summary>
    /// A <see cref="PublishedProfile.ProducedPackages"/> entry: this profile produces the identifier
    /// itself. The <see cref="AmbiguousDependency.Resolutions"/> are the ones that resolved to
    /// another version.
    /// </summary>
    ProducedPackages
}
