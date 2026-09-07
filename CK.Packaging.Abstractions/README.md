# CK.Packaging.Abstractions

This package defines what a CKli World *published*: the immutable, serializable description of a
coherent set of package instances. It is a contract, not a tool. There is no I/O here, no monitor,
no process: only value types and their Json representation, so that anything - CKli itself, its
plugins, a build server, a dashboard - can agree on what a publication is.

Its only dependency is [CK.SVersion](https://github.com/CK-Build/CK-SVersion/blob/stable/CK.SVersion/README.md),
which brings `SVersion` and `PackageInstance`. That is deliberate: like CK.SVersion, this package
stays free of `CK.Core`, `IActivityMonitor` and everything else, so that depending on it commits a
consumer to nothing.

## PublishedProfile

A [`PublishedProfile`](PublishedProfile.cs) is the set of packages a World has published under one
version. It is immutable and always coherent: **the constructor is the guard**, and a profile that
exists is a profile that made sense.

```csharp
var profile = new PublishedProfile( stackUrl, world, SVersion.Parse( "1.2.3" ), repositories );
```

What the constructor enforces:

- `StackUrl` is an absolute url (the "-Stack" repository the World belongs to).
- `Version` is a *Conformant SVersion* - `SVersion.VersionKind` is never `CSVersionKind.None`. A
  profile is produced by a build, and a build produces a version one can reason about.
- No package identifier appears in more than one `Repository`, and none appears twice in the same
  one. A profile carries **at most one version per package identifier**; this is the whole point of
  calling it coherent, and violating it throws rather than picking a winner.
- No two repositories share a `RepositoryKey.Url` or a `RepositoryKey.Id`.
- The optional dependency sets agree with the produced packages - see below.

> The coherency is a shallow one: the packages of a profile agree with each other, but their deeper,
transitive dependencies are not guaranteed to be aligned.

`ProducedPackages` indexes every package instance the `Repositories` produced by its
`PackageInstance.PackageId`, case insensitively - matching `PackageInstance`'s own equality.
`Repositories` and each repository's `Packages` are **sorted** (by url, then by package identifier):
a profile built from the same content in a different discovery order is byte-identical once
serialized.

### DirectDependencies and TransitiveDependencies

A profile can also describe what its repositories *consume*. Both sets are optional and default to
empty - and an empty `TransitiveDependencies` says nothing about the closure, not that there is none.

- `DirectDependencies` are the packages consumed by at least one `Repository`, minus the produced
  identifiers. Like the produced packages, one version per identifier - and none of them can be a
  produced one, since the two sets are complementary by definition.
- `TransitiveDependencies` is the closure of those direct dependencies' own dependencies, in three
  lists:
  - `Regular` - the ones that resolve to a single version.
  - `Ambiguous` - the ones whose required versions disagree (below).
  - `Missing` - the required instances whose nuspec could not be read, which is what makes
    `IsComplete` false. This is a marker rather than a fourth list of its own packages: such an
    instance also appears in `Regular` or `Ambiguous` unless it is a direct dependency.

`Regular` and `Ambiguous` partition the closure by package identifier - the two never share one, and
neither can name a direct or produced identifier as a regular entry - so a consumer that ignores the
details reads their union as a flat, coherent set of resolved instances.

An [`AmbiguousDependency`](AmbiguousDependency.cs) **is** a `PackageInstance`: its inherited `Version`
is what this profile resolves the identifier to, and its `ResolvedFrom` says where that version comes
from. Each [`VersionSource`](VersionSource.cs) value names the profile property that holds it:

| `ResolvedFrom` | The resolved version is | The `Requirements` are |
|---|---|---|
| `TransitiveDependencies` | the greatest requirement (NuGet's highest-wins) | all of them, at least 2, disagreeing among themselves |
| `DirectDependencies` | the direct dependency's version, which NuGet's nearest-wins makes authoritative | the ones asking for **more** |
| `ProducedPackages` | the version this profile produces | the ones asking for **more** |

Only the harmful direction is reported. A transitive requirement *below* what the profile references
or produces is invisible to a restore, and a requirement equal to the anchor is not a disagreement at
all - so an anchored identifier with nothing greater to report is simply not in the list, since
`DirectDependencies` or `ProducedPackages` already states its version.

A `VersionRequirement` is one required version, the closure members that require it (`RequiredBy`) and
the target frameworks under which they do (`TargetFrameworks` - the union across the `RequiredBy`,
where the empty string stands for "any framework"). Requirements are sorted by descending version, and
so is everything inside them: canonical form again. Its equality is **structural**, unlike the one a
`record struct` would synthesize (`ImmutableArray`'s own equality compares the underlying array by
reference).

> The model is **TFM-blind**: the closure is one flat set, not one per target framework. The
frameworks are recorded on every requirement, but nothing here filters by them - for a given target
NuGet picks the single best-matching dependency group of each package, so a per-target closure is not
a subset of this one. Computing those, if the need arises, is CKli's business, from the NuGet cache.

### Deprecation

Deprecation is the only state a published profile can still change, and it changes by producing a
new profile rather than by mutating:

- `Deprecate()` returns a deprecated profile, or `this` when it already is.
- `OnDeprecatedPackage( packageId, version )` deprecates the profile when it *produces* exactly that
  package at that version, and returns `this` otherwise. A dependency is not a produced package: only
  `ProducedPackages` is consulted.

Both are updaters: the caller keeps whichever instance it wants.

## Repository, RepositoryKey and RandomId

A [`Repository`](Repository.cs) is a [`RepositoryKey`](RepositoryKey.cs) plus the
`PackageInstance` it produced. The key pairs a `Url` (where the repository is) with a
[`RandomId`](RandomId.cs) (what the repository *is*): a repository that moves keeps its identity,
which is why the identifier and the locator are two separate things.

A `RandomId` is an unsigned long rendered as 11 Base64Url characters. `default` is the invalid
identifier - `Value` is 0 and `ToString()` is `"AAAAAAAAAAA"`, which parses back to itself.
`CreateRandom()` never produces it. `TryMatch` forwards its head on success (for parsing an
identifier out of a larger string); `TryParse` requires the whole string to be the identifier.

## WorldName

A [`WorldName`](WorldName.cs) is a stack name alone (`CKli`) for the default world, or a stack name
followed by a Long Term Support suffix (`CKli@v1.0`) for an LTS world. `LTSName` always carries its
leading `@` and is normalized to null for the default world; `EnsureLTSPrefix` is the helper that
prefixes a branch or folder name with it.

`IsValidRepositoryName` and `IsValidLTSName` validate the two halves and match the **whole** name -
`"no way!"` is not a valid repository name. Equality and hashing use the case insensitive `FullName`.

## Json serialization

Serialization uses the **basic** `Utf8JsonWriter` and `Utf8JsonReader`, not `JsonSerializer`: no
reflection, no converters, no source generator, and the format is exactly what
[`PublishedProfile.Json.cs`](PublishedProfile.Json.cs) writes.

```json
{
  "StackUrl": "https://github.com/Signature-Code/CKt-Stack",
  "World": "CKt",
  "Version": "1.2.3",
  "IsDeprecated": false,
  "Repositories": [
    {
      "Url": "https://github.com/Signature-Code/CKt-One",
      "Id": "AQAAAAAAAAA",
      "Packages": [
        "CK.One@1.2.3",
        "CK.One.Sub@1.2.3"
      ]
    }
  ],
  "DirectDependencies": [
    "NUnit@4.2.2",
    "System.Text.Json@9.0.0"
  ],
  "TransitiveDependencies": {
    "Regular": [
      "System.IO.Pipelines@9.0.0"
    ],
    "Ambiguous": [
      {
        "Package": "System.Text.Json@9.0.0",
        "ResolvedFrom": "DirectDependencies",
        "Requirements": [
          {
            "Version": "10.0.0",
            "RequiredBy": [
              "Some.Analyzer@2.1.0"
            ],
            "TargetFrameworks": [
              "net10.0"
            ]
          }
        ]
      }
    ],
    "Missing": []
  }
}
```

Packages are their `PackageInstance.ToString()` - `"packageId@version"` - rather than an object:
short, diffable, and parsed back by `PackageInstance` itself. An ambiguity is the one exception: it
needs its anchor and its requirements, so it is an object whose `"Package"` field carries the resolved
instance - the same field whatever the anchor, for a consumer that wants only the effective closure.

A dependency's version is any **SemVer**: an external package is not bound to CSemVer, unlike the
profile's own `Version`.

The API:

- `Write( Utf8JsonWriter )` writes the object. `Repository.Write`, `TransitiveDependencies.Write`,
  `AmbiguousDependency.Write` and `VersionRequirement.Write` do the same for one of their own, and
  each has the matching `Read`.
- `Read( ref Utf8JsonReader )` must be given a reader on the `StartObject` token (or one that has
  not started yet) and **leaves it on the `EndObject` token**. That is the usual converter contract,
  so a profile can be one property of a bigger document.
- `Parse( ReadOnlySpan<byte> )` reads a standalone document. It skips a leading utf-8 BOM, which
  `Utf8JsonReader` does not handle.
- `ToUtf8Bytes( indented )` / `ToJsonString( indented )` produce the text.

Two properties the output deliberately has: the new line is always `\r\n` (`JsonWriterOptions`
otherwise follows `Environment.NewLine`, which would make the same profile differ per platform), and
the encoder is the relaxed one - these are files, never embedded in html or in a script, so a
version's `+metadata` and a package's `@` stay readable.

Reading is strict about what it understands and forgiving about what it does not: an unknown
property is skipped, so the format can grow, but a missing or malformed one throws a `JsonException`
naming the property. A document that parses but does not describe a coherent profile throws the
constructor's `ArgumentException`.

The two dependency sets are the exception to "missing throws": they are **optional**, absent means
empty, and a profile written before they existed still parses. There is no file format version, and
`IsComplete` covers the only case a consumer must distinguish. They are nevertheless always written,
empty or not, so that every profile file has the same shape.

## Where do profiles live?

Not here. Storing profiles as files - one `v{Version}.json` per profile, in a folder subordinated to
the version's branch - is CKli's job, and only its
[Publish plugin](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Publish.Plugin/README.md)
does it, through its `PublishedFolder`. This package stays a contract: it describes a profile and
knows how to read and write one, and stops there.
