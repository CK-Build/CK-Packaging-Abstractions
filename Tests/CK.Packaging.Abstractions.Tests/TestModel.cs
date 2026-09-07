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
    /// Creates a <see cref="VersionRequirement"/>. The <paramref name="targetFrameworks"/> are comma
    /// separated: the empty string is the single "any framework" entry, not an empty list.
    /// </summary>
    public static VersionRequirement Req( string version, string targetFrameworks, params string[] requiredBy )
    {
        return new VersionRequirement( SVersion.Parse( version ),
                                       Packages( requiredBy ),
                                       [.. targetFrameworks.Split( ',' )] );
    }

    /// <summary>
    /// Creates an <see cref="AmbiguousDependency"/> from the "packageId@version" it resolves to.
    /// </summary>
    public static AmbiguousDependency Ambiguous( string packageInstance,
                                                 VersionSource resolvedFrom,
                                                 params VersionRequirement[] requirements )
    {
        var p = Package( packageInstance );
        return new AmbiguousDependency( p.PackageId, p.Version, resolvedFrom, [.. requirements] );
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
    /// The reference closure: one regular dependency, one ambiguity of each <see cref="VersionSource"/>
    /// and one missing package. The <paramref name="version"/> is the profile's one: it is what the
    /// <see cref="VersionSource.ProducedPackages"/> ambiguity is anchored on.
    /// </summary>
    public static TransitiveDependencies SampleTransitiveDependencies( string version = "1.2.3" )
    {
        return new TransitiveDependencies(
                    Packages( "System.IO.Pipelines@9.0.0" ),
                    [
                        // Anchored on a direct dependency: an analyzer requires more than what the
                        // repositories reference.
                        Ambiguous( "System.Text.Json@9.0.0",
                                   VersionSource.DirectDependencies,
                                   Req( "10.0.0", "net10.0", "Some.Analyzer@2.1.0" ) ),
                        // Resolved from its own requirements: NuGet's highest-wins.
                        Ambiguous( "System.Text.Encodings.Web@9.0.0",
                                   VersionSource.TransitiveDependencies,
                                   Req( "8.0.0", ".NETStandard2.0", "Foo.Legacy@1.2.0", "Bar@3.0.0" ),
                                   Req( "9.0.0", "net8.0", "System.Text.Json@9.0.0" ) ),
                        // Anchored on a produced package: something requires a version this world
                        // has not produced.
                        Ambiguous( $"CK.Two@{version}",
                                   VersionSource.ProducedPackages,
                                   Req( "99.0.0", "net10.0", "Third.Party@1.0.0" ) )
                    ],
                    Packages( "Ghost.Package@0.1.0" ) );
    }

    /// <summary>
    /// Creates the reference profile: 2 repositories, 3 packages, 2 direct dependencies and the
    /// <see cref="SampleTransitiveDependencies"/> closure. The repositories, the packages and the
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
