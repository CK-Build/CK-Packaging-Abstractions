using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;

namespace CK.Packaging.Abstractions.Tests;

[TestFixture]
public class WorldNameTests
{
    [TestCase( "CK", true )]
    [TestCase( "CKli", true )]
    [TestCase( "CK-Build", true )]
    [TestCase( "CK_Build_2", true )]
    [TestCase( "C", false, "At least 2 characters." )]
    [TestCase( "", false )]
    [TestCase( "1CK", false, "Must start with a letter." )]
    [TestCase( "-CK", false )]
    [TestCase( "CK Build", false, "No space." )]
    [TestCase( "CK/Build", false )]
    [TestCase( "no way!", false, "The whole name must match: this is the anchoring regression." )]
    [TestCase( "!CKli", false )]
    [TestCase( "CKli!", false )]
    [TestCase( "CKli\n", false, "$ would match before a trailing newline: \\z is used." )]
    public void IsValidRepositoryName_tests( string name, bool valid, string? reason = null )
    {
        WorldName.IsValidRepositoryName( name ).ShouldBe( valid, reason );
    }

    [TestCase( "@lts", true )]
    [TestCase( "@v1.0", true )]
    [TestCase( "@2024_q3-b", true )]
    [TestCase( "@l", false, "At least 3 characters." )]
    [TestCase( "@", false )]
    [TestCase( "lts", false, "Must start with '@'." )]
    [TestCase( "@LTS", false, "Lowercase only." )]
    [TestCase( "@lts/x", false )]
    [TestCase( "CKli@lts", false, "This is a full name, not a LTS name." )]
    public void IsValidLTSName_tests( string name, bool valid, string? reason = null )
    {
        WorldName.IsValidLTSName( name ).ShouldBe( valid, reason );
    }

    [Test]
    public void a_default_world_has_no_LTSName()
    {
        var w = WorldName.Parse( "CKli" );

        w.StackName.ShouldBe( "CKli" );
        w.LTSName.ShouldBeNull();
        w.IsDefaultWorld.ShouldBeTrue();
        w.FullName.ShouldBe( "CKli" );
        w.ToString().ShouldBe( "CKli" );
        w.EnsureLTSPrefix( "dev/stable" ).ShouldBe( "dev/stable" );
    }

    [Test]
    public void a_LTS_world_has_its_LTSName_in_its_FullName()
    {
        var w = WorldName.Parse( "CKli@v1.0" );

        w.StackName.ShouldBe( "CKli" );
        w.LTSName.ShouldBe( "@v1.0" );
        w.IsDefaultWorld.ShouldBeFalse();
        w.FullName.ShouldBe( "CKli@v1.0" );
        w.EnsureLTSPrefix( "dev/stable" ).ShouldBe( "@v1.0/dev/stable" );
        w.EnsureLTSPrefix( "@v1.0/dev/stable" ).ShouldBe( "@v1.0/dev/stable", "Idempotent." );
    }

    [Test]
    public void an_empty_or_white_LTSName_is_normalized_to_null()
    {
        new WorldName( "CKli", null ).IsDefaultWorld.ShouldBeTrue();
        new WorldName( "CKli", "" ).IsDefaultWorld.ShouldBeTrue();
        new WorldName( "CKli", "   " ).IsDefaultWorld.ShouldBeTrue();
    }

    [TestCase( null )]
    [TestCase( "" )]
    [TestCase( "   " )]
    [TestCase( "no way!" )]
    [TestCase( "C" )]
    [TestCase( "CKli@LTS" )]
    [TestCase( "CKli@" )]
    [TestCase( "@lts" )]
    public void invalid_full_names_are_rejected( string? fullName )
    {
        WorldName.TryParse( fullName ).ShouldBeNull();
        WorldName.TryParse( fullName, out var name ).ShouldBeFalse();
        name.ShouldBeNull();
        Should.Throw<ArgumentException>( () => WorldName.Parse( fullName ) );
    }

    [Test]
    public void invalid_names_are_rejected_by_the_constructor()
    {
        Should.Throw<ArgumentException>( () => new WorldName( "no way!", null ) )
              .Message.ShouldStartWith( "Invalid stackName." );
        Should.Throw<ArgumentException>( () => new WorldName( "CKli", "@NoWay" ) )
              .Message.ShouldStartWith( "Invalid ltsName." );
    }

    [Test]
    public void equality_is_case_insensitive_on_the_FullName()
    {
        var w = WorldName.Parse( "CKli" );

        w.Equals( WorldName.Parse( "ckli" ) ).ShouldBeTrue();
        w.Equals( (object)WorldName.Parse( "CKLI" ) ).ShouldBeTrue();
        w.GetHashCode().ShouldBe( WorldName.Parse( "cKLi" ).GetHashCode() );
        w.Equals( WorldName.Parse( "CKli@v1.0" ) ).ShouldBeFalse();
        w.Equals( (WorldName?)null ).ShouldBeFalse();
        w.Equals( (object)"CKli" ).ShouldBeFalse();
    }
}
