using CK.Core;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// The index of a set of <see cref="PublishedProfile"/>: their versions, split into the ones that are
/// alive and the ones that are <see cref="PublishedProfile.IsDeprecated"/>, each grouped by branch and
/// each group ordered from the latest to the oldest version.
/// <para>
/// This is a reflection of a set of profiles, never a source: it carries versions only, and a version
/// is enough to reach its profile (<see cref="PublishedProfile.GetProfilePath"/>). It exists so that a
/// consumer can see what a World published - and pick the publication it wants - without reading every
/// profile.
/// </para>
/// <para>
/// The grouping is not stored state: a group name is a pure function of the version it holds
/// (<see cref="GetGroupName(SVersion)"/>), so an index cannot disagree with itself about where a
/// version belongs.
/// </para>
/// </summary>
public sealed class PublishedIndex
{
    readonly SortedDictionary<string, ImmutableArray<SVersion>> _alive;
    readonly SortedDictionary<string, ImmutableArray<SVersion>> _deprecated;

    /// <summary>
    /// The conventional file name of an index: "index.json".
    /// <para>
    /// This is not a profile file name (a profile file is named after its version), so a folder that
    /// holds both can tell them apart and an index can never hide a profile.
    /// </para>
    /// </summary>
    public const string IndexFileName = "index.json";

    /// <summary>
    /// The group name of the stable versions, whose <see cref="SVersion.BranchName"/> is the empty
    /// string: "(stable)".
    /// <para>
    /// This is deliberately NOT a World's root branch name - that one is the business of whoever models
    /// branches and can be renamed (a Long Term Support World has its own). An index names the versions
    /// it contains, not the branches of any particular World.
    /// </para>
    /// <para>
    /// The parentheses cannot collide with a branch name and '(' sorts before every letter a branch name
    /// can start with: the stable group always comes first.
    /// </para>
    /// </summary>
    public const string StableGroupName = "(stable)";

    /// <summary>
    /// The group name of the CI builds of the stable versions: "(stable-ci)". They share the
    /// <see cref="StableGroupName"/> empty <see cref="SVersion.BranchName"/> but never its group.
    /// </summary>
    public const string StableCIGroupName = "(stable-ci)";

    /// <summary>
    /// What qualifies a branch group name to hold its CI builds instead of its regular versions: "-ci".
    /// <para>
    /// This cannot collide with a branch: "alpha" to "zulu" are fixed and by design none of them ends
    /// with it, and an exploratory name that would (or that starts with "ci-") is refused by
    /// <see cref="SVersion.IsReservedExploratoryName(ReadOnlySpan{char})"/>.
    /// </para>
    /// <para>
    /// Ordinal order puts a group immediately before its own CI one: ')' and the end of a string both
    /// precede '-'.
    /// </para>
    /// </summary>
    public const string CIGroupSuffix = "-ci";

    /// <summary>
    /// The index of no profile at all. Its two sets hold their <see cref="StableGroupName"/> group and
    /// nothing else.
    /// </summary>
    public static readonly PublishedIndex Empty = new PublishedIndex( [], [] );

    /// <summary>
    /// Initializes a new index from the profile versions it must contain.
    /// <para>
    /// The versions are grouped by <see cref="GetGroupName(SVersion)"/> and each group is ordered from
    /// the latest to the oldest, so an index built from the same versions in a different discovery order
    /// is byte-identical once serialized.
    /// </para>
    /// </summary>
    /// <param name="alive">
    /// The versions of the profiles that are not deprecated. Each must be a Conformant
    /// <see cref="SVersion"/> and must appear once across both sets.
    /// </param>
    /// <param name="deprecated">
    /// The versions of the <see cref="PublishedProfile.IsDeprecated"/> profiles. Each must be a
    /// Conformant <see cref="SVersion"/> and must appear once across both sets.
    /// </param>
    public PublishedIndex( IEnumerable<SVersion> alive, IEnumerable<SVersion> deprecated )
    {
        ArgumentNullException.ThrowIfNull( alive );
        ArgumentNullException.ThrowIfNull( deprecated );
        // A profile version identifies a profile - one file - so it belongs to exactly one of the two
        // sets: this also catches the same version listed twice in one of them.
        var all = new Dictionary<SVersion, bool>();
        _alive = Group( alive, nameof( alive ), isAlive: true, all );
        _deprecated = Group( deprecated, nameof( deprecated ), isAlive: false, all );

        static SortedDictionary<string, ImmutableArray<SVersion>> Group( IEnumerable<SVersion> versions,
                                                                        string name,
                                                                        bool isAlive,
                                                                        Dictionary<SVersion, bool> all )
        {
            var builders = new SortedDictionary<string, List<SVersion>>( StringComparer.Ordinal )
            {
                // Seeded, so a consumer always has the root group to read. Every other group exists
                // only because a version landed in it.
                { StableGroupName, new List<SVersion>() }
            };
            foreach( var v in versions )
            {
                if( v is null )
                {
                    throw new ArgumentException( "Null version.", name );
                }
                if( !all.TryAdd( v, isAlive ) )
                {
                    throw new ArgumentException( $"Version '{v}' appears more than once: a profile version is "
                                                 + (all[v] == isAlive
                                                        ? "unique."
                                                        : "either alive or deprecated, not both."),
                                                 name );
                }
                // GetGroupName rejects a non Conformant version: this is where that is enforced.
                var groupName = GetGroupName( v, name );
                if( !builders.TryGetValue( groupName, out var group ) )
                {
                    builders.Add( groupName, group = new List<SVersion>() );
                }
                group.Add( v );
            }
            var result = new SortedDictionary<string, ImmutableArray<SVersion>>( StringComparer.Ordinal );
            foreach( var (groupName, group) in builders )
            {
                group.Sort( static ( v1, v2 ) => v2.CompareTo( v1 ) );
                result.Add( groupName, group.ToImmutableArray() );
            }
            return result;
        }
    }

