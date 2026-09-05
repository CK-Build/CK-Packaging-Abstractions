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

        p.Packages.Count.ShouldBe( 3 );
        p.Packages["CK.One"].Version.ShouldBe( TestModel.V( "1.2.3" ) );
        // PackageId is case insensitive.
        p.Packages["ck.two"].ShouldBe( TestModel.Package( "CK.Two@1.2.3" ) );
        p.Packages.ContainsKey( "CK.Unknown" ).ShouldBeFalse();
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
        d.Packages.ShouldBeSameAs( p.Packages );

        d.Deprecate().ShouldBeSameAs( d );
    }

    [Test]
    public void OnDeprecatedPackage_requires_the_package_and_its_exact_version()
    {
        var p = TestModel.SampleProfile();

        p.OnDeprecatedPackage( "CK.Unknown", TestModel.V( "1.2.3" ) ).ShouldBeSameAs( p );
        p.OnDeprecatedPackage( "CK.Two", TestModel.V( "1.2.4" ) ).ShouldBeSameAs( p, "Not the published version." );

        var d = p.OnDeprecatedPackage( "ck.TWO", TestModel.V( "1.2.3" ) );
        d.ShouldNotBeSameAs( p );
        d.IsDeprecated.ShouldBeTrue();

        d.OnDeprecatedPackage( "CK.One", TestModel.V( "1.2.3" ) )
         .ShouldBeSameAs( d, "Already deprecated." );
    }

    [Test]
    public void profile_compact_json_is_canonical()
    {
        var p = TestModel.SampleProfile();

        p.ToJsonString( indented: false ).ShouldBe( """
            {"StackUrl":"https://github.com/Signature-Code/CKt-Stack","World":"CKt","Version":"1.2.3","IsDeprecated":false,"Repositories":[{"Url":"https://github.com/Signature-Code/CKt-One","Id":"AQAAAAAAAAA","Packages":["CK.One@1.2.3","CK.One.Sub@1.2.3"]},{"Url":"https://github.com/Signature-Code/CKt-Two","Id":"AgAAAAAAAAA","Packages":["CK.Two@1.2.3"]}]}
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
        back.Packages.Count.ShouldBe( 3 );
        back.Repositories[0].Key.ShouldBe( p.Repositories[0].Key );

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
                ]
            }
            """;
        var p = PublishedProfile.Parse( Encoding.UTF8.GetBytes( json ) );

        p.Version.ShouldBe( TestModel.V( "1.2.3" ) );
        // IsDeprecated defaults to false when the property is missing.
        p.IsDeprecated.ShouldBeFalse();
        p.Packages.Keys.ShouldBe( new[] { "CK.One" } );
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
        p.Packages.Count.ShouldBe( 3 );
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
