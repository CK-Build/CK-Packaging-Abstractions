using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// One version of a transitive dependency and the profile repositories whose restore resolved it.
/// <para>
/// This is an observation, not a requirement: the transitive packages of a publication come from NuGet's
/// own resolution ("dotnet package list --include-transitive"), which never says which package pulled an
/// identifier in. What it does say is which repository ended up with which version, and that is what a
/// disagreement has to name to be diagnosable.
/// </para>
/// </summary>
/// <param name="Version">
/// The resolved version. Any SemVer: an external package is not bound to CSemVer.
/// </param>
/// <param name="Repositories">
/// The <see cref="RepositoryKey.Id"/> of the profile repositories that resolved the <see cref="Version"/>,
/// in ascending order. Never empty and without duplicate.
/// </param>
public readonly record struct VersionResolution( SVersion Version, ImmutableArray<RandomId> Repositories )
{
    /// <summary>
    /// Structural equality: the synthesized one would compare the <see cref="ImmutableArray{T}"/>
    /// by reference (that is what <c>ImmutableArray</c>'s own equality does).
    /// </summary>
    /// <param name="other">The other resolution.</param>
    /// <returns>True if this resolution is the same as the other one, false otherwise.</returns>
    public bool Equals( VersionResolution other )
    {
        if( Version != other.Version ) return false;
        if( Repositories.IsDefault || other.Repositories.IsDefault )
        {
            return Repositories.IsDefault && other.Repositories.IsDefault;
        }
        if( Repositories.Length != other.Repositories.Length ) return false;
        for( int i = 0; i < Repositories.Length; ++i )
        {
            if( Repositories[i] != other.Repositories[i] ) return false;
        }
        return true;
    }

    /// <summary>
    /// Overridden to match <see cref="Equals(VersionResolution)"/>.
    /// </summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add( Version );
        if( !Repositories.IsDefault )
        {
            foreach( var id in Repositories ) h.Add( id );
        }
        return h.ToHashCode();
    }

    /// <summary>
    /// Overridden to return the version and the repositories that resolved it.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString()
    {
        return $"{Version} resolved by [{(Repositories.IsDefaultOrEmpty ? "" : string.Join( ", ", Repositories ))}]";
    }

    /// <summary>
    /// Writes this resolution as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteString( "Version", Version.ToString() );
        w.WriteStartArray( "Repositories" );
        foreach( var id in Repositories )
        {
            w.WriteStringValue( id.ToString() );
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a resolution written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The resolution.</returns>
    public static VersionResolution Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( VersionResolution ) );
        SVersion? version = null;
        ImmutableArray<RandomId>.Builder? repositories = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "Version":
                    version = JsonHelper.GetPackageVersion( ref r, name );
                    break;
                case "Repositories":
                    repositories = JsonHelper.ReadRandomIds( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( VersionResolution ) );
        return new VersionResolution( JsonHelper.Required( version, "Version", nameof( VersionResolution ) ),
                                      JsonHelper.Required( repositories, "Repositories", nameof( VersionResolution ) )
                                                .DrainToImmutable() );
    }
}

/// <summary>
/// A transitive dependency that the publication's repositories did not resolve to a single version, or whose
/// resolution disagrees with a <see cref="PublishedProfile.DirectDependencies"/> or
/// <see cref="PublishedProfile.ProducedPackages"/> entry. The inherited <see cref="PackageInstance.Version"/>
/// is what the profile resolves to and <see cref="ResolvedFrom"/> says where that version comes from.
/// <para>
/// Only the harmful direction is reported: a resolution below what the profile references or produces is
/// invisible to a restore, so an anchored ambiguity carries only the <see cref="Resolutions"/> that are
/// greater.
/// </para>
/// </summary>
public sealed class AmbiguousDependency : PackageInstance
{
    readonly VersionSource _resolvedFrom;
    readonly ImmutableArray<VersionResolution> _resolutions;

