using CK.Core;
using System;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// The published profile of a World Reference (an upstream World) that a <see cref="PublishedProfile"/> was
/// aligned on: the packages the World consumes from that upstream are the ones this upstream profile produces.
/// <para>
/// It identifies a profile, it doesn't carry it: <see cref="StackUrl"/> and <see cref="World"/> locate the
/// upstream's published folder and <see cref="PublishedProfile.GetProfilePath"/> gives the file of the
/// <see cref="Version"/> there.
/// </para>
/// </summary>
public sealed class UpstreamProfile
{
    readonly Uri _stackUrl;
    readonly WorldName _world;
    readonly TimeVersion _version;

    /// <summary>
    /// Initializes a new upstream profile.
    /// </summary>
    /// <param name="stackUrl">The upstream stack repository url. Must be absolute.</param>
    /// <param name="world">The upstream world name.</param>
    /// <param name="version">The upstream profile version.</param>
    public UpstreamProfile( Uri stackUrl, WorldName world, TimeVersion version )
    {
        ArgumentNullException.ThrowIfNull( stackUrl );
        ArgumentNullException.ThrowIfNull( world );
        ArgumentNullException.ThrowIfNull( version );
        if( !stackUrl.IsAbsoluteUri )
        {
            throw new ArgumentException( $"Stack url must be absolute: '{stackUrl}'.", nameof( stackUrl ) );
        }
        _stackUrl = stackUrl;
        _world = world;
        _version = version;
    }

    /// <summary>
    /// Initializes a new upstream profile that identifies a profile.
    /// </summary>
    /// <param name="profile">The upstream profile.</param>
    public UpstreamProfile( PublishedProfile profile )
        : this( (profile ?? throw new ArgumentNullException( nameof( profile ) )).StackUrl, profile.World, profile.Version )
    {
    }

    /// <summary>
    /// Gets the url of the upstream stack repository.
    /// </summary>
    public Uri StackUrl => _stackUrl;

    /// <summary>
    /// Gets the upstream world name.
    /// </summary>
    public WorldName World => _world;

    /// <summary>
    /// Gets the version of the upstream profile.
    /// </summary>
    public TimeVersion Version => _version;

    /// <summary>
    /// Gets whether this identifies the same World as another one: same <see cref="StackUrl"/> (case insensitive)
    /// and same <see cref="World"/>, whatever their <see cref="Version"/>.
    /// </summary>
    /// <param name="other">The other upstream profile.</param>
    /// <returns>True if both are profiles of the same World.</returns>
    public bool IsSameWorld( UpstreamProfile other )
    {
        ArgumentNullException.ThrowIfNull( other );
        return IsSameWorld( other._stackUrl, other._world );
    }

    internal bool IsSameWorld( Uri stackUrl, WorldName world )
    {
        return string.Equals( _stackUrl.AbsoluteUri, stackUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase )
               && _world.Equals( world );
    }

    /// <summary>
    /// Overridden to return the <see cref="World"/> and <see cref="Version"/>, like <see cref="PublishedProfile.ToString"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{_world.FullName}/v{_version}";

    /// <summary>
    /// Writes this upstream profile as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteString( "StackUrl", _stackUrl.AbsoluteUri );
        w.WriteString( "World", _world.FullName );
        w.WriteString( "Version", _version.ToString() );
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads an upstream profile written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The upstream profile.</returns>
    public static UpstreamProfile Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( UpstreamProfile ) );
        Uri? stackUrl = null;
        WorldName? world = null;
        TimeVersion? version = null;
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
                    version = JsonHelper.GetTimeVersion( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( UpstreamProfile ) );
        return new UpstreamProfile( JsonHelper.Required( stackUrl, "StackUrl", nameof( UpstreamProfile ) ),
                                    JsonHelper.Required( world, "World", nameof( UpstreamProfile ) ),
                                    JsonHelper.Required( version, "Version", nameof( UpstreamProfile ) ) );
    }
}
