using CK.Core;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace CK.Packaging.Abstractions.Tests;

/// <summary>
/// Helpers that build the model of the "CKt" test stack.
/// </summary>
static class TestModel
{
    public static readonly Uri StackUrl = new Uri( "https://github.com/Signature-Code/CKt-Stack" );

    public static readonly WorldName World = WorldName.Parse( "CKt" );

    /// <summary>
    /// Gets the "https://github.com/Signature-Code/CKt-{name}" url.
    /// </summary>
    public static Uri RepoUrl( string name ) => new Uri( $"https://github.com/Signature-Code/CKt-{name}" );

    /// <summary>
    /// Creates a repository from its name, its numerical identifier and its "packageId@version" packages.
    /// </summary>
    public static Repository Repo( string name, ulong id, params string[] packages )
    {
        return new Repository( new RepositoryKey( RepoUrl( name ), new RandomId( id ) ),
                               packages.Select( Package ).ToImmutableArray() );
    }

    /// <summary>
    /// Parses a "packageId@version" package instance.
    /// </summary>
    public static PackageInstance Package( string packageInstance )
    {
        return PackageInstance.TryParse( packageInstance, out var p )
                ? p
                : throw new ArgumentException( $"Invalid package instance '{packageInstance}'." );
    }

    /// <summary>
    /// Parses "packageId@version" package instances.
    /// </summary>
    public static ImmutableArray<PackageInstance> Packages( params string[] packageInstances )
    {
        return packageInstances.Select( Package ).ToImmutableArray();
    }

    /// <summary>
    /// Parses a Conformant SVersion.
    /// </summary>
    public static SVersion V( string version ) => SVersion.Parse( version, mustBeCSVersion: true );

    /// <summary>
    /// Creates a <see cref="VersionResolution"/> from the numerical identifiers of the repositories
    /// that resolved the <paramref name="version"/>.
    /// </summary>
    public static VersionResolution Res( string version, params ulong[] repositories )
    {
        return new VersionResolution( SVersion.Parse( version ),
                                      [.. repositories.Select( id => new RandomId( id ) )] );
    }

    /// <summary>
    /// Creates an <see cref="AmbiguousDependency"/> from the "packageId@version" it resolves to.
    /// </summary>
    public static AmbiguousDependency Ambiguous( string packageInstance,
                                                 VersionSource resolvedFrom,
                                                 params VersionResolution[] resolutions )
    {
        var p = Package( packageInstance );
        return new AmbiguousDependency( p.PackageId, p.Version, resolvedFrom, [.. resolutions] );
    }

    /// <summary>
    /// Creates a profile of the <see cref="World"/> in the <see cref="StackUrl"/> stack.
    /// </summary>
    public static PublishedProfile Profile( string version, params Repository[] repositories )
    {
        return new PublishedProfile( StackUrl, World, V( version ), [.. repositories] );
    }

    /// <summary>
    /// Creates a profile of the <see cref="World"/> in the <see cref="StackUrl"/> stack with its dependencies.
    /// </summary>
    public static PublishedProfile Profile( string version,
                                            ImmutableArray<PackageInstance> directDependencies,
                                            TransitiveDependencies? transitiveDependencies,
                                            params Repository[] repositories )
    {
        return new PublishedProfile( StackUrl,
                                     World,
                                     V( version ),
                                     [.. repositories],
                                     directDependencies,
                                     transitiveDependencies );
    }

    /// <summary>
    /// The reference direct dependencies: 2 external packages, deliberately not ordered.
    /// </summary>
    public static ImmutableArray<PackageInstance> SampleDirectDependencies
        => Packages( "System.Text.Json@9.0.0", "NUnit@4.2.2" );

    /// <summary>
    /// The reference transitive dependencies: one regular dependency and one ambiguity of each
    /// <see cref="VersionSource"/>. The repositories they name are the ones of <see cref="SampleProfile"/>
    /// (1 is "One" and 2 is "Two") and the <paramref name="version"/> is the profile's one: it is what the
    /// <see cref="VersionSource.ProducedPackages"/> ambiguity is anchored on.
    /// </summary>
    public static TransitiveDependencies SampleTransitiveDependencies( string version = "1.2.3" )
    {
        return new TransitiveDependencies(
                    Packages( "System.IO.Pipelines@9.0.0" ),
                    [
                        // Anchored on a direct dependency: a repository's restore resolved more than what
                        // the repositories reference.
                        Ambiguous( "System.Text.Json@9.0.0",
                                   VersionSource.DirectDependencies,
                                   Res( "10.0.0", 2 ) ),
                        // Resolved from the repositories themselves: NuGet's highest-wins. Repository 1
                        // appears in both resolutions: two of its target frameworks resolved differently.
                        Ambiguous( "System.Text.Encodings.Web@9.0.0",
                                   VersionSource.TransitiveDependencies,
                                   Res( "8.0.0", 1 ),
                                   Res( "9.0.0", 2, 1 ) ),
                        // Anchored on a produced package: a repository resolved a version this world
                        // has not produced.
                        Ambiguous( $"CK.Two@{version}",
                                   VersionSource.ProducedPackages,
                                   Res( "99.0.0", 1 ) )
                    ] );
    }

    /// <summary>
    /// Creates the reference profile: 2 repositories, 3 packages, 2 direct dependencies and the
    /// <see cref="SampleTransitiveDependencies"/>. The repositories, the packages and the
    /// dependencies are deliberately not ordered.
    /// </summary>
    public static PublishedProfile SampleProfile( string version = "1.2.3" )
    {
        return Profile( version,
                        SampleDirectDependencies,
                        SampleTransitiveDependencies( version ),
                        Repo( "Two", 2, $"CK.Two@{version}" ),
                        Repo( "One", 1, $"CK.One.Sub@{version}", $"CK.One@{version}" ) );
    }
}
