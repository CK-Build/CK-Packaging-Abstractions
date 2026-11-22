using CK.Core;
using System;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// A NuGet feed of the World when a <see cref="PublishedProfile"/> was written: where its packages can be
/// obtained. It never carries a secret.
/// <para>
/// A feed is in one of 3 shapes:
/// <list type="bullet">
///     <item>
///     A true public feed (nuget.org): no <see cref="SecretKey"/>, no <see cref="PublicReadUserName"/>.
///     </item>
///     <item>
///     A public feed that requires an authentication (the GitHub feeds): its <see cref="PublicReadUserName"/>
///     and <see cref="PublicReadPassword"/> are not secrets, they are used as is (and committed in the
///     repositories' <c>nuget.config</c>).
///     </item>
///     <item>
///     A feed that requires a secret, to be read (a private feed) or to be pushed to: <see cref="SecretKey"/>
///     is the <b>name</b> of this secret in the publisher's secrets store, never its value. A reader finds the
///     credentials in its own NuGet configuration or environment.
///     </item>
/// </list>
/// </para>
/// </summary>
public sealed class PublishedFeed
{
    readonly string _name;
    readonly string _url;
    readonly CSVersionKindFilter? _pushQualityFilter;
    readonly string? _secretKey;
    readonly string? _publicReadUserName;
    readonly string? _publicReadPassword;