    /// <summary>
    /// Creates an index of the specified profiles, splitting them on their
    /// <see cref="PublishedProfile.IsDeprecated"/>.
    /// </summary>
    /// <param name="profiles">The profiles to index.</param>
    /// <returns>The index.</returns>
    public static PublishedIndex Create( IEnumerable<PublishedProfile> profiles )
    {
        ArgumentNullException.ThrowIfNull( profiles );
        var alive = new List<SVersion>();
        var deprecated = new List<SVersion>();
        foreach( var p in profiles )
        {
            if( p is null )
            {
                throw new ArgumentException( "Null profile.", nameof( profiles ) );
            }
            (p.IsDeprecated ? deprecated : alive).Add( p.Version );
        }
        return new PublishedIndex( alive, deprecated );
    }

    /// <summary>
    /// Gets the versions of the profiles that are not deprecated, by group name in ordinal order, each
    /// group ordered from the latest to the oldest version.
    /// <para>
    /// The <see cref="StableGroupName"/> group is always present (possibly empty).
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, ImmutableArray<SVersion>> Alive => _alive;

    /// <summary>
    /// Gets the versions of the <see cref="PublishedProfile.IsDeprecated"/> profiles, by group name in
    /// ordinal order, each group ordered from the latest to the oldest version.
    /// <para>
    /// The <see cref="StableGroupName"/> group is always present (possibly empty).
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, ImmutableArray<SVersion>> Deprecated => _deprecated;

    /// <summary>
    /// Gets whether this index holds no version at all.
    /// </summary>
    public bool IsEmpty => IsEmptySet( _alive ) && IsEmptySet( _deprecated );

    static bool IsEmptySet( SortedDictionary<string, ImmutableArray<SVersion>> set )
    {
        foreach( var group in set.Values )
        {
            if( group.Length > 0 ) return false;
        }
        return true;
    }

    /// <summary>
    /// Gets the <see cref="Alive"/> versions of a group, the latest first. An absent group is an empty
    /// one: a group exists only because a version landed in it.
    /// </summary>
    /// <param name="groupName">The group name (see <see cref="GetGroupName(SVersion)"/>).</param>
    /// <returns>The versions, the latest first. Empty when the group holds none.</returns>
    public ImmutableArray<SVersion> GetAlive( string groupName )
    {
        ArgumentNullException.ThrowIfNull( groupName );
        return _alive.TryGetValue( groupName, out var versions ) ? versions : [];
    }

    /// <summary>
    /// Gets the <see cref="Deprecated"/> versions of a group, the latest first. An absent group is an
    /// empty one: a group exists only because a version landed in it.
    /// </summary>
    /// <param name="groupName">The group name (see <see cref="GetGroupName(SVersion)"/>).</param>
    /// <returns>The versions, the latest first. Empty when the group holds none.</returns>
    public ImmutableArray<SVersion> GetDeprecated( string groupName )
    {
        ArgumentNullException.ThrowIfNull( groupName );
        return _deprecated.TryGetValue( groupName, out var versions ) ? versions : [];
    }

    /// <summary>
    /// Gets the group name a version belongs to: its <see cref="SVersion.BranchName"/>, the stable
    /// group names standing for the empty one, and the CI builds of a branch in their own
    /// <see cref="CIGroupSuffix"/> group.
    /// </summary>
    /// <param name="version">The version. Must be a Conformant <see cref="SVersion"/>.</param>
    /// <returns>The group name.</returns>
    public static string GetGroupName( SVersion version )
    {
        ArgumentNullException.ThrowIfNull( version );
        return GetGroupName( version, nameof( version ) );
    }

    static string GetGroupName( SVersion version, string name )
    {
        var branchName = version.BranchName;
        if( branchName == null )
        {
            throw new ArgumentException( $"Version '{version}' must be a Conformant SVersion.", name );
        }
        return GetGroupName( branchName, version.IsCI );
    }

