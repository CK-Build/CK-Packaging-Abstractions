using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CK.Core;

/// <summary>
/// Encapsulates a numeric/string identifier.
/// <para>
/// The <c>default</c> value is invalid, Value is 0 and its string representation is "AAAAAAAAAAA".
/// </para>
/// </summary>
public readonly struct RandomId : ISpanParsable<RandomId>, IEquatable<RandomId>, IComparable<RandomId>
{
    /// <summary>
    /// The length of the string representation of an identifier: 8 bytes Base64Url encoded.
    /// </summary>
    public const int StringLength = 11;

    static readonly string _invalidString = "AAAAAAAAAAA";
    readonly string? _s;

    /// <summary>
    /// The numeric value. 0 is invalid.
    /// </summary>
    public readonly ulong Value;

    /// <summary>
    /// Gets whether this identifier is valid.
    /// </summary>
    public bool IsValid => Value != 0;

    /// <summary>
    /// Initializes a new repository identifier.
    /// </summary>
    /// <param name="value">The numerical value. When 0, this id is invalid.</param>
    public RandomId( ulong value )
    {
        Value = value;
        var s = new Span<ulong>( ref value );
        var b = MemoryMarshal.AsBytes( s );
        if( value != 0 )
        {
            _s = Base64Url.EncodeToString( b );
        }
        else
        {
            Debug.Assert( Base64Url.EncodeToString( b ) == _invalidString );
            _s = _invalidString;
        }
    }

    /// <summary>
    /// Initializes a new repository identifier from its string representation. Throws a
    /// <see cref="FormatException"/> if the string is not a <see cref="StringLength"/> characters
    /// Base64Url encoded unsigned long.
    /// <para>
    /// "AAAAAAAAAAA" is valid: it is the invalid identifier (<see cref="IsValid"/> is false).
    /// </para>
    /// </summary>
    /// <param name="s">The string value.</param>
    public RandomId( string s )
    {
        ArgumentNullException.ThrowIfNull( s );
        this = Parse( s.AsSpan() );
    }

    /// <inheritdoc />
    public bool Equals( RandomId other ) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals( [NotNullWhen( true )] object? obj ) => obj is RandomId i && Value == i.Value;

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo( RandomId other ) => Value.CompareTo( other.Value );

    /// <summary>
    /// Returns this identifier as a string. When <see cref="IsValid"/> is false, this is "AAAAAAAAAAA".
    /// </summary>
    /// <returns>This identifier.</returns>
    public override string ToString() => _s ?? _invalidString;

    RandomId( ulong v, string s )
    {
        _s = s;
        Value = v;
    }

    /// <summary>
    /// Creates a new random identifier.
    /// </summary>
    /// <returns>A new valid random identifier.</returns>
    public static RandomId CreateRandom()
    {
        ulong value = 0ul;
        var s = new Span<ulong>( ref value );
        Span<byte> bytes = MemoryMarshal.AsBytes( s );
        do
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill( bytes );
        }
        while( value == 0 );
        return new RandomId( value );
    }

    /// <summary>
    /// Tries to match a valid string value. The head is forwarded on success.
    /// </summary>
    /// <param name="head">The head to match.</param>
    /// <param name="id">The resulting identifier.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out RandomId id )
    {
        if( head.Length >= StringLength )
        {
            ulong value = 0;
            var s = new Span<ulong>( ref value );
            Span<byte> bytes = MemoryMarshal.AsBytes( s );
            var op = Base64Url.DecodeFromChars( head.Slice( 0, StringLength ), bytes, out int charsConsumed, out int bytesWritten );
            if( op == OperationStatus.Done && charsConsumed == StringLength && bytesWritten == sizeof( ulong ) )
            {
                id = new RandomId( value, new string( head.Slice( 0, charsConsumed ) ) );
                head = head.Slice( charsConsumed );
                return true;
            }
        }
        id = default;
        return false;
    }

    /// <summary>
    /// Tries to parse an identifier. The whole <paramref name="s"/> must be the identifier.
    /// It can be the invalid one ("AAAAAAAAAAA").
    /// </summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="id">The result.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( ReadOnlySpan<char> s, out RandomId id )
    {
        if( TryMatch( ref s, out id ) && s.Length == 0 ) return true;
        id = default;
        return false;
    }

    /// <summary>
    /// Tries to parse an identifier or throws a <see cref="FormatException"/>.
    /// </summary>
    /// <param name="s">The text to parse.</param>
    /// <returns>The parsed identifier.</returns>
    public static RandomId Parse( ReadOnlySpan<char> s )
    {
        if( TryParse( s, out var id ) ) return id;
        throw new FormatException( $"Invalid RandomId: '{s}'." );
    }

    static RandomId ISpanParsable<RandomId>.Parse( ReadOnlySpan<char> s, IFormatProvider? provider )
    {
        return Parse( s );
    }

    /// <inheritdoc />
    static bool ISpanParsable<RandomId>.TryParse( ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen( false )] out RandomId result )
    {
        return TryParse( s, out result );
    }

    /// <inheritdoc />
    static RandomId IParsable<RandomId>.Parse( string s, IFormatProvider? provider )
    {
        return Parse( s.AsSpan() );
    }

    /// <inheritdoc />
    static bool IParsable<RandomId>.TryParse( [NotNullWhen( true )] string? s, IFormatProvider? provider, [MaybeNullWhen( false )] out RandomId result )
    {
        return TryParse( s.AsSpan(), out result );
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

    public static bool operator ==( RandomId left, RandomId right ) => left.Equals( right );

    public static bool operator !=( RandomId left, RandomId right ) => !(left == right);

    public static bool operator <( RandomId left, RandomId right ) => left.CompareTo( right ) < 0;

    public static bool operator <=( RandomId left, RandomId right ) => left.CompareTo( right ) <= 0;

    public static bool operator >( RandomId left, RandomId right ) => left.CompareTo( right ) > 0;

    public static bool operator >=( RandomId left, RandomId right ) => left.CompareTo( right ) >= 0;

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
