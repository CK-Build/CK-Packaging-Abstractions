using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace CK.Packaging.Abstractions;

/// <summary>
/// Immutable set of package instances that have been produced by a CKli world and are coherent: their dependencies
/// are homogeneous, no discrepancy exists among them.
/// <para>
/// Note that the coherency can be only a shallow one. deeper, transitive, dependencies are not guaranteed to be aligned.
/// </para>
/// <para>
/// A profile carries at most one version of a package identifier: this is checked by the constructor, for
/// its <see cref="ProducedPackages"/> as well as for its <see cref="DirectDependencies"/>.
/// </para>
/// <para>
/// Beyond what its repositories produce, a profile can also describe what they consume: the
/// <see cref="DirectDependencies"/> are the external packages they reference and the
/// <see cref="TransitiveDependencies"/> are the closure of those. Both are optional - an empty
/// <see cref="TransitiveDependencies"/> says nothing about the closure, not that there is none.
/// </para>
/// </summary>
public sealed partial class PublishedProfile
{
    readonly Uri _stackUrl;
    readonly WorldName _world;
    readonly SVersion _version;
    readonly ImmutableArray<Repository> _repositories;
    readonly Dictionary<string, PackageInstance> _producedPackages;
    readonly ImmutableArray<PackageInstance> _directDependencies;
    readonly TransitiveDependencies _transitiveDependencies;
    readonly bool _isDeprecated;
    string? _toString;

    /// <summary>
    /// Initializes a new profile.
    /// <para>
    /// The <paramref name="repositories"/> are sorted by their <see cref="RepositoryKey.Url"/>: a profile
    /// is always in a canonical form, whatever the order in which its repositories have been discovered.
    /// The <paramref name="directDependencies"/> are sorted the same way, by
    /// <see cref="PackageInstance.PackageId"/>.
    /// </para>
    /// </summary>
    /// <param name="stackUrl">The stack repository url. Must be absolute.</param>
    /// <param name="world">The world name.</param>
    /// <param name="version">The profile version. Must be a Conformant SVersion.</param>
    /// <param name="repositories">
    /// The repositories. Must not be default, must not contain the same <see cref="RepositoryKey.Url"/>
    /// or <see cref="RepositoryKey.Id"/> twice and no package identifier can appear in more than one of them.
    /// </param>
    /// <param name="directDependencies">
    /// The packages consumed by at least one repository, minus the produced ones. Must not contain the same
    /// <see cref="PackageInstance.PackageId"/> twice nor any identifier of <see cref="ProducedPackages"/>.
    /// Defaults to empty.
    /// </param>
    /// <param name="transitiveDependencies">
    /// The closure of the <paramref name="directDependencies"/>. Its
    /// <see cref="TransitiveDependencies.Regular"/> must share no identifier with the
    /// <paramref name="directDependencies"/> nor with <see cref="ProducedPackages"/>, and each of its
    /// <see cref="TransitiveDependencies.Ambiguous"/> must agree with its own
    /// <see cref="AmbiguousDependency.ResolvedFrom"/>. Defaults to
    /// <see cref="Abstractions.TransitiveDependencies.Empty"/>.
    /// </param>
    /// <param name="isDeprecated">Whether this profile is deprecated.</param>
    public PublishedProfile( Uri stackUrl,
                             WorldName world,
                             SVersion version,
                             ImmutableArray<Repository> repositories,
                             ImmutableArray<PackageInstance> directDependencies = default,
                             TransitiveDependencies? transitiveDependencies = null,
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
        _producedPackages = CreatePackageIndex( _repositories );
        _directDependencies = CreateDirectDependencies( directDependencies, _producedPackages, out var directIndex );
        _transitiveDependencies = transitiveDependencies ?? TransitiveDependencies.Empty;
        CheckTransitiveDependencies( _transitiveDependencies, directIndex, _producedPackages );
        _stackUrl = stackUrl;
        _world = world;
        _version = version;
        _isDeprecated = isDeprecated;
    }

