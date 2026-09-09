using CK.Core;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

public sealed partial class PublishedProfile
{
    /// <summary>
    /// Writes this profile as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteString( "StackUrl", _stackUrl.AbsoluteUri );
        w.WriteString( "World", _world.FullName );
        w.WriteString( "Version", _version.ToString() );
        w.WriteBoolean( "IsDeprecated", _isDeprecated );
        w.WriteStartArray( "Repositories" );
        foreach( var r in _repositories )
        {
            r.Write( w );
        }
        w.WriteEndArray();
        // The dependencies are always written, empty or not: the format is the same for every profile
        // and Read accepts their absence for the profiles written before they existed.
        w.WriteStartArray( "DirectDependencies" );
        foreach( var p in _directDependencies )
        {
            w.WriteStringValue( p.ToString() );
        }
        w.WriteEndArray();
        w.WritePropertyName( "TransitiveDependencies" );
        _transitiveDependencies.Write( w );
        w.WriteEndObject();
    }

    /// <summary>
    /// Gets the utf-8 Json representation of this profile. This is what is stored in a
    /// profile file.
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
    /// Gets the Json representation of this profile.
    /// </summary>
    /// <param name="indented">False to obtain a compact representation.</param>
    /// <returns>The Json string.</returns>
    public string ToJsonString( bool indented = true ) => Encoding.UTF8.GetString( ToUtf8Bytes( indented ) );

    /// <summary>
    /// Parses a utf-8 Json profile written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// Throws a <see cref="JsonException"/> on any syntax or content error and an
    /// <see cref="ArgumentException"/> when the content is syntactically valid but
    /// doesn't define a coherent profile.
    /// </para>
    /// </summary>
    /// <param name="utf8Json">The utf-8 Json bytes.</param>
    /// <returns>The profile.</returns>
    public static PublishedProfile Parse( ReadOnlySpan<byte> utf8Json )
    {
        // Utf8JsonReader doesn't handle the utf-8 BOM: a file written by another tool may have one.
        if( utf8Json.StartsWith( JsonHelper.Utf8Bom ) ) utf8Json = utf8Json.Slice( JsonHelper.Utf8Bom.Length );
        var r = new Utf8JsonReader( utf8Json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip } );
        var p = Read( ref r );
        if( r.Read() )
        {
            throw new JsonException( $"Unexpected '{r.TokenType}' token after the profile." );
        }
        return p;
    }

    /// <summary>
    /// Reads a profile written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The profile.</returns>
    public static PublishedProfile Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( PublishedProfile ) );
        Uri? stackUrl = null;
        WorldName? world = null;
        SVersion? version = null;
        bool isDeprecated = false;
        ImmutableArray<Repository>.Builder? repositories = null;
        ImmutableArray<PackageInstance>.Builder? directDependencies = null;
        TransitiveDependencies? transitiveDependencies = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "StackUrl":
                    stackUrl = JsonHelper.GetUri( ref r, name );
                    break;
                case "World":
                    world = JsonHelper.GetWorldName( ref r, name );
                    break;
                case "Version":
                    version = JsonHelper.GetVersion( ref r, name );
                    break;
                case "IsDeprecated":
                    isDeprecated = JsonHelper.GetBoolean( ref r, name );
                    break;
                case "Repositories":
                    JsonHelper.EnsureStartArray( ref r, name );
                    repositories = ImmutableArray.CreateBuilder<Repository>();
                    while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                    {
                        repositories.Add( Repository.Read( ref r ) );
                    }
                    JsonHelper.EnsureEndArray( ref r, name );
                    break;
                case "DirectDependencies":
                    directDependencies = JsonHelper.ReadPackageInstances( ref r, name );
                    break;
                case "TransitiveDependencies":
                    transitiveDependencies = TransitiveDependencies.Read( ref r );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( PublishedProfile ) );
        return new PublishedProfile( JsonHelper.Required( stackUrl, "StackUrl", nameof( PublishedProfile ) ),
                                     JsonHelper.Required( world, "World", nameof( PublishedProfile ) ),
                                     JsonHelper.Required( version, "Version", nameof( PublishedProfile ) ),
                                     JsonHelper.Required( repositories, "Repositories", nameof( PublishedProfile ) )
                                               .DrainToImmutable(),
                                     // Unlike the Repositories, the dependencies are optional: absent
                                     // means empty, there is no file format version to distinguish them.
                                     directDependencies?.DrainToImmutable() ?? [],
                                     transitiveDependencies,
                                     isDeprecated );
    }
}
