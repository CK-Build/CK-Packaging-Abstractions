using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CK.Packaging.Abstractions.Tests;

[TestFixture]
public class ProfileTests
{
    [Test]
    public void profile_indexes_the_packages_of_its_repositories()
    {
        var p = TestModel.SampleProfile();

        p.Version.ShouldBe( TestModel.V( "1.2.3" ) );
        p.World.ShouldBe( TestModel.World );
        p.StackUrl.ShouldBe( TestModel.StackUrl );
        p.IsDeprecated.ShouldBeFalse();
        p.ToString().ShouldBe( "CKt/v1.2.3" );

        p.ProducedPackages.Count.ShouldBe( 3 );
        p.ProducedPackages["CK.One"].Version.ShouldBe( TestModel.V( "1.2.3" ) );
        // PackageId is case insensitive.
        p.ProducedPackages["ck.two"].ShouldBe( TestModel.Package( "CK.Two@1.2.3" ) );
        p.ProducedPackages.ContainsKey( "CK.Unknown" ).ShouldBeFalse();
    }

    [Test]
    public void repositories_are_sorted_by_url_and_packages_by_identifier()
    {
        // SampleProfile declares "Two" before "One" and "CK.One.Sub" before "CK.One".
        var p = TestModel.SampleProfile();

        p.Repositories.Select( r => r.Key.Url.AbsoluteUri )
                      .ShouldBe( new[] { "https://github.com/Signature-Code/CKt-One",
                                         "https://github.com/Signature-Code/CKt-Two" } );
        p.Repositories[0].Packages.Select( x => x.PackageId )
                                  .ShouldBe( new[] { "CK.One", "CK.One.Sub" } );
    }

    [Test]
    public void profile_version_must_be_a_conformant_SVersion()
    {
        var notCS = SVersion.Parse( "1.0.0-not-a-conformant-prerelease" );
        notCS.VersionKind.ShouldBe( CSVersionKind.None );

        Should.Throw<ArgumentException>( () => new PublishedProfile( TestModel.StackUrl, TestModel.World, notCS, [] ) )
              .Message.ShouldStartWith( "Version '1.0.0-not-a-conformant-prerelease' must be a Conformant SVersion." );
    }

    [Test]
    public void profile_stack_url_must_be_absolute()
    {
        var relative = new Uri( "CKt-Stack", UriKind.Relative );

        Should.Throw<ArgumentException>( () => new PublishedProfile( relative,
                                                                     TestModel.World,
                                                                     TestModel.V( "1.0.0" ),
                                                                     [] ) )
              .Message.ShouldStartWith( "Stack url must be absolute: 'CKt-Stack'." );
    }

    [Test]
    public void repositories_must_be_initialized()
    {
        Should.Throw<ArgumentException>( () => new PublishedProfile( TestModel.StackUrl,
                                                                     TestModel.World,
                                                                     TestModel.V( "1.0.0" ),
                                                                     default ) )
              .Message.ShouldStartWith( "Repositories must be initialized." );
    }