    /// <summary>
    /// Gets the group name of a branch: the branch name itself, <see cref="StableGroupName"/> or
    /// <see cref="StableCIGroupName"/> when it is the empty one, and the <see cref="CIGroupSuffix"/>
    /// group when the CI builds are wanted.
    /// <para>
    /// This is the overload to use to look a branch up: the branch name is the version side of a branch
    /// (the empty string for the stable line, "alpha" to "zulu", "explo/{name}"), not the name a
    /// repository gives it.
    /// </para>
    /// </summary>
    /// <param name="branchName">
    /// The <see cref="SVersion.BranchName"/> of the branch: the empty string for the stable line.
    /// </param>
    /// <param name="isCI">True to get the group of the branch's CI builds instead of its regular ones.</param>
    /// <returns>The group name.</returns>
    public static string GetGroupName( string branchName, bool isCI )
    {
        ArgumentNullException.ThrowIfNull( branchName );
        return branchName.Length == 0
                ? (isCI ? StableCIGroupName : StableGroupName)
                : (isCI ? branchName + CIGroupSuffix : branchName);
    }

    /// <summary>
    /// Overridden to return the number of alive and deprecated versions.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{Count( _alive )} alive, {Count( _deprecated )} deprecated";

    static int Count( SortedDictionary<string, ImmutableArray<SVersion>> set )
    {
        int c = 0;
        foreach( var group in set.Values )
        {
            c += group.Length;
        }
        return c;
    }

    /// <summary>
    /// Writes this index as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        WriteSet( w, "Alive", _alive );
        WriteSet( w, "Deprecated", _deprecated );
        w.WriteEndObject();

        static void WriteSet( Utf8JsonWriter w, string setName, SortedDictionary<string, ImmutableArray<SVersion>> set )
        {
            w.WriteStartObject( setName );
            foreach( var (groupName, versions) in set )
            {
                w.WriteStartArray( groupName );
                foreach( var v in versions )
                {
                    w.WriteStringValue( v.ToString() );
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
    }

    /// <summary>
    /// Gets the utf-8 Json representation of this index. This is what is stored in an
    /// <see cref="IndexFileName"/> file.
    /// </summary>
    /// <param name="indented">False to obtain a compact representation.</param>
    /// <returns>The utf-8 Json bytes.</returns>
    public byte[] ToUtf8Bytes( bool indented = true )
    {
        var buffer = new ArrayBufferWriter<byte>();
        using( var w = new Utf8JsonWriter( buffer, indented ? JsonHelper.IndentedOptions : JsonHelper.CompactOptions ) )
        {
            Write( w );
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Gets the Json representation of this index.
    /// </summary>
    /// <param name="indented">False to obtain a compact representation.</param>
    /// <returns>The Json string.</returns>
    public string ToJsonString( bool indented = true ) => Encoding.UTF8.GetString( ToUtf8Bytes( indented ) );

    /// <summary>
    /// Parses a utf-8 Json index written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// Throws a <see cref="JsonException"/> on any syntax or content error and an
    /// <see cref="ArgumentException"/> when the content is syntactically valid but doesn't define a
    /// coherent index.
    /// </para>
    /// </summary>
    /// <param name="utf8Json">The utf-8 Json bytes.</param>
    /// <returns>The index.</returns>
    public static PublishedIndex Parse( ReadOnlySpan<byte> utf8Json )
    {
        // Utf8JsonReader doesn't handle the utf-8 BOM: a file written by another tool may have one.
        if( utf8Json.StartsWith( JsonHelper.Utf8Bom ) ) utf8Json = utf8Json.Slice( JsonHelper.Utf8Bom.Length );
        var r = new Utf8JsonReader( utf8Json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip } );
        var i = Read( ref r );
        if( r.Read() )
        {
            throw new JsonException( $"Unexpected '{r.TokenType}' token after the index." );
        }
        return i;
    }

    /// <summary>
    /// Reads an index written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped and a missing set is empty.
    /// </para>
    /// <para>
    /// A group name is a pure function of the versions it holds, so it is checked rather than trusted:
    /// a version listed under a group it doesn't belong to throws a <see cref="JsonException"/>.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The index.</returns>
    public static PublishedIndex Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( PublishedIndex ) );
        List<SVersion>? alive = null;
        List<SVersion>? deprecated = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "Alive":
                    alive = ReadSet( ref r, name );
                    break;
                case "Deprecated":
                    deprecated = ReadSet( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( PublishedIndex ) );
        // An absent set is an empty one: the format can grow and the constructor seeds the stable group.
        return alive == null && deprecated == null
                ? Empty
                : new PublishedIndex( alive ?? [], deprecated ?? [] );

        static List<SVersion> ReadSet( ref Utf8JsonReader r, string setName )
        {
            JsonHelper.EnsureStartObject( ref r, setName );
            var versions = new List<SVersion>();
            while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
            {
                var groupName = JsonHelper.StartProperty( ref r );
                JsonHelper.EnsureStartArray( ref r, groupName );
                while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                {
                    var v = JsonHelper.GetVersion( ref r, groupName );
                    var expected = GetGroupName( v, groupName );
                    if( expected != groupName )
                    {
                        throw new JsonException( $"Version '{v}' of the '{setName}' set is listed in the "
                                                 + $"'{groupName}' group but belongs to '{expected}'." );
                    }
                    versions.Add( v );
                }
                JsonHelper.EnsureEndArray( ref r, groupName );
            }
            JsonHelper.EnsureEndObject( ref r, setName );
            return versions;
        }
    }
}
