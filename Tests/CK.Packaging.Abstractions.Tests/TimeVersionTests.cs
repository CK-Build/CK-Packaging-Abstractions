using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Text;

namespace CK.Packaging.Abstractions.Tests;

[TestFixture]
public class TimeVersionTests
{
    [TestCase( "2026.11.21", 0, "", -1 )]
    [TestCase( "v2026.11.21", 0, "", -1 )]
    [TestCase( "2026.11.21.1", 1, "", -1 )]
    [TestCase( "2026.11.21.3--ci.0", 3, "", 0 )]
    [TestCase( "2026.11.21--ci.0", 0, "", 0 )]
    [TestCase( "2026.11.21-alpha", 0, "alpha", -1 )]
    [TestCase( "2026.11.21-zulu.0.ci.12", 0, "zulu", 12 )]
    [TestCase( "2026.11.21-0.some-explo", 0, "explo/some-explo", -1 )]
    [TestCase( "2026.11.21.2-0.some-explo.0.ci.3", 2, "explo/some-explo", 3 )]
    public void parsing_a_TimeVersion( string s, int clashNumber, string branchName, int ciNumber )
    {
        var v = TimeVersion.Parse( s );
        v.Date.ShouldBe( new DateOnly( 2026, 11, 21 ) );
        v.ClashNumber.ShouldBe( clashNumber );
        v.BranchName.ShouldBe( branchName );
        v.CINumber.ShouldBe( ciNumber );
        v.IsCI.ShouldBe( ciNumber >= 0 );
        v.ToString().ShouldBe( s.TrimStart( 'v' ) );
        // The string form is the one of the SVersion: the clash number is the fourth part, when not 0.
        var sv = v.ToSVersion();
        sv.FourthPart.ShouldBe( clashNumber == 0 ? -1 : clashNumber );
        sv.ToString().ShouldBe( v.ToString() );
        sv.AsTimeVersion().ShouldBe( v );
    }

    [TestCase( "2026.11.21.0" )]                // A 0 clash number has no fourth part: one string per version.
    [TestCase( "2026.11.21.0-alpha" )]
    [TestCase( "2026.13.1" )]                   // Invalid month (and not accepted as a legacy DayOfYear).
    [TestCase( "2026.11.0" )]                   // Invalid day.
    [TestCase( "2026.2.29" )]                   // Not a leap year.
    [TestCase( "0.1.1" )]                       // Year 0.
    [TestCase( "2026.11.21-beta2" )]            // Not a Conformant prerelease.
    [TestCase( "2026.11.21-alpha.1" )]          // A TimeVersion has no prerelease number.
    [TestCase( "2026.11.21-alpha.0" )]          // Not a Conformant prerelease.
    [TestCase( "2026.11.21+meta" )]             // No build metadata.
    [TestCase( "not a version" )]
    [TestCase( null )]
    public void invalid_TimeVersion( string? s )
    {
        TimeVersion.TryParse( s, out var v ).ShouldBeFalse();
        v.ShouldBeNull();
        Should.Throw<ArgumentException>( () => TimeVersion.Parse( s ) );
    }

    [Test]
    public void leap_day_is_valid()
    {
        TimeVersion.Parse( "2028.2.29" ).Date.ShouldBe( new DateOnly( 2028, 2, 29 ) );
    }

    // The "Year.DayOfYear.Number" form is the one of the profiles published before the TimeVersion
    // (since September 2026: the DayOfYear is greater than 12).
    [TestCase( "2026.325.0", "2026.11.21" )]
    [TestCase( "2026.325.2", "2026.11.21.2" )]
    [TestCase( "2026.13.0", "2026.1.13" )]
    [TestCase( "2026.365.0", "2026.12.31" )]
    [TestCase( "2028.366.0", "2028.12.31" )]
    [TestCase( "2026.325.1--ci.0", "2026.11.21.1--ci.0" )]
    [TestCase( "2026.325.0-alpha.0.ci.0", "2026.11.21-alpha.0.ci.0" )]
    [TestCase( "2026.325.0-0.spike", "2026.11.21-0.spike" )]
    public void the_DayOfYear_legacy_form_is_converted_on_demand( string legacy, string expected )
    {
        TimeVersion.TryParse( legacy, out _ ).ShouldBeFalse();
        TimeVersion.Parse( legacy, acceptDayOfYear: true ).ToString().ShouldBe( expected );
        SVersion.Parse( legacy ).AsTimeVersion().ShouldBeNull();
        SVersion.Parse( legacy ).AsTimeVersion( acceptDayOfYear: true )!.ToString().ShouldBe( expected );
    }

