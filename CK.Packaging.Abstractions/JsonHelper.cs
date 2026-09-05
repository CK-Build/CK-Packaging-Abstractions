using CK.Core;
using System;
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