    /// <summary>
    /// Initializes a new ambiguous dependency.
    /// <para>
    /// The <paramref name="resolutions"/> are sorted by descending <see cref="VersionResolution.Version"/>, as
    /// are each resolution's <see cref="VersionResolution.Repositories"/>: an ambiguous dependency is always in
    /// a canonical form, whatever the order in which its resolutions have been discovered.
    /// </para>
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The version this profile resolves the <paramref name="packageId"/> to.</param>
    /// <param name="resolvedFrom">Where the <paramref name="version"/> comes from.</param>
    /// <param name="resolutions">
    /// The resolutions that disagree with the <paramref name="version"/>. Must not be default nor empty, must
    /// not carry the same version twice, and each of them must name at least one repository.
    /// <para>
    /// A repository CAN appear in two resolutions: its own restore resolves one identifier to two versions when
    /// two of its target frameworks resolve differently.
    /// </para>
    /// <para>
    /// When <paramref name="resolvedFrom"/> is <see cref="VersionSource.TransitiveDependencies"/> there must be
    /// at least 2 of them and the <paramref name="version"/> must be the greatest one. Otherwise they must all
    /// be greater than the <paramref name="version"/>.
    /// </para>
    /// </param>
    public AmbiguousDependency( string packageId,
                                SVersion version,
                                VersionSource resolvedFrom,
                                ImmutableArray<VersionResolution> resolutions )
        : base( packageId, version )
    {
        if( resolvedFrom is not VersionSource.TransitiveDependencies
                         and not VersionSource.DirectDependencies
                         and not VersionSource.ProducedPackages )
        {
            throw new ArgumentException( $"Invalid VersionSource '{(int)resolvedFrom}'.", nameof( resolvedFrom ) );
        }
        if( resolutions.IsDefault )
        {
            throw new ArgumentException( "Resolutions must be initialized.", nameof( resolutions ) );
        }
        if( resolutions.IsEmpty )
        {
            throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' has no resolution: "
                                         + "an ambiguity is a disagreement, so at least one is required.",
                                         nameof( resolutions ) );
        }
        resolutions = Canonicalize( packageId, version, resolutions );
        if( resolvedFrom == VersionSource.TransitiveDependencies )
        {
            // Nothing outside the closure anchors this identifier: NuGet's highest-wins applies across the
            // packages a consumer takes together, so the resolved version is the greatest resolution. A
            // single resolution is no disagreement at all: such an identifier belongs to
            // TransitiveDependencies.Regular.
            if( resolutions.Length < 2 )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' is resolved from its own "
                                             + $"resolutions but has only one ('{resolutions[0].Version}'): it is a "
                                             + "regular transitive dependency.",
                                             nameof( resolutions ) );
            }
            if( resolutions[0].Version != version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' is resolved from its own "
                                             + "resolutions: its version must be the greatest of them, which is "
                                             + $"'{resolutions[0].Version}'.",
                                             nameof( version ) );
            }
        }
        else
        {
            // Anchored on DirectDependencies or ProducedPackages: only the harmful direction is kept, so
            // every resolution is strictly greater than what the profile resolves to. Resolutions are
            // sorted by descending version: the last one is the smallest.
            var smallest = resolutions[^1].Version;
            if( smallest <= version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' is anchored on "
                                             + $"{resolvedFrom}: its resolutions must all be greater than "
                                             + $"'{version}', but '{smallest}' is not.",
                                             nameof( resolutions ) );
            }
        }
        _resolvedFrom = resolvedFrom;
        _resolutions = resolutions;
    }

    // Sorts the resolutions (and their content) and checks them.
    static ImmutableArray<VersionResolution> Canonicalize( string packageId,
                                                           SVersion version,
                                                           ImmutableArray<VersionResolution> resolutions )
    {
        var b = ImmutableArray.CreateBuilder<VersionResolution>( resolutions.Length );
        foreach( var res in resolutions )
        {
            if( res.Version is null )
            {
                throw new ArgumentException( $"Resolution of '{packageId}@{version}' has no version.",
                                             nameof( resolutions ) );
            }
            if( res.Repositories.IsDefault || res.Repositories.IsEmpty )
            {
                throw new ArgumentException( $"Resolution '{packageId}@{res.Version}' has no repository: a "
                                             + "resolution comes from at least one repository.",
                                             nameof( resolutions ) );
            }
            var repositories = res.Repositories.Sort();
            for( int i = 0; i < repositories.Length; ++i )
            {
                var id = repositories[i];
                if( !id.IsValid )
                {
                    throw new ArgumentException( $"Resolution '{packageId}@{res.Version}' names an invalid "
                                                 + "repository identifier.",
                                                 nameof( resolutions ) );
                }
                if( i > 0 && repositories[i - 1] == id )
                {
                    throw new ArgumentException( $"Resolution '{packageId}@{res.Version}' names the repository "
                                                 + $"'{id}' more than once.",
                                                 nameof( resolutions ) );
                }
            }
            b.Add( new VersionResolution( res.Version, repositories ) );
        }
        var sorted = b.DrainToImmutable().Sort( static ( r1, r2 ) => r2.Version.CompareTo( r1.Version ) );
        for( int i = 1; i < sorted.Length; ++i )
        {
            if( sorted[i].Version == sorted[i - 1].Version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}' resolves '{sorted[i].Version}' "
                                             + "more than once: such resolutions must be merged.",
                                             nameof( resolutions ) );
            }
        }
        return sorted;
    }

    /// <summary>
    /// Gets where the resolved <see cref="PackageInstance.Version"/> comes from.
    /// </summary>
    public VersionSource ResolvedFrom => _resolvedFrom;

    /// <summary>
    /// Gets the resolutions that disagree with the resolved <see cref="PackageInstance.Version"/>, ordered by
    /// descending <see cref="VersionResolution.Version"/>. Never empty.
    /// </summary>
    public ImmutableArray<VersionResolution> Resolutions => _resolutions;

    /// <summary>
    /// Writes this ambiguous dependency as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        // The resolved instance is written as one field so that a consumer that wants only the
        // effective closure reads the same place whatever the anchor.
        w.WriteString( "Package", ToString() );
        w.WriteString( "ResolvedFrom", _resolvedFrom.ToString() );
        w.WriteStartArray( "Resolutions" );
        foreach( var res in _resolutions )
        {
            res.Write( w );
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads an ambiguous dependency written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The ambiguous dependency.</returns>
    public static AmbiguousDependency Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( AmbiguousDependency ) );
        PackageInstance? package = null;
        VersionSource? resolvedFrom = null;
        ImmutableArray<VersionResolution>.Builder? resolutions = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "Package":
                    package = JsonHelper.GetPackageInstance( ref r, name );
                    break;
                case "ResolvedFrom":
                    resolvedFrom = JsonHelper.GetVersionSource( ref r, name );
                    break;
                case "Resolutions":
                    JsonHelper.EnsureStartArray( ref r, name );
                    resolutions = ImmutableArray.CreateBuilder<VersionResolution>();
                    while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                    {
                        resolutions.Add( VersionResolution.Read( ref r ) );
                    }
                    JsonHelper.EnsureEndArray( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( AmbiguousDependency ) );
        package = JsonHelper.Required( package, "Package", nameof( AmbiguousDependency ) );
        if( resolvedFrom == null )
        {
            throw new JsonException( $"Missing 'ResolvedFrom' property in '{nameof( AmbiguousDependency )}'." );
        }
        return new AmbiguousDependency( package.PackageId,
                                        package.Version,
                                        resolvedFrom.Value,
                                        JsonHelper.Required( resolutions,
                                                             "Resolutions",
                                                             nameof( AmbiguousDependency ) )
                                                  .DrainToImmutable() );
    }
}