    [Test]
    public void a_second_part_up_to_12_is_always_a_month()
    {
        TimeVersion.Parse( "2026.5.3", acceptDayOfYear: true ).ToString().ShouldBe( "2026.5.3" );
        TimeVersion.Parse( "2026.12.31.1", acceptDayOfYear: true ).ToString().ShouldBe( "2026.12.31.1" );
    }

    [TestCase( "2026.0.0" )]
    [TestCase( "2026.366.0" )]
    [TestCase( "2026.325.0.1" )]                // The legacy form has no fourth part.
    [TestCase( "2026.325.0-beta" )]
    [TestCase( "2026.325.0-alpha.1" )]
    public void invalid_DayOfYear_legacy_form( string legacy )
    {
        TimeVersion.TryParse( legacy, out _, acceptDayOfYear: true ).ShouldBeFalse();
    }

    [Test]
    public void updaters_follow_the_CSVersion_semantics()
    {
        var v = TimeVersion.Create( new DateOnly( 2026, 11, 21 ) );
        v.ToString().ShouldBe( "2026.11.21" );
        v.VersionKind.ShouldBe( CSVersionKind.Stable );
        v.SetClashNumber( 0 ).ShouldBeSameAs( v );
        v.SetClashNumber( 1 ).ToString().ShouldBe( "2026.11.21.1" );
        v.SetClashNumber( 1 ).SetClashNumber( 0 ).ShouldBe( v );

        var ci = v.SetCINumber( 0 );
        ci.ToString().ShouldBe( "2026.11.21--ci.0" );
        // Unlike SVersion.SetCINumber, no other part is impacted.
        ci.ClashNumber.ShouldBe( 0 );
        ci.SetCINumber( -1 ).ShouldBe( v );

        var alpha = ci.SetBranchName( CSVersionKind.Alpha );
        alpha.ToString().ShouldBe( "2026.11.21-alpha.0.ci.0" );
        alpha.BranchName.ShouldBe( "alpha" );
        alpha.SetCINumber( -1 ).ToString().ShouldBe( "2026.11.21-alpha" );
        alpha.SetBranchName( "" ).ShouldBe( ci );

        var explo = v.SetExploratoryName( "explo/spike" ).SetClashNumber( 3 );
        explo.ToString().ShouldBe( "2026.11.21.3-0.spike" );
        explo.VersionKind.ShouldBe( CSVersionKind.Exploratory );
        explo.ExploratoryName.ToString().ShouldBe( "spike" );
        explo.BranchName.ShouldBe( "explo/spike" );
        v.SetBranchName( "explo/spike" ).SetClashNumber( 3 ).ShouldBe( explo );

        Should.Throw<ArgumentException>( () => v.SetBranchName( "nope" ) );
        Should.Throw<ArgumentException>( () => v.SetExploratoryName( "ci-is-reserved" ) );
        Should.Throw<ArgumentOutOfRangeException>( () => v.SetClashNumber( -1 ) );
        Should.Throw<ArgumentOutOfRangeException>( () => TimeVersion.Create( new DateOnly( 2026, 11, 21 ), -1 ) );
    }

