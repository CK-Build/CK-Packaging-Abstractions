using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// What a restore of a <see cref="PublishedProfile"/>'s packages brings beyond the packages the profile
/// references explicitly: the union of what NuGet resolved transitively for each of its repositories.
/// <para>
/// <see cref="Regular"/> and <see cref="Ambiguous"/> partition it by package identifier - the two never
/// share one - so a consumer that ignores the details can read their union as a flat coherent set of
/// resolved package instances.
/// </para>
/// <para>
/// This is not a computed closure: nothing is walked, nothing can be unreachable and no package can be
/// missing. Each repository's set is NuGet's own resolution - target framework aware and pruned - and an
/// <see cref="Ambiguous"/> entry exists only because two of them, or one of them and the profile itself,
/// disagree.
/// </para>
/// </summary>
public sealed class TransitiveDependencies
{
    readonly ImmutableArray<PackageInstance> _regular;
    readonly ImmutableArray<AmbiguousDependency> _ambiguous;

    /// <summary>
    /// The empty closure. This is what a profile that carries no dependency information exposes.
    /// </summary>
    public static readonly TransitiveDependencies Empty = new TransitiveDependencies( [], [] );

    /// <summary>
    /// Initializes a new set of transitive dependencies.
    /// <para>
    /// The two lists are sorted by <see cref="PackageInstance.PackageId"/> (case insensitive) then by
    /// <see cref="PackageInstance.Version"/>: they are always in a canonical form, whatever the order
    /// in which the packages have been discovered.
    /// </para>
    /// </summary>
    /// <param name="regular">
    /// The transitive dependencies that resolve to one version. Must not be default and must not contain
    /// the same <see cref="PackageInstance.PackageId"/> more than once.
    /// </param>
    /// <param name="ambiguous">
    /// The transitive dependencies whose resolved versions disagree. Must not be default, must not contain
    /// the same <see cref="PackageInstance.PackageId"/> more than once and must share no identifier with
    /// <paramref name="regular"/>.
    /// </param>
    public TransitiveDependencies( ImmutableArray<PackageInstance> regular,
                                   ImmutableArray<AmbiguousDependency> ambiguous )
    {
        CheckNoNull( regular, nameof( regular ) );
        CheckNoNull( ambiguous, nameof( ambiguous ) );
        // PackageInstance's comparison is by PackageId (case insensitive) then by Version: a duplicate
        // identifier can only be adjacent in the sorted arrays.
        regular = regular.Sort();
        ambiguous = ambiguous.Sort( static ( a1, a2 ) => a1.CompareTo( a2 ) );
        CheckNoDuplicateId( regular, "Regular", nameof( regular ) );
        CheckNoDuplicateId( ambiguous, "Ambiguous", nameof( ambiguous ) );
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
    /// Gets the transitive dependencies whose resolved versions disagree, ordered by
    /// <see cref="PackageInstance.PackageId"/> (case insensitive).
    /// </summary>
    public ImmutableArray<AmbiguousDependency> Ambiguous => _ambiguous;

    /// <summary>
    /// Gets whether this holds nothing at all.
    /// </summary>
    public bool IsEmpty => _regular.IsEmpty && _ambiguous.IsEmpty;

    /// <summary>
    /// Overridden to return the size of the two lists.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{_regular.Length} regular, {_ambiguous.Length} ambiguous";

    /// <summary>
    /// Writes these transitive dependencies as a Json object.
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
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads what <see cref="Write(Utf8JsonWriter)"/> wrote.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped and a missing list is empty.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The transitive dependencies.</returns>
    public static TransitiveDependencies Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( TransitiveDependencies ) );
        ImmutableArray<PackageInstance>.Builder? regular = null;
        ImmutableArray<AmbiguousDependency>.Builder? ambiguous = null;
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
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( TransitiveDependencies ) );
        // An absent list is an empty one: the format can grow and an old profile carries none of them.
        return regular == null && ambiguous == null
                ? Empty
                : new TransitiveDependencies( regular?.DrainToImmutable() ?? [],
                                              ambiguous?.DrainToImmutable() ?? [] );
    }
}
