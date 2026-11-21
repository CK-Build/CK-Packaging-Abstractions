using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CK.Packaging.Abstractions.Tests;

[TestFixture]
public class PublishedIndexTests
{
    // The group name of a version, from the version alone. These are the cases the index must separate.
    [TestCase( "2026.11.20", "(stable)" )]
    [TestCase( "2026.11.20--ci.0", "(stable-ci)" )]
    [TestCase( "2026.11.21-alpha", "alpha" )]
    [TestCase( "2026.11.21.1-alpha.0.ci.7", "alpha-ci" )]
    [TestCase( "2026.11.21-zulu", "zulu" )]
    [TestCase( "2026.11.21-zulu.0.ci.12", "zulu-ci" )]
    [TestCase( "2026.11.21-0.some-explo", "explo/some-explo" )]
    public void group_name_is_a_function_of_the_version( string version, string groupName )
    {
        PublishedIndex.GetGroupName( TestModel.TV( version ) ).ShouldBe( groupName );
    }

    [Test]
    public void group_name_of_a_branch_takes_the_version_side_branch_name()
    {
        // The empty branch name is the stable line: this is the overload a consumer uses to look a
        // branch up, and the empty string is what TimeVersion.BranchName answers for a stable version.
        PublishedIndex.GetGroupName( "", isCI: false ).ShouldBe( PublishedIndex.StableGroupName );
        PublishedIndex.GetGroupName( "", isCI: true ).ShouldBe( PublishedIndex.StableCIGroupName );
        PublishedIndex.GetGroupName( "alpha", isCI: false ).ShouldBe( "alpha" );
        PublishedIndex.GetGroupName( "alpha", isCI: true ).ShouldBe( "alpha-ci" );
        PublishedIndex.GetGroupName( "explo/spike", isCI: false ).ShouldBe( "explo/spike" );
        PublishedIndex.GetGroupName( "explo/spike", isCI: true ).ShouldBe( "explo/spike-ci" );
    }

    [Test]
    public void the_empty_index_still_carries_the_stable_group()
    {
        var i = PublishedIndex.Empty;

        i.IsEmpty.ShouldBeTrue();
        i.ToString().ShouldBe( "0 alive, 0 deprecated" );
        // Seeded in both sets, so a consumer always has the root list to read.
        i.Alive.Keys.ShouldBe( new[] { PublishedIndex.StableGroupName } );
        i.Deprecated.Keys.ShouldBe( new[] { PublishedIndex.StableGroupName } );
        i.Alive[PublishedIndex.StableGroupName].ShouldBeEmpty();
        // An absent group is an empty one.
        i.GetAlive( "alpha" ).ShouldBeEmpty();
        i.GetDeprecated( "explo/nope" ).ShouldBeEmpty();
    }

    [Test]
    public void versions_are_grouped_and_ordered_from_the_latest_to_the_oldest()
    {
        // Deliberately unordered, and the stable versions are not adjacent.
        var i = new PublishedIndex( Versions( "2026.11.20", "2026.11.21.1-alpha.0.ci.7", "2026.11.21", "2026.11.21.1--ci.3",
                                              "2026.11.21-0.some-explo", "2026.11.22--ci.0", "2026.11.21-zulu.0.ci.12" ),
                                    Versions( "2026.11.20.1", "2026.11.21-alpha" ) );

        i.IsEmpty.ShouldBeFalse();
        i.ToString().ShouldBe( "7 alive, 2 deprecated" );

        // Ordinal order: "(stable)" first ('(' precedes every letter), then a group immediately before
        // its own CI one (')' and the end of a string both precede '-').
        i.Alive.Keys.ShouldBe( new[] { "(stable)", "(stable-ci)", "alpha-ci", "explo/some-explo", "zulu-ci" } );
        i.Deprecated.Keys.ShouldBe( new[] { "(stable)", "alpha" } );

        Text( i.GetAlive( "(stable)" ) ).ShouldBe( new[] { "2026.11.21", "2026.11.20" } );
        Text( i.GetAlive( "(stable-ci)" ) ).ShouldBe( new[] { "2026.11.22--ci.0", "2026.11.21.1--ci.3" } );
        Text( i.GetAlive( "alpha-ci" ) ).ShouldBe( new[] { "2026.11.21.1-alpha.0.ci.7" } );
        Text( i.GetDeprecated( "(stable)" ) ).ShouldBe( new[] { "2026.11.20.1" } );
        Text( i.GetDeprecated( "alpha" ) ).ShouldBe( new[] { "2026.11.21-alpha" } );

        // A version is in one set only: the alive stable group doesn't see the deprecated 2026.11.20.1.
        i.GetAlive( "alpha" ).ShouldBeEmpty();
    }

