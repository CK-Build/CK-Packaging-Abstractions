using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;

namespace CK.Packaging.Abstractions.Tests;

[TestFixture]
public class RandomIdTests
{
    [Test]
    public void the_default_identifier_is_invalid()
    {
        var id = default( RandomId );

        id.IsValid.ShouldBeFalse();
        id.Value.ShouldBe( 0ul );
        id.ToString().ShouldBe( "AAAAAAAAAAA" );
        id.ShouldBe( new RandomId( 0 ) );
        new RandomId( 0 ).ToString().ShouldBe( "AAAAAAAAAAA" );
        // The invalid identifier can be parsed: it is a valid representation.
        new RandomId( "AAAAAAAAAAA" ).IsValid.ShouldBeFalse();
    }

    [TestCase( 1ul, "AQAAAAAAAAA" )]
    [TestCase( 2ul, "AgAAAAAAAAA" )]
    [TestCase( ulong.MaxValue, "__________8" )]
    public void identifier_string_is_the_Base64Url_of_its_value( ulong value, string expected )
    {
        // This test documents the (little endian) encoding: the expected strings have
        // been captured from the implementation.
        var id = new RandomId( value );
        id.IsValid.ShouldBeTrue();
        id.ToString().ShouldBe( expected );
        new RandomId( expected ).ShouldBe( id );
    }

    [Test]
    public void CreateRandom_is_valid_and_round_trips()
    {
        for( int i = 0; i < 50; ++i )
        {
            var id = RandomId.CreateRandom();
            id.IsValid.ShouldBeTrue();
            id.ToString().Length.ShouldBe( RandomId.StringLength );

            var back = new RandomId( id.ToString() );
            back.ShouldBe( id );
            back.Value.ShouldBe( id.Value );
            back.ToString().ShouldBe( id.ToString() );
            RandomId.Parse( id.ToString() ).ShouldBe( id );
            RandomId.TryParse( id.ToString(), out var t ).ShouldBeTrue();
            t.ShouldBe( id );
        }
    }

    [TestCase( "" )]
    [TestCase( "AQAAAAAAAA" )]
    [TestCase( "AQAAAAAAAAAA" )]
    [TestCase( "AQAAA!AAAAA" )]
    [TestCase( "AQAAA+AAAAA" )]
    [TestCase( "AQAAA/AAAAA" )]
    public void invalid_string_representations_are_rejected( string s )
    {
        RandomId.TryParse( s, out var id ).ShouldBeFalse();
        id.ShouldBe( default( RandomId ) );
        Should.Throw<FormatException>( () => RandomId.Parse( s ) );
        Should.Throw<FormatException>( () => new RandomId( s ) );
    }

    [Test]
    public void the_string_constructor_rejects_null()
    {
        // The CKli.Core implementation that this type replaces silently accepted null and
        // produced the invalid identifier. A constructor that is documented to throw on an
        // invalid string must not special case null: use default or new RandomId( 0 ) for
        // the invalid identifier.
        Should.Throw<ArgumentNullException>( () => new RandomId( (string)null! ) );
    }

    [Test]
    public void TryMatch_forwards_the_head_but_TryParse_requires_the_whole_string()
    {
        var head = "AQAAAAAAAAA/and the rest".AsSpan();
        RandomId.TryMatch( ref head, out var id ).ShouldBeTrue();
        id.ShouldBe( new RandomId( 1 ) );
        head.ToString().ShouldBe( "/and the rest" );

        RandomId.TryParse( "AQAAAAAAAAA/and the rest", out _ ).ShouldBeFalse();
    }

    [Test]
    public void identifiers_are_comparable_and_hashable()
    {
        var i1 = new RandomId( 1 );
        var i2 = new RandomId( 2 );

        (i1 < i2).ShouldBeTrue();
        (i1 <= i2).ShouldBeTrue();
        (i2 > i1).ShouldBeTrue();
        (i2 >= i1).ShouldBeTrue();
        (i1 == new RandomId( 1 )).ShouldBeTrue();
        (i1 != i2).ShouldBeTrue();
        i1.CompareTo( i2 ).ShouldBeLessThan( 0 );
        i1.Equals( (object)new RandomId( 1 ) ).ShouldBeTrue();
        i1.Equals( (object?)null ).ShouldBeFalse();

        var set = new HashSet<RandomId> { i1, new RandomId( 1 ), i2 };
        set.Count.ShouldBe( 2 );
    }

    [Test]
    public void ISpanParsable_and_IParsable_are_supported()
    {
        var id = RandomId.CreateRandom();
        var s = id.ToString();

        ParseSpan<RandomId>( s ).ShouldBe( id );
        ParseString<RandomId>( s ).ShouldBe( id );
        TryParseSpan<RandomId>( "no way", out _ ).ShouldBeFalse();
        TryParseString<RandomId>( null, out _ ).ShouldBeFalse();
        TryParseString<RandomId>( s, out var t ).ShouldBeTrue();
        t.ShouldBe( id );

        static T ParseSpan<T>( string s ) where T : ISpanParsable<T> => T.Parse( s.AsSpan(), null );

        static T ParseString<T>( string s ) where T : IParsable<T> => T.Parse( s, null );

        static bool TryParseSpan<T>( string s, out T? r ) where T : ISpanParsable<T>
            => T.TryParse( s.AsSpan(), null, out r );

        static bool TryParseString<T>( string? s, out T? r ) where T : IParsable<T>
            => T.TryParse( s, null, out r );
    }
}
