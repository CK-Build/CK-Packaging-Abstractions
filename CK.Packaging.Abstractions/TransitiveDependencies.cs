using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// The closure of a <see cref="PublishedProfile.DirectDependencies"/>' own dependencies: what a restore
/// of this profile's packages actually brings, beyond what the profile references explicitly.
/// <para>
/// <see cref="Regular"/> and <see cref="Ambiguous"/> partition the closure by package identifier - the
/// two never share one - so a consumer that ignores the details can read their union as a flat coherent
/// set of resolved package instances. <see cref="Missing"/> is not a fourth list of its own packages: it
/// marks the instances whose nuspec could not be found, and such an instance also appears in
/// <see cref="Regular"/> or <see cref="Ambiguous"/> unless it is a direct dependency.
/// </para>
/// </summary>
public sealed class TransitiveDependencies
{
    readonly ImmutableArray<PackageInstance> _regular;
    readonly ImmutableArray<AmbiguousDependency> _ambiguous;
    readonly ImmutableArray<PackageInstance> _missing;

    /// <summary>
    /// The empty closure. This is what a profile that carries no dependency information exposes.
    /// </summary>
    public static readonly TransitiveDependencies Empty = new TransitiveDependencies( [], [], [] );

    /// <summary>
    /// Initializes a new closure.
    /// <para>
    /// The three lists are sorted by <see cref="PackageInstance.PackageId"/> (case insensitive) then by
    /// <see cref="PackageInstance.Version"/>: a closure is always in a canonical form, whatever the order
    /// in which its packages have been discovered.
    /// </para>
    /// </summary>
    /// <param name="regular">
    /// The transitive dependencies that resolve to one version. Must not be default and must not contain
    /// the same <see cref="PackageInstance.PackageId"/> more than once.
    /// </param>
    /// <param name="ambiguous">
    /// The transitive dependencies whose required versions disagree. Must not be default, must not contain
    /// the same <see cref="PackageInstance.PackageId"/> more than once and must share no identifier with
    /// <paramref name="regular"/>.
    /// </param>
    /// <param name="missing">
    /// The required instances that are absent from the NuGet cache: their own dependencies are unknown.
    /// Must not be default and must not contain the same instance more than once.
    /// </param>
    public TransitiveDependencies( ImmutableArray<PackageInstance> regular,
                                   ImmutableArray<AmbiguousDependency> ambiguous,
                                   ImmutableArray<PackageInstance> missing )
    {
        CheckNoNull( regular, nameof( regular ) );
        CheckNoNull( ambiguous, nameof( ambiguous ) );
        CheckNoNull( missing, nameof( missing ) );
        // PackageInstance's comparison is by PackageId (case insensitive) then by Version: a duplicate
        // identifier can only be adjacent in the sorted arrays.
        regular = regular.Sort();
        ambiguous = ambiguous.Sort( static ( a1, a2 ) => a1.CompareTo( a2 ) );
        missing = missing.Sort();
        CheckNoDuplicateId( regular, "Regular", nameof( regular ) );
        CheckNoDuplicateId( ambiguous, "Ambiguous", nameof( ambiguous ) );
        for( int i = 1; i < missing.Length; ++i )
        {
            if( missing[i - 1] == missing[i] )
            {
                throw new ArgumentException( $"Missing dependency '{missing[i]}' appears more than once.",
                                             nameof( missing ) );
            }
        }
        // Both lists are sorted, but by an ordering that Version participates in: a set is required
        // here rather than a merge walk.
        var regularIds = new HashSet<string>( regular.Length, StringComparer.OrdinalIgnoreCase );
        foreach( var p in regular )
        {
            regularIds.Add( p.PackageId );
        }
        foreach( var a in ambiguous )
        {
            if( regularIds.Contains( a.PackageId ) )
            {
                throw new ArgumentException( $"Transitive dependency '{a.PackageId}' is both regular and ambiguous: "
                                             + "one identifier resolves to one version, ambiguous or not.",
                                             nameof( ambiguous ) );
            }
        }
        _regular = regular;
        _ambiguous = ambiguous;
        _missing = missing;

        static void CheckNoNull<T>( ImmutableArray<T> a, string name ) where T : PackageInstance
        {
            if( a.IsDefault )
            {
                throw new ArgumentException( "Must be initialized.", name );
            }
            for( int i = 0; i < a.Length; ++i )
            {
                if( a[i] is null )
                {
                    throw new ArgumentException( $"Null package at index {i}.", name );
                }
            }
        }

        static void CheckNoDuplicateId<T>( ImmutableArray<T> a, string listName, string name ) where T : PackageInstance
        {
            for( int i = 1; i < a.Length; ++i )
            {
                if( StringComparer.OrdinalIgnoreCase.Equals( a[i - 1].PackageId, a[i].PackageId ) )
                {
                    throw new ArgumentException( $"{listName} transitive dependency '{a[i].PackageId}' appears more "
                                                 + $"than once: '{a[i - 1].Version}' and '{a[i].Version}'.",
                                                 name );
                }
            }
        }
    }