    [Test]
    public void a_package_identifier_cannot_appear_in_two_repositories()
    {
        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Repo( "One", 1, "CK.Shared@1.0.0" ),
                                                                  TestModel.Repo( "Two", 2, "CK.Shared@2.0.0" ) ) )
              .Message.ShouldStartWith( "Package 'CK.Shared' appears more than once in the profile: "
                                        + "'1.0.0' and '2.0.0'." );
    }

    [Test]
    public void a_package_identifier_cannot_appear_twice_in_a_repository()
    {
        Should.Throw<ArgumentException>( () => TestModel.Repo( "One", 1, "CK.Shared@1.0.0", "ck.SHARED@2.0.0" ) )
              .Message.ShouldStartWith( "Repository 'https://github.com/Signature-Code/CKt-One' contains 'ck.SHARED' "
                                        + "more than once: '1.0.0' and '2.0.0'." );
    }

    [Test]
    public void two_repositories_cannot_share_their_url()
    {
        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Repo( "One", 1, "CK.One@1.0.0" ),
                                                                  TestModel.Repo( "One", 2, "CK.Two@1.0.0" ) ) )
              .Message.ShouldStartWith( "Duplicate repository url 'https://github.com/Signature-Code/CKt-One'." );
    }

    [Test]
    public void two_repositories_cannot_share_their_identifier()
    {
        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Repo( "One", 1, "CK.One@1.0.0" ),
                                                                  TestModel.Repo( "Two", 1, "CK.Two@1.0.0" ) ) )
              .Message.ShouldStartWith( "Duplicate repository identifier 'AQAAAAAAAAA' "
                                        + "('https://github.com/Signature-Code/CKt-Two')." );
    }

    [Test]
    public void a_repository_key_must_be_valid()
    {
        Should.Throw<ArgumentException>( () => new Repository( new RepositoryKey( TestModel.RepoUrl( "One" ), default ),
                                                               [] ) )
              .Message.ShouldStartWith( "Invalid repository key "
                                        + "'https://github.com/Signature-Code/CKt-One (AAAAAAAAAAA)'." );

        Should.Throw<ArgumentException>( () => new Repository( new RepositoryKey( new Uri( "CKt-One", UriKind.Relative ),
                                                                                  new RandomId( 1 ) ),
                                                               [] ) );
    }

    [Test]
    public void Deprecate_is_idempotent_and_preserves_the_content()
    {
        var p = TestModel.SampleProfile();
        var d = p.Deprecate();

        d.ShouldNotBeSameAs( p );
        d.IsDeprecated.ShouldBeTrue();
        p.IsDeprecated.ShouldBeFalse( "The profile is immutable." );
        d.Version.ShouldBe( p.Version );
        d.World.ShouldBe( p.World );
        d.StackUrl.ShouldBe( p.StackUrl );
        d.Repositories.ShouldBe( p.Repositories );
        d.ProducedPackages.ShouldBeSameAs( p.ProducedPackages );
        d.DirectDependencies.ShouldBe( p.DirectDependencies );
        d.TransitiveDependencies.ShouldBeSameAs( p.TransitiveDependencies );

        d.Deprecate().ShouldBeSameAs( d );
    }

    [Test]
    public void OnDeprecatedPackage_requires_the_package_and_its_exact_version()
    {
        var p = TestModel.SampleProfile();

        p.OnDeprecatedPackage( "CK.Unknown", TestModel.V( "1.2.3" ) ).ShouldBeSameAs( p );
        p.OnDeprecatedPackage( "CK.Two", TestModel.V( "1.2.4" ) ).ShouldBeSameAs( p, "Not the published version." );
        p.OnDeprecatedPackage( "NUnit", TestModel.V( "4.2.2" ) )
         .ShouldBeSameAs( p, "A direct dependency is not a produced package." );

        var d = p.OnDeprecatedPackage( "ck.TWO", TestModel.V( "1.2.3" ) );
        d.ShouldNotBeSameAs( p );
        d.IsDeprecated.ShouldBeTrue();

        d.OnDeprecatedPackage( "CK.One", TestModel.V( "1.2.3" ) )
         .ShouldBeSameAs( d, "Already deprecated." );
    }

    #region DirectDependencies and TransitiveDependencies

    [Test]
    public void a_profile_carries_no_dependency_by_default()
    {
        var p = TestModel.Profile( "1.0.0", TestModel.Repo( "One", 1, "CK.One@1.0.0" ) );

        p.DirectDependencies.ShouldBeEmpty();
        p.TransitiveDependencies.ShouldBeSameAs( TransitiveDependencies.Empty );
        p.TransitiveDependencies.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void dependencies_are_sorted_and_the_ambiguities_carry_their_anchor()
    {
        // SampleDirectDependencies declares "System.Text.Json" before "NUnit".
        var p = TestModel.SampleProfile();

        p.DirectDependencies.Select( x => x.ToString() )
                            .ShouldBe( new[] { "NUnit@4.2.2", "System.Text.Json@9.0.0" } );

        var t = p.TransitiveDependencies;
        t.Regular.Select( x => x.ToString() ).ShouldBe( new[] { "System.IO.Pipelines@9.0.0" } );
        t.IsEmpty.ShouldBeFalse();
        t.ToString().ShouldBe( "1 regular, 3 ambiguous" );

        // Ambiguities are sorted by package identifier, whatever their anchor.
        t.Ambiguous.Select( x => $"{x} ({x.ResolvedFrom})" )
                   .ShouldBe( new[] { "CK.Two@1.2.3 (ProducedPackages)",
                                      "System.Text.Encodings.Web@9.0.0 (TransitiveDependencies)",
                                      "System.Text.Json@9.0.0 (DirectDependencies)" } );

        // The anchors are the profile's own entries.
        p.ProducedPackages["CK.Two"].Version.ShouldBe( t.Ambiguous[0].Version );
        p.DirectDependencies.ShouldContain( x => x.Equals( t.Ambiguous[2] ) );
    }

    [Test]
    public void resolutions_are_sorted_by_descending_version_and_their_content_is_canonical()
    {
        var a = TestModel.SampleProfile().TransitiveDependencies.Ambiguous[1];

        a.PackageId.ShouldBe( "System.Text.Encodings.Web" );
        a.ResolvedFrom.ShouldBe( VersionSource.TransitiveDependencies );
        // SampleTransitiveDependencies declares 8.0.0 before 9.0.0.
        a.Resolutions.Select( r => r.Version.ToString() ).ShouldBe( new[] { "9.0.0", "8.0.0" } );
        a.Version.ShouldBe( a.Resolutions[0].Version, "Highest-wins." );
        // ...and repository 2 before repository 1.
        a.Resolutions[0].Repositories.Select( x => x.ToString() )
                                     .ShouldBe( new[] { "AQAAAAAAAAA", "AgAAAAAAAAA" } );
        // One repository can resolve one identifier to two versions: two of its target frameworks
        // resolved differently. Repository 1 is in both resolutions.
        a.Resolutions[1].Repositories.Select( x => x.ToString() ).ShouldBe( new[] { "AQAAAAAAAAA" } );
    }

    [Test]
    public void a_direct_dependency_identifier_cannot_appear_twice()
    {
        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Packages( "CK.Ext@1.0.0", "ck.EXT@2.0.0" ),
                                                                  null ) )
              .Message.ShouldStartWith( "Direct dependency 'ck.EXT' appears more than once in the profile: "
                                        + "'1.0.0' and '2.0.0'." );
    }

    [Test]
    public void a_direct_dependency_cannot_be_produced_by_the_profile()
    {
        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Packages( "CK.One@0.9.0" ),
                                                                  null,
                                                                  TestModel.Repo( "One", 1, "CK.One@1.0.0" ) ) )
              .Message.ShouldStartWith( "Direct dependency 'CK.One@0.9.0' is produced by this profile "
                                        + "('CK.One@1.0.0'): the direct dependencies are the consumed packages "
                                        + "minus the produced ones." );
    }

    [Test]
    public void a_regular_transitive_dependency_cannot_be_produced_nor_direct()
    {
        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  [],
                                                                  new TransitiveDependencies( TestModel.Packages( "CK.One@0.9.0" ), [] ),
                                                                  TestModel.Repo( "One", 1, "CK.One@1.0.0" ) ) )
              .Message.ShouldStartWith( "Regular transitive dependency 'CK.One@0.9.0' is produced by this profile "
                                        + "('CK.One@1.0.0'): a produced identifier can only appear in the closure "
                                        + "as an ambiguity anchored on ProducedPackages." );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Packages( "NUnit@4.2.2" ),
                                                                  new TransitiveDependencies( TestModel.Packages( "nunit@4.0.0" ), [] ) ) )
              .Message.ShouldStartWith( "Regular transitive dependency 'nunit@4.0.0' is a direct dependency "
                                        + "('NUnit@4.2.2'): a direct identifier can only appear in the closure "
                                        + "as an ambiguity anchored on DirectDependencies." );
    }

    [Test]
    public void a_transitive_dependency_cannot_be_both_regular_and_ambiguous()
    {
        var a = TestModel.Ambiguous( "A@2.0.0",
                                     VersionSource.TransitiveDependencies,
                                     TestModel.Res( "2.0.0", 1 ),
                                     TestModel.Res( "1.0.0", 2 ) );

        Should.Throw<ArgumentException>( () => new TransitiveDependencies( TestModel.Packages( "a@2.0.0" ), [a] ) )
              .Message.ShouldStartWith( "Transitive dependency 'A' is both regular and ambiguous: one identifier "
                                        + "resolves to one version, ambiguous or not." );
    }

    [Test]
    public void a_transitive_dependency_identifier_appears_once_in_each_list()
    {
        Should.Throw<ArgumentException>( () => new TransitiveDependencies( TestModel.Packages( "A@1.0.0", "a@2.0.0" ),
                                                                           [] ) )
              .Message.ShouldStartWith( "Regular transitive dependency 'a' appears more than once: "
                                        + "'1.0.0' and '2.0.0'." );
    }

    [Test]
    public void the_two_lists_must_be_initialized()
    {
        Should.Throw<ArgumentException>( () => new TransitiveDependencies( default, [] ) )
              .Message.ShouldStartWith( "Must be initialized." );
        Should.Throw<ArgumentException>( () => new TransitiveDependencies( [], default ) )
              .Message.ShouldStartWith( "Must be initialized." );
    }

    [Test]
    public void an_ambiguity_anchored_on_DirectDependencies_must_match_its_direct_dependency()
    {
        var a = TestModel.Ambiguous( "NUnit@4.0.0",
                                     VersionSource.DirectDependencies,
                                     TestModel.Res( "5.0.0", 1 ) );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  [],
                                                                  new TransitiveDependencies( [], [a] ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'NUnit@4.0.0' is resolved from DirectDependencies but "
                                        + "no entry of DirectDependencies has this identifier." );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Packages( "NUnit@4.2.2" ),
                                                                  new TransitiveDependencies( [], [a] ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'NUnit@4.0.0' is resolved from DirectDependencies but "
                                        + "the entry of DirectDependencies is 'NUnit@4.2.2'." );
    }

    [Test]
    public void an_ambiguity_anchored_on_ProducedPackages_must_match_its_produced_package()
    {
        var a = TestModel.Ambiguous( "CK.One@0.9.0",
                                     VersionSource.ProducedPackages,
                                     TestModel.Res( "5.0.0", 1 ) );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  [],
                                                                  new TransitiveDependencies( [], [a] ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'CK.One@0.9.0' is resolved from ProducedPackages but "
                                        + "no entry of ProducedPackages has this identifier." );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  [],
                                                                  new TransitiveDependencies( [], [a] ),
                                                                  TestModel.Repo( "One", 1, "CK.One@1.0.0" ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'CK.One@0.9.0' is resolved from ProducedPackages but "
                                        + "the entry of ProducedPackages is 'CK.One@1.0.0'." );
    }

    [Test]
    public void an_ambiguity_resolved_from_its_own_resolutions_must_have_no_anchor()
    {
        var a = TestModel.Ambiguous( "CK.One@2.0.0",
                                     VersionSource.TransitiveDependencies,
                                     TestModel.Res( "2.0.0", 1 ),
                                     TestModel.Res( "1.0.0", 1 ) );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Packages( "CK.One@2.0.0" ),
                                                                  new TransitiveDependencies( [], [a] ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'CK.One@2.0.0' is resolved from its own resolutions "
                                        + "but 'CK.One@2.0.0' is a direct dependency: it must be resolved from "
                                        + "DirectDependencies." );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "2.0.0",
                                                                  [],
                                                                  new TransitiveDependencies( [], [a] ),
                                                                  TestModel.Repo( "One", 1, "CK.One@2.0.0" ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'CK.One@2.0.0' is resolved from its own resolutions "
                                        + "but 'CK.One@2.0.0' is produced by this profile: it must be resolved "
                                        + "from ProducedPackages." );
    }

    [Test]
    public void a_resolution_can_only_name_a_repository_of_the_profile()
    {
        // The repository identifiers are the join key with PublishedProfile.Repositories: one that
        // names nothing cannot be resolved back by a consumer.
        var a = TestModel.Ambiguous( "NUnit@4.2.2",
                                     VersionSource.DirectDependencies,
                                     TestModel.Res( "5.0.0", 3712 ) );

        Should.Throw<ArgumentException>( () => TestModel.Profile( "1.0.0",
                                                                  TestModel.Packages( "NUnit@4.2.2" ),
                                                                  new TransitiveDependencies( [], [a] ),
                                                                  TestModel.Repo( "One", 1, "CK.One@1.0.0" ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'NUnit@4.2.2' is resolved by the repository "
                                        + "'gA4AAAAAAAA' which is not one of this profile's repositories." );
    }

    [Test]
    public void an_ambiguity_has_at_least_one_resolution()
    {
        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0", VersionSource.DirectDependencies ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'A@1.0.0' has no resolution: an ambiguity is a "
                                        + "disagreement, so at least one is required." );

        Should.Throw<ArgumentException>( () => new AmbiguousDependency( "A",
                                                                        TestModel.V( "1.0.0" ),
                                                                        VersionSource.DirectDependencies,
                                                                        default ) )
              .Message.ShouldStartWith( "Resolutions must be initialized." );
    }

    [Test]
    public void an_ambiguity_resolved_from_its_own_resolutions_takes_the_greatest_of_them()
    {
        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0",
                                                                    VersionSource.TransitiveDependencies,
                                                                    TestModel.Res( "1.0.0", 1 ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'A@1.0.0' is resolved from its own resolutions but "
                                        + "has only one ('1.0.0'): it is a regular transitive dependency." );

        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0",
                                                                    VersionSource.TransitiveDependencies,
                                                                    TestModel.Res( "1.0.0", 1 ),
                                                                    TestModel.Res( "2.0.0", 2 ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'A@1.0.0' is resolved from its own resolutions: its "
                                        + "version must be the greatest of them, which is '2.0.0'." );
    }

    [TestCase( VersionSource.DirectDependencies )]
    [TestCase( VersionSource.ProducedPackages )]
    public void an_anchored_ambiguity_only_reports_the_greater_resolutions( VersionSource resolvedFrom )
    {
        // A resolution equal to the anchor is not a disagreement, and a smaller one is invisible
        // to a restore: only the harmful direction is reported.
        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@2.0.0",
                                                                    resolvedFrom,
                                                                    TestModel.Res( "3.0.0", 1 ),
                                                                    TestModel.Res( "2.0.0", 2 ) ) )
              .Message.ShouldStartWith( $"Ambiguous dependency 'A@2.0.0' is anchored on {resolvedFrom}: its "
                                        + "resolutions must all be greater than '2.0.0', but '2.0.0' is not." );

        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@2.0.0",
                                                                    resolvedFrom,
                                                                    TestModel.Res( "1.0.0", 1 ) ) )
              .Message.ShouldStartWith( $"Ambiguous dependency 'A@2.0.0' is anchored on {resolvedFrom}: its "
                                        + "resolutions must all be greater than '2.0.0', but '1.0.0' is not." );
    }

    [Test]
    public void a_resolution_comes_from_at_least_one_repository()
    {
        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0",
                                                                    VersionSource.DirectDependencies,
                                                                    TestModel.Res( "2.0.0" ) ) )
              .Message.ShouldStartWith( "Resolution 'A@2.0.0' has no repository: a resolution comes from at "
                                        + "least one repository." );

        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0",
                                                                    VersionSource.DirectDependencies,
                                                                    TestModel.Res( "2.0.0", 0 ) ) )
              .Message.ShouldStartWith( "Resolution 'A@2.0.0' names an invalid repository identifier." );
    }

    [Test]
    public void a_version_cannot_be_resolved_twice_and_neither_can_a_repository_be_named_twice()
    {
        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0",
                                                                    VersionSource.DirectDependencies,
                                                                    TestModel.Res( "2.0.0", 1 ),
                                                                    TestModel.Res( "2.0.0", 2 ) ) )
              .Message.ShouldStartWith( "Ambiguous dependency 'A' resolves '2.0.0' more than once: such "
                                        + "resolutions must be merged." );

        Should.Throw<ArgumentException>( () => TestModel.Ambiguous( "A@1.0.0",
                                                                    VersionSource.DirectDependencies,
                                                                    TestModel.Res( "2.0.0", 1, 1 ) ) )
              .Message.ShouldStartWith( "Resolution 'A@2.0.0' names the repository 'AQAAAAAAAAA' more than once." );
    }

    [Test]
    public void a_dependency_version_is_any_SemVer()
    {
        // An external package is not bound to CSemVer, unlike the profile's own version.
        var p = TestModel.Profile( "1.0.0",
                                   TestModel.Packages( "Some.Lib@1.0.0-beta2" ),
                                   new TransitiveDependencies( TestModel.Packages( "Other.Lib@2.0.0-rc.2.23479.6" ),
                                                               [] ) );

        var back = PublishedProfile.Parse( p.ToUtf8Bytes() );
        back.DirectDependencies[0].Version.ToString().ShouldBe( "1.0.0-beta2" );
        back.TransitiveDependencies.Regular[0].Version.ToString().ShouldBe( "2.0.0-rc.2.23479.6" );
    }

    [Test]
    public void absent_dependency_properties_are_empty()
    {
        // A profile written before the dependencies existed still parses: there is no file format
        // version, and an empty set says nothing about the transitive packages anyway.
        var json = """
            {
                "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
                "World": "CKt",
                "Version": "1.2.3",
                "Repositories": []
            }
            """;
        var p = PublishedProfile.Parse( Encoding.UTF8.GetBytes( json ) );

        p.DirectDependencies.ShouldBeEmpty();
        p.TransitiveDependencies.ShouldBeSameAs( TransitiveDependencies.Empty );

        // Same for the 2 lists of an existing TransitiveDependencies object.
        var partial = """
            {
                "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
                "World": "CKt",
                "Version": "1.2.3",
                "Repositories": [],
                "DirectDependencies": [],
                "TransitiveDependencies": { "Regular": [ "Ghost.Package@0.1.0" ] }
            }
            """;
        var t = PublishedProfile.Parse( Encoding.UTF8.GetBytes( partial ) ).TransitiveDependencies;

        t.Regular.Select( x => x.ToString() ).ShouldBe( new[] { "Ghost.Package@0.1.0" } );
        t.Ambiguous.ShouldBeEmpty();
    }

    #endregion

    [Test]
    public void profile_compact_json_is_canonical()
    {
        var p = TestModel.SampleProfile();

        p.ToJsonString( indented: false ).ShouldBe( """
            {"StackUrl":"https://github.com/Signature-Code/CKt-Stack","World":"CKt","Version":"1.2.3","IsDeprecated":false,"Repositories":[{"Url":"https://github.com/Signature-Code/CKt-One","Id":"AQAAAAAAAAA","Packages":["CK.One@1.2.3","CK.One.Sub@1.2.3"]},{"Url":"https://github.com/Signature-Code/CKt-Two","Id":"AgAAAAAAAAA","Packages":["CK.Two@1.2.3"]}],"DirectDependencies":["NUnit@4.2.2","System.Text.Json@9.0.0"],"TransitiveDependencies":{"Regular":["System.IO.Pipelines@9.0.0"],"Ambiguous":[{"Package":"CK.Two@1.2.3","ResolvedFrom":"ProducedPackages","Resolutions":[{"Version":"99.0.0","Repositories":["AQAAAAAAAAA"]}]},{"Package":"System.Text.Encodings.Web@9.0.0","ResolvedFrom":"TransitiveDependencies","Resolutions":[{"Version":"9.0.0","Repositories":["AQAAAAAAAAA","AgAAAAAAAAA"]},{"Version":"8.0.0","Repositories":["AQAAAAAAAAA"]}]},{"Package":"System.Text.Json@9.0.0","ResolvedFrom":"DirectDependencies","Resolutions":[{"Version":"10.0.0","Repositories":["AgAAAAAAAAA"]}]}]}}
            """ );
    }

    [Test]
    public void profile_indented_json_uses_CRLF_on_any_platform()
    {
        var json = TestModel.SampleProfile().ToJsonString();

        json.Replace( "\r\n", "" ).ShouldNotContain( "\n" );
        json.ShouldStartWith( "{\r\n  \"StackUrl\": \"https://github.com/Signature-Code/CKt-Stack\",\r\n" );
    }

    [TestCase( "1.2.3" )]
    [TestCase( "1.2.3--ci.5" )]
    [TestCase( "1.3.0-alpha" )]
    [TestCase( "1.3.0-zulu.4.ci.12" )]
    [TestCase( "0.0.0-0.some-explo" )]
    public void json_round_trip( string version )
    {
        var p = TestModel.SampleProfile( version ).Deprecate();

        var back = PublishedProfile.Parse( p.ToUtf8Bytes() );
        back.ToJsonString().ShouldBe( p.ToJsonString() );
        back.Version.ShouldBe( p.Version );
        back.IsDeprecated.ShouldBeTrue();
        back.ProducedPackages.Count.ShouldBe( 3 );
        back.Repositories[0].Key.ShouldBe( p.Repositories[0].Key );
        back.DirectDependencies.ShouldBe( p.DirectDependencies );
        back.TransitiveDependencies.Regular.ShouldBe( p.TransitiveDependencies.Regular );
        back.TransitiveDependencies.Ambiguous.Select( a => a.ResolvedFrom )
                                             .ShouldBe( p.TransitiveDependencies.Ambiguous.Select( a => a.ResolvedFrom ) );
        back.TransitiveDependencies.Ambiguous[1].Resolutions
            .ShouldBe( p.TransitiveDependencies.Ambiguous[1].Resolutions );

        // The compact and the indented forms carry the same content.
        PublishedProfile.Parse( p.ToUtf8Bytes( indented: false ) ).ToJsonString().ShouldBe( p.ToJsonString() );
    }

    [Test]
    public void unknown_json_properties_are_skipped()
    {
        var json = """
            {
                "Unknown": { "Nested": [1,2,{"Deep":null}] },
                "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
                "World": "CKt",
                "Version": "1.2.3",
                "Repositories": [
                    {
                        "Url": "https://github.com/Signature-Code/CKt-One",
                        "Id": "AQAAAAAAAAA",
                        "AlsoUnknown": [],
                        "Packages": [ "CK.One@1.2.3" ]
                    }
                ],
                "TransitiveDependencies": {
                    "Unknown": 3712,
                    "Ambiguous": [
                        {
                            "Package": "A@1.0.0",
                            "ResolvedFrom": "TransitiveDependencies",
                            "Unknown": [],
                            "Resolutions": [
                                { "Version": "1.0.0", "Repositories": [ "AQAAAAAAAAA" ], "Unknown": {} },
                                { "Version": "0.5.0", "Repositories": [ "AQAAAAAAAAA" ] }
                            ]
                        }
                    ]
                }
            }
            """;
        var p = PublishedProfile.Parse( Encoding.UTF8.GetBytes( json ) );

        p.Version.ShouldBe( TestModel.V( "1.2.3" ) );
        // IsDeprecated defaults to false when the property is missing.
        p.IsDeprecated.ShouldBeFalse();
        p.ProducedPackages.Keys.ShouldBe( new[] { "CK.One" } );
        var a = p.TransitiveDependencies.Ambiguous[0];
        a.ToString().ShouldBe( "A@1.0.0" );
        a.Resolutions.Length.ShouldBe( 2 );
        a.Resolutions[0].Repositories.Select( x => x.ToString() ).ShouldBe( new[] { "AQAAAAAAAAA" } );
    }

    [TestCase( """{"World":"CKt","Version":"1.2.3","Repositories":[]}""",
               "Missing 'StackUrl' property in 'PublishedProfile'." )]
    [TestCase( """{"StackUrl":"https://x/y","Version":"1.2.3","Repositories":[]}""",
               "Missing 'World' property in 'PublishedProfile'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Repositories":[]}""",
               "Missing 'Version' property in 'PublishedProfile'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3"}""",
               "Missing 'Repositories' property in 'PublishedProfile'." )]
    [TestCase( """{"StackUrl":"not an url","World":"CKt","Version":"1.2.3","Repositories":[]}""",
               "Expected an absolute url for 'StackUrl', got 'not an url'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"no way!","Version":"1.2.3","Repositories":[]}""",
               "Expected a world full name for 'World', got 'no way!'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.0.0-nope","Repositories":[]}""",
               "Expected a Conformant SVersion for 'Version', got '1.0.0-nope'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","IsDeprecated":"yes","Repositories":[]}""",
               "Expected a boolean for 'IsDeprecated', got 'String'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":{}}""",
               "Expected an array for 'Repositories', got 'StartObject'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[3]}""",
               "Expected 'Repository' object start, got 'Number'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[{"Id":"AQAAAAAAAAA","Packages":[]}]}""",
               "Missing 'Url' property in 'Repository'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[{"Url":"https://x/z","Packages":[]}]}""",
               "Missing 'Id' property in 'Repository'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[{"Url":"https://x/z","Id":"AAAAAAAAAAA","Packages":[]}]}""",
               "Expected a valid RandomId for 'Id', got 'AAAAAAAAAAA'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[{"Url":"https://x/z","Id":"AQAAAAAAAAA"}]}""",
               "Missing 'Packages' property in 'Repository'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[{"Url":"https://x/z","Id":"AQAAAAAAAAA","Packages":["CK.One"]}]}""",
               "Expected a \"packageId@version\" string in 'Packages', got 'CK.One'." )]
    [TestCase( """[]""", "Expected 'PublishedProfile' object start, got 'StartArray'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"DirectDependencies":{}}""",
               "Expected an array for 'DirectDependencies', got 'StartObject'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"DirectDependencies":["CK.One"]}""",
               "Expected a \"packageId@version\" string in 'DirectDependencies', got 'CK.One'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":[]}""",
               "Expected 'TransitiveDependencies' object start, got 'StartArray'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Regular":["CK.One"]}}""",
               "Expected a \"packageId@version\" string in 'Regular', got 'CK.One'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"ResolvedFrom":"TransitiveDependencies","Resolutions":[]}]}}""",
               "Missing 'Package' property in 'AmbiguousDependency'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","Resolutions":[]}]}}""",
               "Missing 'ResolvedFrom' property in 'AmbiguousDependency'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"Nope","Resolutions":[]}]}}""",
               "Expected \"TransitiveDependencies\", \"DirectDependencies\" or \"ProducedPackages\" for 'ResolvedFrom', got 'Nope'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"TransitiveDependencies"}]}}""",
               "Missing 'Resolutions' property in 'AmbiguousDependency'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"TransitiveDependencies","Resolutions":[{"Repositories":["AQAAAAAAAAA"]}]}]}}""",
               "Missing 'Version' property in 'VersionResolution'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"TransitiveDependencies","Resolutions":[{"Version":"1.0.0"}]}]}}""",
               "Missing 'Repositories' property in 'VersionResolution'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"TransitiveDependencies","Resolutions":[{"Version":"nope","Repositories":["AQAAAAAAAAA"]}]}]}}""",
               "Expected a SemVer version for 'Version', got 'nope'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"TransitiveDependencies","Resolutions":[{"Version":"1.0.0","Repositories":[3]}]}]}}""",
               "Expected a string for 'Repositories', got 'Number'." )]
    [TestCase( """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[],"TransitiveDependencies":{"Ambiguous":[{"Package":"A@1.0.0","ResolvedFrom":"TransitiveDependencies","Resolutions":[{"Version":"1.0.0","Repositories":["nope"]}]}]}}""",
               "Expected a valid RandomId for 'Repositories', got 'nope'." )]
    public void json_errors_are_explicit( string json, string message )
    {
        Should.Throw<JsonException>( () => PublishedProfile.Parse( Encoding.UTF8.GetBytes( json ) ) )
              .Message.ShouldBe( message );
    }

    [Test]
    public void trailing_data_is_a_json_error()
    {
        var json = """{"StackUrl":"https://x/y","World":"CKt","Version":"1.2.3","Repositories":[]} 3""";

        Should.Throw<JsonException>( () => PublishedProfile.Parse( Encoding.UTF8.GetBytes( json ) ) );
    }

    [Test]
    public void an_incoherent_json_profile_is_an_ArgumentException()
    {
        var json = """
            {
                "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
                "World": "CKt",
                "Version": "1.2.3",
                "Repositories": [
                    { "Url": "https://x/One", "Id": "AQAAAAAAAAA", "Packages": [ "CK.One@1.2.3" ] },
                    { "Url": "https://x/Two", "Id": "AgAAAAAAAAA", "Packages": [ "CK.One@1.2.4" ] }
                ]
            }
            """;
        Should.Throw<ArgumentException>( () => PublishedProfile.Parse( Encoding.UTF8.GetBytes( json ) ) )
              .Message.ShouldStartWith( "Package 'CK.One' appears more than once in the profile: "
                                        + "'1.2.3' and '1.2.4'." );

        // The dependencies are checked the same way.
        var withDependencies = """
            {
                "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
                "World": "CKt",
                "Version": "1.2.3",
                "Repositories": [
                    { "Url": "https://x/One", "Id": "AQAAAAAAAAA", "Packages": [ "CK.One@1.2.3" ] }
                ],
                "DirectDependencies": [ "CK.One@1.2.3" ]
            }
            """;
        Should.Throw<ArgumentException>( () => PublishedProfile.Parse( Encoding.UTF8.GetBytes( withDependencies ) ) )
              .Message.ShouldStartWith( "Direct dependency 'CK.One@1.2.3' is produced by this profile" );
    }

    [Test]
    public void Read_leaves_the_reader_on_the_EndObject_token()
    {
        // A profile can be a property of a bigger document: Read must not consume more
        // than the profile's own object.
        var profileJson = TestModel.SampleProfile().ToJsonString( indented: false );
        var json = $$"""{"Before":3712,"Profile":{{profileJson}},"After":"done"}""";

        var r = new Utf8JsonReader( Encoding.UTF8.GetBytes( json ) );
        r.Read();
        r.TokenType.ShouldBe( JsonTokenType.StartObject );

        r.Read();
        r.GetString().ShouldBe( "Before" );
        r.Read();
        r.GetInt32().ShouldBe( 3712 );

        r.Read();
        r.GetString().ShouldBe( "Profile" );
        r.Read();
        var p = PublishedProfile.Read( ref r );
        p.ProducedPackages.Count.ShouldBe( 3 );
        r.TokenType.ShouldBe( JsonTokenType.EndObject );

        r.Read();
        r.GetString().ShouldBe( "After" );
        r.Read();
        r.GetString().ShouldBe( "done" );
        r.Read();
        r.TokenType.ShouldBe( JsonTokenType.EndObject );
        r.Read().ShouldBeFalse();
    }
}
