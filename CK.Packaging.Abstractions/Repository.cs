using CK.Core;
using System;
using System.Collections.Immutable;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// A repository contains the published <see cref="Packages"/>.
/// </summary>
public sealed class Repository
{
    readonly RepositoryKey _key;
    readonly ImmutableArray<PackageInstance> _packages;

    /// <summary>
    /// Initializes a new repository with its packages.
    /// <para>
    /// The <paramref name="packages"/> are sorted by <see cref="PackageInstance.PackageId"/> (case
    /// insensitive): a repository is always in a canonical form, whatever the order in which its
    /// packages have been discovered.
    /// </para>
    /// </summary>
    /// <param name="key">The repository identifier and locator. Must be <see cref="RepositoryKey.IsValid"/>.</param>
    /// <param name="packages">
    /// The packages. Must not be default and must not contain the same
    /// <see cref="PackageInstance.PackageId"/> more than once.
    /// </param>
    public Repository( RepositoryKey key, ImmutableArray<PackageInstance> packages )
    {
        if( !key.IsValid )
        {
            throw new ArgumentException( $"Invalid repository key '{key}'.", nameof( key ) );
        }
        if( packages.IsDefault )
        {
            throw new ArgumentException( "Packages must be initialized.", nameof( packages ) );
        }
        // PackageInstance's comparison is by PackageId (case insensitive) then by Version:
        // a duplicate PackageId can only be adjacent in the sorted array.
        packages = packages.Sort();
        for( int i = 0; i < packages.Length; ++i )
        {
            var p = packages[i];
            if( p is null )
            {
                throw new ArgumentException( $"Null package at index {i}.", nameof( packages ) );
            }
            if( i > 0 && StringComparer.OrdinalIgnoreCase.Equals( packages[i - 1].PackageId, p.PackageId ) )
            {
                throw new ArgumentException( $"Repository '{key.Url}' contains '{p.PackageId}' more than once: "
                                             + $"'{packages[i - 1].Version}' and '{p.Version}'.",
                                             nameof( packages ) );
            }
        }
        _key = key;
        _packages = packages;
    }

    /// <summary>
    /// Gets the identifier and locator.
    /// </summary>
    public RepositoryKey Key => _key;

    /// <summary>
    /// Gets the packages, ordered by <see cref="PackageInstance.PackageId"/> (case insensitive).
    /// </summary>
    public ImmutableArray<PackageInstance> Packages => _packages;

    /// <summary>
    /// Overridden to return the <see cref="Key"/> and the number of <see cref="Packages"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{_key} - {_packages.Length} package(s)";

    /// <summary>
    /// Writes this repository as a Json object.
    /// </summary>
    /// <param name="w">The writer.</param>
    public void Write( Utf8JsonWriter w )
    {
        ArgumentNullException.ThrowIfNull( w );
        w.WriteStartObject();
        w.WriteString( "Url", _key.Url.AbsoluteUri );
        w.WriteString( "Id", _key.Id.ToString() );
        w.WriteStartArray( "Packages" );
        foreach( var p in _packages )
        {
            w.WriteStringValue( p.ToString() );
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    /// <summary>
    /// Reads a repository written by <see cref="Write(Utf8JsonWriter)"/>.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartObject"/> token (or not
    /// started yet) and is left on the <see cref="JsonTokenType.EndObject"/> token. Unknown properties
    /// are skipped.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <returns>The repository.</returns>
    public static Repository Read( ref Utf8JsonReader r )
    {
        JsonHelper.EnsureStartObject( ref r, nameof( Repository ) );
        Uri? url = null;
        RandomId id = default;
        ImmutableArray<PackageInstance>.Builder? packages = null;
        while( r.Read() && r.TokenType == JsonTokenType.PropertyName )
        {
            var name = JsonHelper.StartProperty( ref r );
            switch( name )
            {
                case "Url":
                    url = JsonHelper.GetUri( ref r, name );
                    break;
                case "Id":
                    id = JsonHelper.GetRandomId( ref r, name );
                    break;
                case "Packages":
                    JsonHelper.EnsureStartArray( ref r, name );
                    packages = ImmutableArray.CreateBuilder<PackageInstance>();
                    while( r.Read() && r.TokenType != JsonTokenType.EndArray )
                    {
                        packages.Add( JsonHelper.GetPackageInstance( ref r, name ) );
                    }
                    JsonHelper.EnsureEndArray( ref r, name );
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
        JsonHelper.EnsureEndObject( ref r, nameof( Repository ) );
        url = JsonHelper.Required( url, "Url", nameof( Repository ) );
        packages = JsonHelper.Required( packages, "Packages", nameof( Repository ) );
        if( !id.IsValid )
        {
            throw new JsonException( $"Missing 'Id' property in '{nameof( Repository )}'." );
        }
        return new Repository( new RepositoryKey( url, id ), packages.DrainToImmutable() );
    }
}