    /// <summary>
    /// Gets the transitive dependencies that resolve to one version, ordered by
    /// <see cref="PackageInstance.PackageId"/> (case insensitive).
    /// </summary>
    public ImmutableArray<PackageInstance> Regular => _regular;

    /// <summary>
    /// Gets the transitive dependencies whose required versions disagree, ordered by
    /// <see cref="PackageInstance.PackageId"/> (case insensitive).
    /// </summary>
    public ImmutableArray<AmbiguousDependency> Ambiguous => _ambiguous;

    /// <summary>
    /// Gets the required instances that are absent from the NuGet cache: their own dependencies are
    /// unknown, so the closure stops there. Ordered by <see cref="PackageInstance.PackageId"/> (case
    /// insensitive) then by <see cref="PackageInstance.Version"/>.
    /// </summary>
    public ImmutableArray<PackageInstance> Missing => _missing;

    /// <summary>
    /// Gets whether every required package could be read: <see cref="Missing"/> is empty.
    /// </summary>
    public bool IsComplete => _missing.IsEmpty;

    /// <summary>
    /// Gets whether this closure holds nothing at all.
    /// </summary>
    public bool IsEmpty => _regular.IsEmpty && _ambiguous.IsEmpty && _missing.IsEmpty;

    /// <summary>
    /// Overridden to return the size of the three lists.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{_regular.Length} regular, {_ambiguous.Length} ambiguous, "
                                        + $"{_missing.Length} missing";

    /// <summary>
    /// Writes this closure as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteStartArray( "Regular" );
        foreach( var p in _regular )
        {
            w.WriteStringValue( p.ToString() );
        }
        w.WriteEndArray();
        w.WriteStartArray( "Ambiguous" );
        foreach( var a in _ambiguous )
        {
            a.Write( w );
        }
        w.WriteEndArray();
        w.WriteStartArray( "Missing" );
        foreach( var p in _missing )
        {
            w.WriteStringValue( p.ToString() );
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a closure written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped and a missing list is empty.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The closure.</returns>
    public static TransitiveDependencies Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( TransitiveDependencies ) );
        ImmutableArray<PackageInstance>.Builder? regular = null;
        ImmutableArray<AmbiguousDependency>.Builder? ambiguous = null;
        ImmutableArray<PackageInstance>.Builder? missing = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "Regular":
                    regular = JsonHelper.ReadPackageInstances( ref r, name );
                    break;
                case "Ambiguous":
                    JsonHelper.EnsureStartArray( ref r, name );
                    ambiguous = ImmutableArray.CreateBuilder<AmbiguousDependency>();
                    while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                    {
                        ambiguous.Add( AmbiguousDependency.Read( ref r ) );
                    }
                    JsonHelper.EnsureEndArray( ref r, name );
                    break;
                case "Missing":
                    missing = JsonHelper.ReadPackageInstances( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( TransitiveDependencies ) );
        // An absent list is an empty one: the format can grow and an old profile carries none of them.
        return regular == null && ambiguous == null && missing == null
                ? Empty
                : new TransitiveDependencies( regular?.DrainToImmutable() ?? [],
                                              ambiguous?.DrainToImmutable() ?? [],
                                              missing?.DrainToImmutable() ?? [] );
    }
}
