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
    /// Parses a Conformant SVersion.
    /// </summary>
    public static SVersion V( string version ) => SVersion.Parse( version, mustBeCSVersion: true );

    /// <summary>
    /// Creates a profile of the <see cref="World"/> in the <see cref="StackUrl"/> stack.
    /// </summary>
    public static PublishedProfile Profile( string version, params Repository[] repositories )
    {
        return new PublishedProfile( StackUrl, World, V( version ), [.. repositories] );
    }

    /// <summary>
    /// Creates the reference profile: 2 repositories, 3 packages, the repositories and the
    /// packages being deliberately not ordered.
    /// </summary>
    public static PublishedProfile SampleProfile( string version = "1.2.3" )
    {
        return Profile( version,
                        Repo( "Two", 2, $"CK.Two@{version}" ),
                        Repo( "One", 1, $"CK.One.Sub@{version}", $"CK.One@{version}" ) );
    }
}
