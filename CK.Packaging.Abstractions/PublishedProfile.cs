using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CK.Packaging.Abstractions;

/// <summary>
/// Immutable set of package instances that have been produced by a CKli world and are coherent: their dependencies
/// are homogeneous, no discrepancy exists among them.
/// <para>
/// Note that the coherency can be only a shallow one. deeper, transitive, dependencies are not guaranteed to be aligned.
/// </para>
/// <para>
/// A profile offers at most one version of a package identifier: this is checked by the constructor.
/// </para>
/// </summary>
public sealed partial class PublishedProfile
{
    readonly Uri _stackUrl;
    readonly WorldName _world;
    readonly SVersion _version;
    readonly ImmutableArray<Repository> _repositories;
    readonly Dictionary<string, PackageInstance> _packages;
    readonly bool _isDeprecated;
    string? _toString;

    /// <summary>
    /// Initializes a new profile.
    /// <para>
    /// The <paramref name="repositories"/> are sorted by their <see cref="RepositoryKey.Url"/>: a profile
    /// is always in a canonical form, whatever the order in which its repositories have been discovered.
    /// </para>
    /// </summary>
    /// <param name="stackUrl">The stack repository url. Must be absolute.</param>
    /// <param name="world">The world name.</param>
    /// <param name="version">The profile version. Must be a Conformant SVersion.</param>
    /// <param name="repositories">
    /// The repositories. Must not be default, must not contain the same <see cref="RepositoryKey.Url"/>
    /// or <see cref="RepositoryKey.Id"/> twice and no package identifier can appear in more than one of them.
    /// </param>
    /// <param name="isDeprecated">Whether this profile is deprecated.</param>
    public PublishedProfile( Uri stackUrl,
                             WorldName world,
                             SVersion version,
                             ImmutableArray<Repository> repositories,
                             bool isDeprecated = false )
    {
        ArgumentNullException.ThrowIfNull( stackUrl );
        ArgumentNullException.ThrowIfNull( world );
        ArgumentNullException.ThrowIfNull( version );
        if( !stackUrl.IsAbsoluteUri )
        {
            throw new ArgumentException( $"Stack url must be absolute: '{stackUrl}'.", nameof( stackUrl ) );
        }
        if( version.VersionKind == CSVersionKind.None )
        {
            throw new ArgumentException( $"Version '{version}' must be a Conformant SVersion.", nameof( version ) );
        }
        if( repositories.IsDefault )
        {
            throw new ArgumentException( "Repositories must be initialized.", nameof( repositories ) );
        }
        _repositories = repositories.Sort( static ( r1, r2 ) => r1 is null
                                                                    ? (r2 is null ? 0 : -1)
                                                                    : r2 is null
                                                                        ? 1
                                                                        : string.CompareOrdinal( r1.Key.Url.AbsoluteUri,
                                                                                                 r2.Key.Url.AbsoluteUri ) );
        _packages = CreatePackageIndex( _repositories );
        _stackUrl = stackUrl;
        _world = world;
        _version = version;
        _isDeprecated = isDeprecated;
    }

    // Copy constructor used by the updaters: the repositories and the package index are shared.
    PublishedProfile( PublishedProfile o, bool isDeprecated )
    {
        _stackUrl = o._stackUrl;
        _world = o._world;
        _version = o._version;
        _repositories = o._repositories;
        _packages = o._packages;
        _isDeprecated = isDeprecated;
        _toString = o._toString;
    }

    // Indexes the packages of the (sorted) repositories and checks the profile's coherency.
    static Dictionary<string, PackageInstance> CreatePackageIndex( ImmutableArray<Repository> repositories )
    {
        var packages = new Dictionary<string, PackageInstance>( StringComparer.OrdinalIgnoreCase );
        var ids = new HashSet<RandomId>();
        Uri? previousUrl = null;
        foreach( var r in repositories )
        {
            if( r is null )
            {
                throw new ArgumentException( "Null repository.", nameof( repositories ) );
            }
            // Repositories are sorted by url: a duplicate can only be the previous one.
            if( previousUrl != null
                && string.Equals( previousUrl.AbsoluteUri, r.Key.Url.AbsoluteUri, StringComparison.Ordinal ) )
            {
                throw new ArgumentException( $"Duplicate repository url '{r.Key.Url}'.", nameof( repositories ) );
            }
            previousUrl = r.Key.Url;
            if( !ids.Add( r.Key.Id ) )
            {
                throw new ArgumentException( $"Duplicate repository identifier '{r.Key.Id}' ('{r.Key.Url}').",
                                             nameof( repositories ) );
            }
            foreach( var p in r.Packages )
            {
                if( packages.TryGetValue( p.PackageId, out var already ) )
                {
                    throw new ArgumentException( $"Package '{p.PackageId}' appears more than once in the profile: "
                                                 + $"'{already.Version}' and '{p.Version}'.",
                                                 nameof( repositories ) );
                }
                packages.Add( p.PackageId, p );
            }
        }
        return packages;
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
    /// Gets the repositories, ordered by their <see cref="RepositoryKey.Url"/>.
    /// </summary>
    public ImmutableArray<Repository> Repositories => _repositories;

    /// <summary>
    /// Gets all the packages indexed by their <see cref="PackageInstance.PackageId"/>
    /// (case insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, PackageInstance> Packages => _packages;

    /// <summary>
    /// Updater that transitions <see cref="IsDeprecated"/> to true.
    /// </summary>
    /// <returns>This profile or a new one.</returns>
    public PublishedProfile Deprecate() => _isDeprecated
                                                ? this
                                                : new PublishedProfile( this, true );

    /// <summary>
    /// Updater that <see cref="Deprecate()"/> this profile if the package appears in <see cref="Packages"/>.
    /// </summary>
    /// <param name="packageId">The deprecated package identifier.</param>
    /// <param name="version">The deprecated package version.</param>
    /// <returns>This profile or a new one.</returns>
    public PublishedProfile OnDeprecatedPackage( string packageId, SVersion version )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( packageId );
        ArgumentNullException.ThrowIfNull( version );
        return _isDeprecated || !_packages.TryGetValue( packageId, out var p ) || p.Version != version
                ? this
                : Deprecate();
    }

    /// <summary>
    /// Overridden to return the <see cref="World"/> and <see cref="Version"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => _toString ??= $"{_world.FullName}/v{_version}";
}
