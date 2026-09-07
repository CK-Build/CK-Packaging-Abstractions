using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// One required version of a package identifier, the closure members that require it and the target
/// frameworks under which they do (the union across the <see cref="RequiredBy"/>).
/// </summary>
/// <param name="Version">
/// The required version. Any SemVer: an external package is not bound to CSemVer.
/// </param>
/// <param name="RequiredBy">
/// The packages that require the <see cref="Version"/>. Never empty and without duplicate.
/// </param>
/// <param name="TargetFrameworks">
/// The target frameworks under which the <see cref="RequiredBy"/> require the <see cref="Version"/>,
/// as they appear in the nuspec files (".NETStandard2.0", "net10.0", ...). The empty string stands
/// for "any framework". Without duplicate, but can be empty.
/// </param>
public readonly record struct VersionRequirement( SVersion Version,
                                                  ImmutableArray<PackageInstance> RequiredBy,
                                                  ImmutableArray<string> TargetFrameworks )
{
    /// <summary>
    /// Structural equality: the synthesized one would compare the two <see cref="ImmutableArray{T}"/>
    /// by reference (that is what <c>ImmutableArray</c>'s own equality does).
    /// </summary>
    /// <param name="other">The other requirement.</param>
    /// <returns>True if this requirement is the same as the other one, false otherwise.</returns>
    public bool Equals( VersionRequirement other )
    {
        return Version == other.Version
               && SeqEqual( RequiredBy, other.RequiredBy, EqualityComparer<PackageInstance>.Default )
               && SeqEqual( TargetFrameworks, other.TargetFrameworks, StringComparer.Ordinal );

        static bool SeqEqual<T>( ImmutableArray<T> a, ImmutableArray<T> b, IEqualityComparer<T> comparer )
        {
            if( a.IsDefault || b.IsDefault ) return a.IsDefault && b.IsDefault;
            if( a.Length != b.Length ) return false;
            for( int i = 0; i < a.Length; ++i )
            {
                if( !comparer.Equals( a[i], b[i] ) ) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Overridden to match <see cref="Equals(VersionRequirement)"/>.
    /// </summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add( Version );
        if( !RequiredBy.IsDefault )
        {
            foreach( var p in RequiredBy ) h.Add( p );
        }
        if( !TargetFrameworks.IsDefault )
        {
            foreach( var f in TargetFrameworks ) h.Add( f, StringComparer.Ordinal );
        }
        return h.ToHashCode();
    }

    /// <summary>
    /// Overridden to return the version, its requesters and their frameworks.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString()
    {
        return $"{Version} required by [{Join( RequiredBy )}] for [{Join( TargetFrameworks )}]";

        static string Join<T>( ImmutableArray<T> a ) => a.IsDefaultOrEmpty ? "" : string.Join( ", ", a );
    }

    /// <summary>
    /// Writes this requirement as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteString( "Version", Version.ToString() );
        w.WriteStartArray( "RequiredBy" );
        foreach( var p in RequiredBy )
        {
            w.WriteStringValue( p.ToString() );
        }
        w.WriteEndArray();
        w.WriteStartArray( "TargetFrameworks" );
        foreach( var f in TargetFrameworks )
        {
            w.WriteStringValue( f );
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a requirement written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The requirement.</returns>
    public static VersionRequirement Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( VersionRequirement ) );
        SVersion? version = null;
        ImmutableArray<PackageInstance>.Builder? requiredBy = null;
        ImmutableArray<string>.Builder? targetFrameworks = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "Version":
                    version = JsonHelper.GetPackageVersion( ref r, name );
                    break;
                case "RequiredBy":
                    requiredBy = JsonHelper.ReadPackageInstances( ref r, name );
                    break;
                case "TargetFrameworks":
                    JsonHelper.EnsureStartArray( ref r, name );
                    targetFrameworks = ImmutableArray.CreateBuilder<string>();
                    while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                    {
                        targetFrameworks.Add( JsonHelper.GetString( ref r, name ) );
                    }
                    JsonHelper.EnsureEndArray( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( VersionRequirement ) );
        return new VersionRequirement( JsonHelper.Required( version, "Version", nameof( VersionRequirement ) ),
                                       JsonHelper.Required( requiredBy, "RequiredBy", nameof( VersionRequirement ) )
                                                 .DrainToImmutable(),
                                       JsonHelper.Required( targetFrameworks,
                                                            "TargetFrameworks",
                                                            nameof( VersionRequirement ) )
                                                 .DrainToImmutable() );
    }
}

/// <summary>
/// A transitive dependency whose required versions disagree, or which disagrees with a
/// <see cref="PublishedProfile.DirectDependencies"/> or <see cref="PublishedProfile.ProducedPackages"/>
/// entry. The inherited <see cref="PackageInstance.Version"/> is what the profile resolves to and
/// <see cref="ResolvedFrom"/> says where that version comes from.
/// <para>
/// Only the harmful direction is reported: a transitive requirement below what the profile references
/// or produces is invisible to a restore, so an anchored ambiguity carries only the
/// <see cref="Requirements"/> that ask for more.
/// </para>
/// </summary>
public sealed class AmbiguousDependency : PackageInstance
{
    readonly VersionSource _resolvedFrom;
    readonly ImmutableArray<VersionRequirement> _requirements;

    /// <summary>
    /// Initializes a new ambiguous dependency.
    /// <para>
    /// The <paramref name="requirements"/> are sorted by descending <see cref="VersionRequirement.Version"/>,
    /// as are each requirement's <see cref="VersionRequirement.RequiredBy"/> and
    /// <see cref="VersionRequirement.TargetFrameworks"/>: an ambiguous dependency is always in a canonical
    /// form, whatever the order in which its requirements have been discovered.
    /// </para>
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The version this profile resolves the <paramref name="packageId"/> to.</param>
    /// <param name="resolvedFrom">Where the <paramref name="version"/> comes from.</param>
    /// <param name="requirements">
    /// The transitive requirements that disagree with the <paramref name="version"/>. Must not be default
    /// nor empty, must not require the same version twice, and each of them must have at least one
    /// <see cref="VersionRequirement.RequiredBy"/>.
    /// <para>
    /// When <paramref name="resolvedFrom"/> is <see cref="VersionSource.TransitiveDependencies"/> there
    /// must be at least 2 of them and the <paramref name="version"/> must be the greatest one. Otherwise
    /// they must all be greater than the <paramref name="version"/>.
    /// </para>
    /// </param>
    public AmbiguousDependency( string packageId,
                                SVersion version,
                                VersionSource resolvedFrom,
                                ImmutableArray<VersionRequirement> requirements )
        : base( packageId, version )
    {
        if( resolvedFrom is not VersionSource.TransitiveDependencies
                         and not VersionSource.DirectDependencies
                         and not VersionSource.ProducedPackages )
        {
            throw new ArgumentException( $"Invalid VersionSource '{(int)resolvedFrom}'.", nameof( resolvedFrom ) );
        }
        if( requirements.IsDefault )
        {
            throw new ArgumentException( "Requirements must be initialized.", nameof( requirements ) );
        }
        if( requirements.IsEmpty )
        {
            throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' has no requirement: "
                                         + "an ambiguity is a disagreement, so at least one is required.",
                                         nameof( requirements ) );
        }
        requirements = Canonicalize( packageId, version, requirements );
        if( resolvedFrom == VersionSource.TransitiveDependencies )
        {
            // Nothing outside the closure anchors this identifier: NuGet's highest-wins applies, so the
            // resolved version is the greatest requirement. A single requirement resolves without any
            // disagreement at all: such an identifier belongs to TransitiveDependencies.Regular.
            if( requirements.Length < 2 )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' is resolved from its own "
                                             + $"requirements but has only one ('{requirements[0].Version}'): it is a "
                                             + "regular transitive dependency.",
                                             nameof( requirements ) );
            }
            if( requirements[0].Version != version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' is resolved from its own "
                                             + "requirements: its version must be the greatest of them, which is "
                                             + $"'{requirements[0].Version}'.",
                                             nameof( version ) );
            }
        }
        else
        {
            // Anchored on DirectDependencies or ProducedPackages: only the harmful direction is kept, so
            // every requirement asks for strictly more than what the profile resolves to. Requirements
            // are sorted by descending version: the last one is the smallest.
            var smallest = requirements[^1].Version;
            if( smallest <= version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}@{version}' is anchored on "
                                             + $"{resolvedFrom}: its requirements must all be greater than "
                                             + $"'{version}', but '{smallest}' is not.",
                                             nameof( requirements ) );
            }
        }
        _resolvedFrom = resolvedFrom;
        _requirements = requirements;
    }

    // Sorts the requirements (and their content) and checks them.
    static ImmutableArray<VersionRequirement> Canonicalize( string packageId,
                                                            SVersion version,
                                                            ImmutableArray<VersionRequirement> requirements )
    {
        var b = ImmutableArray.CreateBuilder<VersionRequirement>( requirements.Length );
        foreach( var req in requirements )
        {
            if( req.Version is null )
            {
                throw new ArgumentException( $"Requirement of '{packageId}@{version}' has no version.",
                                             nameof( requirements ) );
            }
            if( req.RequiredBy.IsDefault || req.RequiredBy.IsEmpty )
            {
                throw new ArgumentException( $"Requirement '{packageId}@{req.Version}' has no RequiredBy: a "
                                             + "requirement comes from at least one package.",
                                             nameof( requirements ) );
            }
            if( req.TargetFrameworks.IsDefault )
            {
                throw new ArgumentException( $"Requirement '{packageId}@{req.Version}' has no TargetFrameworks: "
                                             + "the array must be initialized (it can be empty).",
                                             nameof( requirements ) );
            }
            // PackageInstance's comparison is by PackageId (case insensitive) then by Version: a
            // duplicate can only be adjacent in the sorted array.
            var requiredBy = req.RequiredBy.Sort();
            for( int i = 0; i < requiredBy.Length; ++i )
            {
                var p = requiredBy[i];
                if( p is null )
                {
                    throw new ArgumentException( $"Requirement '{packageId}@{req.Version}' has a null RequiredBy.",
                                                 nameof( requirements ) );
                }
                if( i > 0 && requiredBy[i - 1] == p )
                {
                    throw new ArgumentException( $"Requirement '{packageId}@{req.Version}' is required by '{p}' "
                                                 + "more than once.",
                                                 nameof( requirements ) );
                }
            }
            var targetFrameworks = req.TargetFrameworks.Sort( StringComparer.Ordinal );
            for( int i = 0; i < targetFrameworks.Length; ++i )
            {
                var f = targetFrameworks[i];
                if( f is null )
                {
                    throw new ArgumentException( $"Requirement '{packageId}@{req.Version}' has a null "
                                                 + "TargetFrameworks.",
                                                 nameof( requirements ) );
                }
                if( i > 0 && string.Equals( targetFrameworks[i - 1], f, StringComparison.Ordinal ) )
                {
                    throw new ArgumentException( $"Requirement '{packageId}@{req.Version}' carries the target "
                                                 + $"framework '{f}' more than once.",
                                                 nameof( requirements ) );
                }
            }
            b.Add( new VersionRequirement( req.Version, requiredBy, targetFrameworks ) );
        }
        var sorted = b.DrainToImmutable().Sort( static ( r1, r2 ) => r2.Version.CompareTo( r1.Version ) );
        for( int i = 1; i < sorted.Length; ++i )
        {
            if( sorted[i].Version == sorted[i - 1].Version )
            {
                throw new ArgumentException( $"Ambiguous dependency '{packageId}' requires '{sorted[i].Version}' "
                                             + "more than once: such requirements must be merged.",
                                             nameof( requirements ) );
            }
        }
        return sorted;
    }

    /// <summary>
    /// Gets where the resolved <see cref="PackageInstance.Version"/> comes from.
    /// </summary>
    public VersionSource ResolvedFrom => _resolvedFrom;

    /// <summary>
    /// Gets the transitive requirements that disagree with the resolved <see cref="PackageInstance.Version"/>,
    /// ordered by descending <see cref="VersionRequirement.Version"/>. Never empty.
    /// </summary>
    public ImmutableArray<VersionRequirement> Requirements => _requirements;

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
        w.WriteStartArray( "Requirements" );
        foreach( var req in _requirements )
        {
            req.Write( w );
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
        ImmutableArray<VersionRequirement>.Builder? requirements = null;
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
                case "Requirements":
                    JsonHelper.EnsureStartArray( ref r, name );
                    requirements = ImmutableArray.CreateBuilder<VersionRequirement>();
                    while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                    {
                        requirements.Add( VersionRequirement.Read( ref r ) );
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
                                        JsonHelper.Required( requirements,
                                                             "Requirements",
                                                             nameof( AmbiguousDependency ) )
                                                  .DrainToImmutable() );
    }
}