    [Test]
    public void the_grouping_is_independent_of_the_discovery_order()
    {
        var a = new PublishedIndex( Versions( "2026.11.20", "2026.11.21", "2026.11.21-alpha" ), Versions( "2026.11.20.1" ) );
        var b = new PublishedIndex( Versions( "2026.11.21-alpha", "2026.11.21", "2026.11.20" ), Versions( "2026.11.20.1" ) );

        b.ToJsonString().ShouldBe( a.ToJsonString() );
    }

    [Test]
    public void a_version_appears_once_across_both_sets()
    {
        Should.Throw<ArgumentException>( () => new PublishedIndex( Versions( "2026.11.20", "2026.11.20" ), [] ) )
              .Message.ShouldStartWith( "Version '2026.11.20' appears more than once: a profile version is unique." );

        Should.Throw<ArgumentException>( () => new PublishedIndex( Versions( "2026.11.20" ), Versions( "2026.11.20" ) ) )
              .Message.ShouldStartWith( "Version '2026.11.20' appears more than once: a profile version is either "
                                        + "alive or deprecated, not both." );
    }

    [Test]
    public void Create_splits_the_profiles_on_their_IsDeprecated()
    {
        var alive = TestModel.SampleProfile( "2026.11.21" );
        var deprecated = TestModel.SampleProfile( "2026.11.20.1" ).Deprecate();
        var prerelease = TestModel.SampleProfile( "2026.11.21-alpha" );

        var i = PublishedIndex.Create( new[] { deprecated, alive, prerelease } );

        Text( i.GetAlive( "(stable)" ) ).ShouldBe( new[] { "2026.11.21" } );
        Text( i.GetAlive( "alpha" ) ).ShouldBe( new[] { "2026.11.21-alpha" } );
        Text( i.GetDeprecated( "(stable)" ) ).ShouldBe( new[] { "2026.11.20.1" } );
    }

    #region Json

    [Test]
    public void index_json_is_the_stored_format()
    {
        var i = new PublishedIndex( Versions( "2026.11.21", "2026.11.20", "2026.11.22--ci.0", "2026.11.21.1--ci.3",
                                              "2026.11.21.1-alpha.0.ci.7", "2026.11.21-0.some-explo", "2026.11.21-zulu.0.ci.12" ),
                                    Versions( "2026.11.20.1", "2026.11.21-alpha" ) );

        i.ToJsonString().ShouldBe( """
            {
              "Alive": {
                "(stable)": [
                  "2026.11.21",
                  "2026.11.20"
                ],
                "(stable-ci)": [
                  "2026.11.22--ci.0",
                  "2026.11.21.1--ci.3"
                ],
                "alpha-ci": [
                  "2026.11.21.1-alpha.0.ci.7"
                ],
                "explo/some-explo": [
                  "2026.11.21-0.some-explo"
                ],
                "zulu-ci": [
                  "2026.11.21-zulu.0.ci.12"
                ]
              },
              "Deprecated": {
                "(stable)": [
                  "2026.11.20.1"
                ],
                "alpha": [
                  "2026.11.21-alpha"
                ]
              }
            }
            """.ReplaceLineEndings( "\r\n" ) );
    }

    [Test]
    public void the_empty_index_json_carries_the_two_empty_stable_groups()
    {
        PublishedIndex.Empty.ToJsonString().ShouldBe( """
            {
              "Alive": {
                "(stable)": []
              },
              "Deprecated": {
                "(stable)": []
              }
            }
            """.ReplaceLineEndings( "\r\n" ) );
    }

    [Test]
    public void index_compact_json_is_canonical()
    {
        var i = new PublishedIndex( Versions( "2026.11.20" ), Versions( "2026.11.20.1" ) );

        i.ToJsonString( indented: false )
         .ShouldBe( """{"Alive":{"(stable)":["2026.11.20"]},"Deprecated":{"(stable)":["2026.11.20.1"]}}""" );
    }