    /// <summary>
    /// Initializes a new feed.
    /// </summary>
    /// <param name="name">The feed name. Must not be null or white space.</param>
    /// <param name="url">The feed url, as it appears in a <c>nuget.config</c> file. Must not be null or white space.</param>
    /// <param name="pushQualityFilter">See <see cref="PushQualityFilter"/>.</param>
    /// <param name="secretKey">See <see cref="SecretKey"/>. Cannot be set with the public read credentials.</param>
    /// <param name="publicReadUserName">See <see cref="PublicReadUserName"/>. Set with <paramref name="publicReadPassword"/> or not at all.</param>
    /// <param name="publicReadPassword">See <see cref="PublicReadPassword"/>. Set with <paramref name="publicReadUserName"/> or not at all.</param>
    public PublishedFeed( string name,
                          string url,
                          CSVersionKindFilter? pushQualityFilter = null,
                          string? secretKey = null,
                          string? publicReadUserName = null,
                          string? publicReadPassword = null )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace( name );
        ArgumentException.ThrowIfNullOrWhiteSpace( url );
        if( secretKey != null && string.IsNullOrWhiteSpace( secretKey ) )
        {
            throw new ArgumentException( $"Feed '{name}': the secret key cannot be white space.", nameof( secretKey ) );
        }
        if( (publicReadUserName == null) != (publicReadPassword == null) )
        {
            throw new ArgumentException( $"Feed '{name}': the public read user name and password go together.",
                                         publicReadUserName == null ? nameof( publicReadUserName ) : nameof( publicReadPassword ) );
        }
        if( secretKey != null && publicReadUserName != null )
        {
            throw new ArgumentException( $"Feed '{name}': a secret key and public read credentials cannot be "
                                         + "specified at the same time.",
                                         nameof( secretKey ) );
        }
        _name = name;
        _url = url;
        _pushQualityFilter = pushQualityFilter;
        _secretKey = secretKey;
        _publicReadUserName = publicReadUserName;
        _publicReadPassword = publicReadPassword;
    }

    /// <summary>
    /// Gets the feed name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets the feed url, as it appears in a <c>nuget.config</c> file (it can be a local folder).
    /// </summary>
    public string Url => _url;

    /// <summary>
    /// Gets the filter of the versions that are pushed to this feed when it has a <see cref="SecretKey"/>
    /// (see <see cref="IsPushTargetOf(SVersion)"/>). Null for a feed that is only read.
    /// </summary>
    public CSVersionKindFilter? PushQualityFilter => _pushQualityFilter;

    /// <summary>
    /// Gets the name of the secret that this feed requires (to read a private feed, to push to any feed).
    /// This is a name in the publisher's secrets store, never a secret.
    /// </summary>
    public string? SecretKey => _secretKey;

    /// <summary>
    /// Gets the user name of the public read credentials (not a secret). Null when the feed has none.
    /// </summary>
    public string? PublicReadUserName => _publicReadUserName;

    /// <summary>
    /// Gets the password of the public read credentials (not a secret). Null when the feed has none.
    /// </summary>
    public string? PublicReadPassword => _publicReadPassword;

    /// <summary>
    /// Gets whether the publisher pushes a package version to this feed: it has a <see cref="SecretKey"/>
    /// and its <see cref="PushQualityFilter"/> accepts the version (its kind and its CI flag).
    /// <para>
    /// This tells where a package produced by the profile is expected to be. A feed that is not a push target
    /// can still hold it (a mirror, an upstream source).
    /// </para>
    /// </summary>
    /// <param name="version">The package version.</param>
    /// <returns>True if this feed receives this version.</returns>
    public bool IsPushTargetOf( SVersion version )
    {
        ArgumentNullException.ThrowIfNull( version );
        return _secretKey != null && _pushQualityFilter.HasValue && _pushQualityFilter.Value.Accepts( version );
    }

    /// <summary>
    /// Overridden to return the "Name (Url)".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{_name} ({_url})";

    /// <summary>
    /// Writes this feed as a Json object. The optional members are written only when they are set, and the
    /// <see cref="PushQualityFilter"/> in its normalized form ("[papa,]" is written "[papa,stable]").
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteString( "Name", _name );
        w.WriteString( "Url", _url );
        if( _pushQualityFilter.HasValue ) w.WriteString( "PushQualityFilter", _pushQualityFilter.Value.ToString() );
        if( _secretKey != null ) w.WriteString( "SecretKey", _secretKey );
        if( _publicReadUserName != null )
        {
            w.WriteStartObject( "PublicReadCredentials" );
            w.WriteString( "UserName", _publicReadUserName );
            w.WriteString( "Password", _publicReadPassword );
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a feed written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The feed.</returns>
    public static PublishedFeed Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( PublishedFeed ) );
        string? name = null;
        string? url = null;
        CSVersionKindFilter? pushQualityFilter = null;
        string? secretKey = null;
        string? userName = null;
        string? password = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var propertyName = JsonHelper.StartProperty( ref r );
            switch( propertyName )
            {
                case "Name":
                    name = JsonHelper.GetString( ref r, propertyName );
                    break;
                case "Url":
                    url = JsonHelper.GetString( ref r, propertyName );
                    break;
                case "PushQualityFilter":
                    // TryParse allows a trailing suffix: TryMatch is used here to require the whole string
                    // to be the filter.
                    var s = JsonHelper.GetString( ref r, propertyName );
                    var head = s.AsSpan();
                    pushQualityFilter = CSVersionKindFilter.TryMatch( ref head, out var f ) && head.TrimEnd().Length == 0
                                            ? f
                                            : throw new JsonException( $"Expected a \"[Min,Max],AllowCI\" filter for '{propertyName}', got '{s}'." );
                    break;
                case "SecretKey":
                    secretKey = JsonHelper.GetString( ref r, propertyName );
                    break;
                case "PublicReadCredentials":
                    JsonHelper.EnsureStartObject( ref r, "PublicReadCredentials" );
                    while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
                    {
                        var credName = JsonHelper.StartProperty( ref r );
                        switch( credName )
                        {
                            case "UserName":
                                userName = JsonHelper.GetString( ref r, credName );
                                break;
                            case "Password":
                                password = JsonHelper.GetString( ref r, credName );
                                break;
                            default:
                                r.Skip();
                                break;
                        }
                    }
                    JsonHelper.EnsureEndObject( ref r, "PublicReadCredentials" );
                    userName = JsonHelper.Required( userName, "UserName", "PublicReadCredentials" );
                    password = JsonHelper.Required( password, "Password", "PublicReadCredentials" );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( PublishedFeed ) );
        return new PublishedFeed( JsonHelper.Required( name, "Name", nameof( PublishedFeed ) ),
                                  JsonHelper.Required( url, "Url", nameof( PublishedFeed ) ),
                                  pushQualityFilter,
                                  secretKey,
                                  userName,
                                  password );
    }
}