    // Copy constructor used by the updaters: the repositories, the package index and the dependencies
    // are shared.
    PublishedProfile( PublishedProfile o, bool isDeprecated )
    {
        _stackUrl = o._stackUrl;
        _world = o._world;
        _version = o._version;
        _repositories = o._repositories;
        _producedPackages = o._producedPackages;
        _directDependencies = o._directDependencies;
        _transitiveDependencies = o._transitiveDependencies;
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

    // Sorts the direct dependencies, checks their coherency and indexes them: a direct dependency is
    // consumed by at least one repository and produced by none of them.
    static ImmutableArray<PackageInstance> CreateDirectDependencies( ImmutableArray<PackageInstance> directDependencies,
                                                                     Dictionary<string, PackageInstance> produced,
                                                                     out Dictionary<string, PackageInstance> index )
    {
        index = new Dictionary<string, PackageInstance>( StringComparer.OrdinalIgnoreCase );
        // A default array is an empty one: a profile written before the dependencies existed carries none.
        if( directDependencies.IsDefault ) return [];
        // PackageInstance's comparison is by PackageId (case insensitive) then by Version: a duplicate
        // identifier can only be adjacent in the sorted array.
        directDependencies = directDependencies.Sort();
        for( int i = 0; i < directDependencies.Length; ++i )
        {
            var p = directDependencies[i];
            if( p is null )
            {
                throw new ArgumentException( $"Null direct dependency at index {i}.", nameof( directDependencies ) );
            }
            if( i > 0 && StringComparer.OrdinalIgnoreCase.Equals( directDependencies[i - 1].PackageId, p.PackageId ) )
            {
                throw new ArgumentException( $"Direct dependency '{p.PackageId}' appears more than once in the "
                                             + $"profile: '{directDependencies[i - 1].Version}' and '{p.Version}'.",
                                             nameof( directDependencies ) );
            }
            if( produced.TryGetValue( p.PackageId, out var producedPackage ) )
            {
                throw new ArgumentException( $"Direct dependency '{p}' is produced by this profile "
                                             + $"('{producedPackage}'): the direct dependencies are the consumed "
                                             + "packages minus the produced ones.",
                                             nameof( directDependencies ) );
            }
            index.Add( p.PackageId, p );
        }
        return directDependencies;
    }

    // Checks the closure against the two anchors it can name.
    static void CheckTransitiveDependencies( TransitiveDependencies transitiveDependencies,
                                             Dictionary<string, PackageInstance> direct,
                                             Dictionary<string, PackageInstance> produced )
    {
        foreach( var p in transitiveDependencies.Regular )
        {
            if( produced.TryGetValue( p.PackageId, out var anchor ) )
            {
                throw new ArgumentException( $"Regular transitive dependency '{p}' is produced by this profile "
                                             + $"('{anchor}'): a produced identifier can only appear in the closure "
                                             + "as an ambiguity anchored on ProducedPackages.",
                                             nameof( transitiveDependencies ) );
            }
            if( direct.TryGetValue( p.PackageId, out anchor ) )
            {
                throw new ArgumentException( $"Regular transitive dependency '{p}' is a direct dependency "
                                             + $"('{anchor}'): a direct identifier can only appear in the closure "
                                             + "as an ambiguity anchored on DirectDependencies.",
                                             nameof( transitiveDependencies ) );
            }
        }
        foreach( var a in transitiveDependencies.Ambiguous )
        {
            switch( a.ResolvedFrom )
            {
                case VersionSource.DirectDependencies:
                    CheckAnchor( a, direct, "DirectDependencies" );
                    break;
                case VersionSource.ProducedPackages:
                    CheckAnchor( a, produced, "ProducedPackages" );
                    break;
                default:
                    CheckNoAnchor( a, direct, "a direct dependency", "DirectDependencies" );
                    CheckNoAnchor( a, produced, "produced by this profile", "ProducedPackages" );
                    break;
            }
        }

        // These two are not static: they close over the constructor's parameter name.
        void CheckAnchor( AmbiguousDependency a, Dictionary<string, PackageInstance> anchors, string source )
        {
            if( !anchors.TryGetValue( a.PackageId, out var anchor ) )
            {
                throw new ArgumentException( $"Ambiguous dependency '{a}' is resolved from {source} but no entry of "
                                             + $"{source} has this identifier.",
                                             nameof( transitiveDependencies ) );
            }
            if( anchor.Version != a.Version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{a}' is resolved from {source} but the entry "
                                             + $"of {source} is '{anchor}'.",
                                             nameof( transitiveDependencies ) );
            }
        }

        void CheckNoAnchor( AmbiguousDependency a,
                            Dictionary<string, PackageInstance> anchors,
                            string what,
                            string source )
        {
            if( anchors.TryGetValue( a.PackageId, out var anchor ) )
            {
                throw new ArgumentException( $"Ambiguous dependency '{a}' is resolved from its own requirements but "
                                             + $"'{anchor}' is {what}: it must be resolved from {source}.",
                                             nameof( transitiveDependencies ) );
            }
        }
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
    /// Gets all the packages produced by the <see cref="Repositories"/> indexed by their
    /// <see cref="PackageInstance.PackageId"/> (case insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, PackageInstance> ProducedPackages => _producedPackages;

    /// <summary>
    /// Gets the packages consumed by at least one <see cref="Repositories"/> that none of them produces,
    /// ordered by <see cref="PackageInstance.PackageId"/> (case insensitive). Empty when this profile
    /// carries no dependency information.
    /// </summary>
    public ImmutableArray<PackageInstance> DirectDependencies => _directDependencies;

    /// <summary>
    /// Gets the closure of the <see cref="DirectDependencies"/>' own dependencies. Never null: it is
    /// <see cref="Abstractions.TransitiveDependencies.Empty"/> when this profile carries no dependency
    /// information.
    /// </summary>
    public TransitiveDependencies TransitiveDependencies => _transitiveDependencies;

    /// <summary>
    /// Updater that transitions <see cref="IsDeprecated"/> to true.
    /// </summary>
    /// <returns>This profile or a new one.</returns>
    public PublishedProfile Deprecate() => _isDeprecated
                                                ? this
                                                : new PublishedProfile( this, true );

    /// <summary>
    /// Updater that <see cref="Deprecate()"/> this profile if the package appears in <see cref="ProducedPackages"/>.
    /// </summary>
    /// <param name="packageId">The deprecated package identifier.</param>
    /// <param name="version">The deprecated package version.</param>
    /// <returns>This profile or a new one.</returns>
    public PublishedProfile OnDeprecatedPackage( string packageId, SVersion version )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( packageId );
        ArgumentNullException.ThrowIfNull( version );
        return _isDeprecated || !_producedPackages.TryGetValue( packageId, out var p ) || p.Version != version
                ? this
                : Deprecate();
    }