    [Test]
    public void index_round_trips()
    {
        var i = new PublishedIndex( Versions( "2026.11.21", "2026.11.20", "2026.11.22--ci.0", "2026.11.21.1-alpha.0.ci.7",
                                              "2026.11.21-0.some-explo" ),
                                    Versions( "2026.11.20.1", "2026.11.21-alpha" ) );

        var back = PublishedIndex.Parse( i.ToUtf8Bytes() );

        back.ToJsonString().ShouldBe( i.ToJsonString() );
        // Re-writing what was read is idempotent: the constructor regroups and reorders, so a second
        // pass cannot change anything.
        PublishedIndex.Parse( back.ToUtf8Bytes() ).ToJsonString().ShouldBe( i.ToJsonString() );
    }

    [Test]
    public void a_utf8_BOM_is_skipped()
    {
        var i = new PublishedIndex( Versions( "2026.11.20" ), [] );
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat( i.ToUtf8Bytes() ).ToArray();

        PublishedIndex.Parse( withBom ).ToJsonString().ShouldBe( i.ToJsonString() );
    }

    [Test]
    public void absent_sets_are_empty_and_unknown_properties_are_skipped()
    {
        // Reading is forgiving about what it doesn't understand, so the format can grow.
        var json = """
            {
                "Unknown": { "whatever": [ 1, 2 ] },
                "Alive": { "alpha": [ "2026.11.21-alpha" ] }
            }
            """;
        var i = PublishedIndex.Parse( Encoding.UTF8.GetBytes( json ) );

        Text( i.GetAlive( "alpha" ) ).ShouldBe( new[] { "2026.11.21-alpha" } );
        // An absent set is an empty one, and the constructor still seeds its stable group.
        i.Deprecated.Keys.ShouldBe( new[] { PublishedIndex.StableGroupName } );
        i.GetDeprecated( PublishedIndex.StableGroupName ).ShouldBeEmpty();

        // Both sets absent is the Empty singleton.
        PublishedIndex.Parse( Encoding.UTF8.GetBytes( "{}" ) ).ShouldBeSameAs( PublishedIndex.Empty );
    }

    [Test]
    public void a_version_listed_in_a_group_it_doesnt_belong_to_is_refused()
    {
        // The group name is a pure function of the version, so it is checked rather than trusted: an
        // index that disagrees with itself is not silently normalized.
        var json = """{ "Alive": { "alpha": [ "2026.11.20" ] } }""";

        Should.Throw<JsonException>( () => PublishedIndex.Parse( Encoding.UTF8.GetBytes( json ) ) )
              .Message.ShouldBe( "Version '2026.11.20' of the 'Alive' set is listed in the 'alpha' group "
                                 + "but belongs to '(stable)'." );
    }

    [TestCase( """{ "Alive": [ "2026.11.20" ] }""",
               "Expected 'Alive' object start, got 'StartArray'." )]
    [TestCase( """{ "Alive": { "(stable)": "2026.11.20" } }""",
               "Expected an array for '(stable)', got 'String'." )]
    [TestCase( """{ "Alive": { "(stable)": [ 3712 ] } }""",
               "Expected a string for '(stable)', got 'Number'." )]
    [TestCase( """{ "Alive": { "(stable)": [ "not-a-version" ] } }""",
               "Expected a TimeVersion for '(stable)', got 'not-a-version'." )]
    [TestCase( """{ "Alive": { "(stable)": [ "1.0.0-not-a-conformant-prerelease" ] } }""",
               "Expected a TimeVersion for '(stable)', got '1.0.0-not-a-conformant-prerelease'." )]
    public void a_malformed_index_throws_a_JsonException_naming_the_property( string json, string message )
    {
        Should.Throw<JsonException>( () => PublishedIndex.Parse( Encoding.UTF8.GetBytes( json ) ) )
              .Message.ShouldBe( message );
    }

    [Test]
    public void a_trailing_token_after_the_index_is_refused()
    {
        var json = """{ "Alive": { "(stable)": [] } } 3712""";

        // The message is not asserted: Utf8JsonReader enforces the single top level value itself, so
        // it is the one that throws here and Parse's own guard on a trailing token never gets to speak.
        // Parse keeps that guard to mirror PublishedProfile.Parse.
        Should.Throw<JsonException>( () => PublishedIndex.Parse( Encoding.UTF8.GetBytes( json ) ) );
    }

    #endregion

    static ImmutableArray<TimeVersion> Versions( params string[] versions ) => [.. versions.Select( TestModel.TV )];

    static string[] Text( ImmutableArray<TimeVersion> versions ) => [.. versions.Select( v => v.ToString() )];
}
