using CK.Core;
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

public sealed partial class PublishedProfile
{
    // The Json writer options used by ToUtf8Bytes: the new line is explicitly "\r\n" (JsonWriterOptions
    // defaults to Environment.NewLine) so that the produced files are the same on any platform.
    static ReadOnlySpan<byte> _utf8Bom => [0xEF, 0xBB, 0xBF];

    // The UnsafeRelaxedJsonEscaping encoder is used because these Json are files: they are never
    // embedded in a html page nor in a script. This keeps the '+' of a version's build metadata
    // and the '@' of a package instance readable.
    static readonly JsonWriterOptions _indentedOptions = new JsonWriterOptions
    {
        Indented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static readonly JsonWriterOptions _compactOptions = new JsonWriterOptions
    {
        Indented = false,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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
        using( var w = new Utf8JsonWriter( buffer, indented ? _indentedOptions : _compactOptions ) )
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
        if( utf8Json.StartsWith( _utf8Bom ) ) utf8Json = utf8Json.Slice( _utf8Bom.Length );
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
                                     isDeprecated );
    }
}