    /// <summary>
    /// Overridden to return the <see cref="World"/> and <see cref="Version"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => _toString ??= $"{_world.FullName}/v{_version}";

    /// <summary>
    /// Gets a path that identifies or locates a profile representation of a version.
    /// </summary>
    /// <param name="version">The version. Must be a Conformant SVersion.</param>
    /// <param name="prefix">Optional prefix. Typically ends with <paramref name="directorySeparator"/>.</param>
    /// <param name="suffix">Optional suffix. Typically an extension (".json").</param>
    /// <param name="directorySeparator">Directory separator to use.</param>
    /// <returns>The associated path.</returns>
    public static string GetProfilePath( SVersion version, ReadOnlySpan<char> prefix = default, ReadOnlySpan<char> suffix = default, char directorySeparator = '/' )
    {
        ArgumentNullException.ThrowIfNull( version );
        var branchName = version.BranchName;
        if( branchName == null )
        {
            throw new ArgumentException( $"Version '{version}' must be a Conformant SVersion.", nameof( version ) );
        }
        // BranchName is the empty string for stable versions (and their CI builds) and can
        // contain a '/' for exploratory versions ("explo/{name}").
        return branchName.Length == 0
                ? $"{prefix}v{version}{suffix}"
                : $"{prefix}{branchName.Replace( '/', directorySeparator )}{directorySeparator}v{version}{suffix}";
    }

}
