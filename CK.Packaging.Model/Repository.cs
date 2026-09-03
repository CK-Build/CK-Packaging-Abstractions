using CK.Core;
using System.Collections.Immutable;

namespace CK.Packaging.Model;

/// <summary>
/// A repository contains the published <see cref="Packages"/>.
/// </summary>
public sealed class Repository
{
    readonly RepositoryKey _key;
    readonly ImmutableArray<PackageInstance> _packages;

    /// <summary>
    /// Initializes a new repository with its packages.
    /// </summary>
    /// <param name="key">The repository identifier and locator.</param>
    /// <param name="packages">The packages.</param>
    public Repository( RepositoryKey key, ImmutableArray<PackageInstance> packages )
    {
        _key = key;
        _packages = packages;
    }

    /// <summary>
    /// Gets the identifier and locator.
    /// </summary>
    public RepositoryKey Key => _key;

    /// <summary>
    /// Gets the packages.
    /// </summary>
    public ImmutableArray<PackageInstance> Packages => _packages;
}