    [Test]
    public void ordering_is_by_date_then_clash_number_then_branch()
    {
        var versions = new[] { "2026.11.21.10", "2026.11.21.9", "2027.1.1", "2026.11.21", "2026.12.1", "2026.2.1.5" }
                            .Select( TestModel.TV )
                            .Order()
                            .Select( v => v.ToString() );
        versions.ShouldBe( ["2026.2.1.5", "2026.11.21", "2026.11.21.9", "2026.11.21.10", "2026.12.1", "2027.1.1"] );

        // Same date and clash number: the Conformant SVersion ordering of the branch and CI build (where the
        // stable "--ci" form is lower than the stable one since it relies on a Patch increment). This doesn't
        // matter: versions are grouped by branch and CI build, and the publications of a branch share their
        // clash numbers.
        (TestModel.TV( "2026.11.21-alpha" ) < TestModel.TV( "2026.11.21" )).ShouldBeTrue();
        (TestModel.TV( "2026.11.21--ci.0" ) < TestModel.TV( "2026.11.21" )).ShouldBeTrue();
        (TestModel.TV( "2026.11.21--ci.0" ) < TestModel.TV( "2026.11.21--ci.1" )).ShouldBeTrue();
        // But the clash number comes first.
        (TestModel.TV( "2026.11.21" ) < TestModel.TV( "2026.11.21.1-alpha" )).ShouldBeTrue();
    }

    [Test]
    public void equality_and_hash()
    {
        var a = TestModel.TV( "2026.11.21-alpha" );
        var b = TimeVersion.Create( new DateOnly( 2026, 11, 21 ) ).SetBranchName( CSVersionKind.Alpha );
        a.ShouldNotBeSameAs( b );
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe( b.GetHashCode() );
        a.CompareTo( b ).ShouldBe( 0 );
        (a != TestModel.TV( "2026.11.21" )).ShouldBeTrue();
        (a != TestModel.TV( "2026.11.21.1-alpha" )).ShouldBeTrue();
        (a != TestModel.TV( "2026.11.21-alpha.0.ci.0" )).ShouldBeTrue();
    }

    [Test]
    public void legacy_profile_and_index_are_read()
    {
        // Profiles and indexes written before the TimeVersion carry "Year.DayOfYear.Number" versions.
        var profile = """
            {
                "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
                "World": "CKt",
                "Version": "2026.325.1-alpha",
                "Repositories": []
            }
            """;
        PublishedProfile.Parse( Encoding.UTF8.GetBytes( profile ) ).Version.ToString().ShouldBe( "2026.11.21.1-alpha" );

        var index = """{ "Alive": { "(stable)": [ "2026.324.0", "2026.325.0" ], "alpha": [ "2026.325.1-alpha" ] } }""";
        var i = PublishedIndex.Parse( Encoding.UTF8.GetBytes( index ) );
        i.GetAlive( "(stable)" ).Select( v => v.ToString() ).ShouldBe( ["2026.11.21", "2026.11.20"] );
        i.GetAlive( "alpha" ).Select( v => v.ToString() ).ShouldBe( ["2026.11.21.1-alpha"] );
    }

    [TestCase( "2026.11.21", "v2026.11.21.json" )]
    [TestCase( "2026.11.21.1", "v2026.11.21.1.json" )]
    [TestCase( "2026.11.21.1--ci.0", "v2026.11.21.1--ci.0.json" )]
    [TestCase( "2026.11.21-alpha", "alpha/v2026.11.21-alpha.json" )]
    [TestCase( "2026.11.21-0.spike.0.ci.0", "explo/spike/v2026.11.21-0.spike.0.ci.0.json" )]
    public void profile_path( string version, string path )
    {
        PublishedProfile.GetProfilePath( TestModel.TV( version ), suffix: ".json" ).ShouldBe( path );
    }

    [TestCase( "2026.11.21", "v2026.325.0.json" )]
    [TestCase( "2026.11.21.1--ci.0", "v2026.325.1--ci.0.json" )]
    [TestCase( "2026.1.13.3-alpha", "alpha/v2026.13.3-alpha.json" )]
    [TestCase( "2026.11.21-0.spike.0.ci.0", "explo/spike/v2026.325.0-0.spike.0.ci.0.json" )]
    public void legacy_profile_path( string version, string path )
    {
        var v = TestModel.TV( version );
        PublishedProfile.GetLegacyProfilePath( v, suffix: ".json" ).ShouldBe( path );
        // The legacy file name is the legacy version: it converts back.
        var legacy = path.Substring( path.LastIndexOf( '/' ) + 2 ).Replace( ".json", "" );
        TimeVersion.Parse( legacy, acceptDayOfYear: true ).ShouldBe( v );
    }
}
