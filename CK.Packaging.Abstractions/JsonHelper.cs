using CK.Core;
using System;
using System.Collections.Immutable;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CK.Packaging.Abstractions;

/// <summary>
/// Helpers above the basic <see cref="Utf8JsonReader"/> that centralize the error messages of
/// this model's deserialization. Every method throws a <see cref="JsonException"/> on error.
/// <para>
/// The <c>Get</c> methods expect the reader to be on the property's value token: they never
/// move the reader.
/// </para>
/// </summary>
static class JsonHelper
{
    /// <summary>
    /// The utf-8 BOM. <see cref="Utf8JsonReader"/> doesn't handle it, but a file written by another
    /// tool may have one: every Parse of this model skips it.
    /// </summary>
    internal static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // These are files: the new line is explicitly "\r\n" (JsonWriterOptions defaults to
    // Environment.NewLine) so that the same content produces the same bytes on any platform, and the
    // encoder is the relaxed one because a file is never embedded in a html page nor in a script -
    // this keeps the '+' of a version's build metadata and the '@' of a package instance readable.

    /// <summary>
    /// The writer options of the stored, human readable form.
    /// </summary>
    internal static readonly JsonWriterOptions IndentedOptions = new JsonWriterOptions
    {
        Indented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// The writer options of the compact form.
    /// </summary>
    internal static readonly JsonWriterOptions CompactOptions = new JsonWriterOptions
    {
        Indented = false,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Ensures that the reader is on a <see cref="JsonTokenType.StartObject"/> token, reading the
    /// first token when the reader has not started yet.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="typeName">The read type name (used by the error message).</param>
    internal static void EnsureStartObject( ref Utf8JsonReader r, string typeName )
    {
        if( r.TokenType == JsonTokenType.None ) r.Read();
        if( r.TokenType != JsonTokenType.StartObject )
        {
            throw new JsonException( $"Expected '{typeName}' object start, got '{r.TokenType}'." );
        }
    }

    /// <summary>
    /// Ensures that the reader is on a <see cref="JsonTokenType.EndObject"/> token.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="typeName">The read type name (used by the error message).</param>
    internal static void EnsureEndObject( ref Utf8JsonReader r, string typeName )
    {
        if( r.TokenType != JsonTokenType.EndObject )
        {
            throw new JsonException( $"Expected '{typeName}' object end, got '{r.TokenType}'." );
        }
    }

    /// <summary>
    /// Ensures that the reader is on a <see cref="JsonTokenType.StartArray"/> token.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    internal static void EnsureStartArray( ref Utf8JsonReader r, string propertyName )
    {
        if( r.TokenType != JsonTokenType.StartArray )
        {
            throw new JsonException( $"Expected an array for '{propertyName}', got '{r.TokenType}'." );
        }
    }

    /// <summary>
    /// Ensures that the reader is on a <see cref="JsonTokenType.EndArray"/> token.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    internal static void EnsureEndArray( ref Utf8JsonReader r, string propertyName )
    {
        if( r.TokenType != JsonTokenType.EndArray )
        {
            throw new JsonException( $"Expected the end of the '{propertyName}' array, got '{r.TokenType}'." );
        }
    }

    /// <summary>
    /// Reads the value of a property and moves the reader to its value token.
    /// </summary>
    /// <param name="r">The reader (on a <see cref="JsonTokenType.PropertyName"/> token).</param>
    /// <returns>The property name.</returns>
    internal static string StartProperty( ref Utf8JsonReader r )
    {
        var name = r.GetString();
        if( name == null ) throw new JsonException( "Unexpected null property name." );
        if( !r.Read() ) throw new JsonException( $"Missing value for property '{name}'." );
        return name;
    }

    /// <summary>
    /// Gets a non null string value.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The string.</returns>
    internal static string GetString( ref Utf8JsonReader r, string propertyName )
    {
        if( r.TokenType != JsonTokenType.String )
        {
            throw new JsonException( $"Expected a string for '{propertyName}', got '{r.TokenType}'." );
        }
        return r.GetString()!;
    }

    /// <summary>
    /// Gets a boolean value.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The boolean.</returns>
    internal static bool GetBoolean( ref Utf8JsonReader r, string propertyName )
    {
        if( r.TokenType is not JsonTokenType.True and not JsonTokenType.False )
        {
            throw new JsonException( $"Expected a boolean for '{propertyName}', got '{r.TokenType}'." );
        }
        return r.GetBoolean();
    }

    /// <summary>
    /// Gets an absolute url.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The url.</returns>
    internal static Uri GetUri( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        return Uri.TryCreate( s, UriKind.Absolute, out var url )
                ? url
                : throw new JsonException( $"Expected an absolute url for '{propertyName}', got '{s}'." );
    }

    /// <summary>
    /// Gets a Conformant SVersion.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The version.</returns>
    internal static SVersion GetVersion( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        return SVersion.TryParse( s, out var v, mustBeCSVersion: true )
                ? v
                : throw new JsonException( $"Expected a Conformant SVersion for '{propertyName}', got '{s}'." );
    }

    /// <summary>
    /// Gets a SemVer version. Unlike <see cref="GetVersion"/>, no CSemVer conformance is required: an
    /// external package's version is any SemVer.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The version.</returns>
    internal static SVersion GetPackageVersion( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        return SVersion.TryParse( s, out var v )
                ? v
                : throw new JsonException( $"Expected a SemVer version for '{propertyName}', got '{s}'." );
    }

    /// <summary>
    /// Gets a <see cref="VersionSource"/> from its name.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The version source.</returns>
    internal static VersionSource GetVersionSource( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        // The names are matched exactly: Enum.TryParse would also accept a numerical value and any
        // casing, and this is a stored format.
        return s switch
        {
            nameof( VersionSource.TransitiveDependencies ) => VersionSource.TransitiveDependencies,
            nameof( VersionSource.DirectDependencies ) => VersionSource.DirectDependencies,
            nameof( VersionSource.ProducedPackages ) => VersionSource.ProducedPackages,
            _ => throw new JsonException( $"Expected \"{nameof( VersionSource.TransitiveDependencies )}\", "
                                          + $"\"{nameof( VersionSource.DirectDependencies )}\" or "
                                          + $"\"{nameof( VersionSource.ProducedPackages )}\" for "
                                          + $"'{propertyName}', got '{s}'." )
        };
    }

    /// <summary>
    /// Reads an array of "packageId@version" strings.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartArray"/> token and is left
    /// on the <see cref="JsonTokenType.EndArray"/> one.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The package instances.</returns>
    internal static ImmutableArray<PackageInstance>.Builder ReadPackageInstances( ref Utf8JsonReader r,
                                                                                  string propertyName )
    {
        EnsureStartArray( ref r, propertyName );
        var packages = ImmutableArray.CreateBuilder<PackageInstance>();
        while( r.Read() && r.TokenType != JsonTokenType.EndArray )
        {
            packages.Add( GetPackageInstance( ref r, propertyName ) );
        }
        EnsureEndArray( ref r, propertyName );
        return packages;
    }

    /// <summary>
    /// Reads an array of <see cref="RandomId"/> strings.
    /// <para>
    /// The <paramref name="r"/> must be on the <see cref="JsonTokenType.StartArray"/> token and is left
    /// on the <see cref="JsonTokenType.EndArray"/> one.
    /// </para>
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The identifiers.</returns>
    internal static ImmutableArray<RandomId>.Builder ReadRandomIds( ref Utf8JsonReader r, string propertyName )
    {
        EnsureStartArray( ref r, propertyName );
        var ids = ImmutableArray.CreateBuilder<RandomId>();
        while( r.Read() && r.TokenType != JsonTokenType.EndArray )
        {
            ids.Add( GetRandomId( ref r, propertyName ) );
        }
        EnsureEndArray( ref r, propertyName );
        return ids;
    }

    /// <summary>
    /// Gets a valid <see cref="RandomId"/>.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The identifier.</returns>
    internal static RandomId GetRandomId( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        return RandomId.TryParse( s, out var id ) && id.IsValid
                ? id
                : throw new JsonException( $"Expected a valid RandomId for '{propertyName}', got '{s}'." );
    }

    /// <summary>
    /// Gets a <see cref="WorldName"/> from its <see cref="WorldName.FullName"/>.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The world name.</returns>
    internal static WorldName GetWorldName( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        return WorldName.TryParse( s, out var world )
                ? world
                : throw new JsonException( $"Expected a world full name for '{propertyName}', got '{s}'." );
    }

    /// <summary>
    /// Gets a <see cref="PackageInstance"/> from its "packageId@version" representation.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="propertyName">The property name (used by the error message).</param>
    /// <returns>The package instance.</returns>
    internal static PackageInstance GetPackageInstance( ref Utf8JsonReader r, string propertyName )
    {
        var s = GetString( ref r, propertyName );
        // PackageInstance.TryParse allows a trailing suffix: TryMatch is used here to
        // require the whole string to be the package instance.
        var head = s.AsSpan();
        return PackageInstance.TryMatch( ref head, out var p ) && head.Length == 0
                ? p
                : throw new JsonException( $"Expected a \"packageId@version\" string in '{propertyName}', got '{s}'." );
    }

    /// <summary>
    /// Gets a required property value or throws.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The read value (null when the property was missing).</param>
    /// <param name="propertyName">The missing property name.</param>
    /// <param name="typeName">The read type name.</param>
    /// <returns>The non null value.</returns>
    internal static T Required<T>( T? value, string propertyName, string typeName ) where T : class
    {
        return value ?? throw new JsonException( $"Missing '{propertyName}' property in '{typeName}'." );
    }
}
