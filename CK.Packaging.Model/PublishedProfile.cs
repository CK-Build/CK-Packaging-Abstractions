using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CK.Packaging.Model;

/// <summary>
/// Immutable set of package instances that have been produced by a CKli world and are coherent: their dependencies
/// are homogeneous, no discrepancy exists among them.
/// <para>
/// Note that the coherency can be only a shallow one. deeper, transitive, dependencies are not guaranteed to be aligned.
/// </para>
/// </summary>
public sealed class PublishedProfile
{
    readonly Uri _stackUrl;
    readonly WorldName _world;
    readonly SVersion _version;
    readonly ImmutableArray<Repository> _repositories;
    readonly Dictionary<string,PackageInstance> _packages;
    readonly bool _isDeprecated;
    string? _toString;

    public PublishedProfile( Uri stackUrl,
                             WorldName world,
                             SVersion version,
                             ImmutableArray<Repository> repositories,
                             bool isDeprecated )
        : this( stackUrl, world, version, repositories, repositories.SelectMany( r => r.Packages ).ToDictionary( p => p.PackageId ), isDeprecated )
    {
    }

    PublishedProfile( Uri stackUrl,
                      WorldName world,
                      SVersion version,
                      ImmutableArray<Repository> repositories,
                      Dictionary<string, PackageInstance> packages,
                      bool isDeprecated )
    {
        if( version.VersionKind == CSVersionKind.None )
        {
            throw new ArgumentException( $"Version '{version}' must be a Conformant SVersion." );
        }
        _stackUrl = stackUrl;
        _world = world;
        _version = version;
        _repositories = repositories;
        _packages = packages;
        _packages = packages;
        _isDeprecated = isDeprecated;
    }

    /// <summary>
    /// Gets the url of the stack repository (ends with "-Stack").
    /// </summary>
    public Uri StackUrl => _stackUrl;

    /// <summary>
    /// Gets the world name.
    /// </summary>
    public WorldName World => _world;

    /// <summary>
    /// Gets whether this profile is deprecated: at least one package has been deprecated.
    /// </summary>
    public bool IsDeprecated => _isDeprecated;

    /// <summary>
    /// Gets the version. <see cref="SVersion.VersionKind"/> is necessarily
    /// not <see cref="CSVersionKind.None"/>: this is a "Conformant SVersion".
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the repositories.
    /// </summary>
    public ImmutableArray<Repository> Repositories => _repositories;

    /// <summary>
    /// Gets all the packages indexed by their <see cref="PackageInstance.PackageId"/>.
    /// </summary>
    public Dictionary<string, PackageInstance> Packages => _packages;

    /// <summary>
    /// Updater that transitions <see cref="IsDeprecated"/> to true.
    /// </summary>
    /// <returns>This profile or a new one.</returns>
    public PublishedProfile Deprecate() => _isDeprecated
                                                ? this
                                                : new PublishedProfile( _stackUrl, _world, _version, _repositories, _packages, true );

    /// <summary>
    /// Overridden to return the <see cref="World"/> and <see cref="Version"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => _toString ??= $"{_world.FullName}/v{_version}";

}


