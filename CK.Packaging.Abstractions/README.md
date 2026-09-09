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
empty - and an empty `TransitiveDependencies` says nothing about the transitive packages, not that
there are none.

- `DirectDependencies` are the packages consumed by at least one `Repository`, minus the produced
  identifiers. Like the produced packages, one version per identifier - and none of them can be a
  produced one, since the two sets are complementary by definition.
- `TransitiveDependencies` is what a restore brings beyond those - the union of what NuGet resolved
  transitively for each repository - in two lists:
  - `Regular` - the ones every repository resolved to the same single version.
  - `Ambiguous` - the ones the repositories disagreed on, or that disagree with the profile itself
    (below).

Nothing here is a *computed* closure: nothing is walked, nothing can be unreachable and no package
can be missing. Each repository's set comes from NuGet's own resolution - target framework aware and
pruned - so an `Ambiguous` entry exists only because two repositories, or one repository and the
profile, ended up with different versions.

`Regular` and `Ambiguous` partition the set by package identifier - the two never share one, and
neither can name a direct or produced identifier as a regular entry - so a consumer that ignores the
details reads their union as a flat, coherent set of resolved instances.

An [`AmbiguousDependency`](AmbiguousDependency.cs) **is** a `PackageInstance`: its inherited `Version`
is what this profile resolves the identifier to, and its `ResolvedFrom` says where that version comes
from. Each [`VersionSource`](VersionSource.cs) value names the profile property that holds it:

| `ResolvedFrom` | The resolved version is | The `Resolutions` are |
|---|---|---|
| `TransitiveDependencies` | the greatest resolution (NuGet's highest-wins) | all of them, at least 2, disagreeing among themselves |
| `DirectDependencies` | the direct dependency's version, which NuGet's nearest-wins makes authoritative | the ones that resolved to **more** |
| `ProducedPackages` | the version this profile produces | the ones that resolved to **more** |

Only the harmful direction is reported. A resolution *below* what the profile references or produces
is invisible to a restore, and one equal to the anchor is not a disagreement at all - so an anchored
identifier with nothing greater to report is simply not in the list, since `DirectDependencies` or
`ProducedPackages` already states its version.

A `VersionResolution` is one resolved version and the `Repositories` whose restore produced it, named
by their `RepositoryKey.Id` - the join key with `PublishedProfile.Repositories`, which the constructor
checks. Those identifiers are what a disagreement has to name to be diagnosable: NuGet's package list
never says which *package* pulled an identifier in, only which project ended up with which version.
One repository can appear in two resolutions of the same ambiguity - its own restore resolves an
identifier to two versions when two of its target frameworks resolve differently. Resolutions are
sorted by descending version, and so is everything inside them: canonical form again. Their equality
is **structural**, unlike the one a `record struct` would synthesize (`ImmutableArray`'s own equality
compares the underlying array by reference).

> The model is **TFM-blind**: one flat set, not one per target framework - `BuildContentInfo` has
already flattened the frameworks away before a profile is built. Recovering them means making that
stored format framework-qualified first; it is CKli's business, not this contract's.

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

## PublishedIndex

A [`PublishedIndex`](PublishedIndex.cs) is the index of a *set* of profiles: their versions, split
into the alive ones and the `IsDeprecated` ones, each grouped by branch and each group ordered from
the latest to the oldest.

```csharp
var index = PublishedIndex.Create( profiles );
var latestAlpha = index.GetAlive( PublishedIndex.GetGroupName( "alpha", isCI: false ) ).FirstOrDefault();
```

```json
{
  "Alive": {
    "(stable)": [ "1.2.5", "1.2.3" ],
    "(stable-ci)": [ "1.2.6--ci.0" ],
    "alpha-ci": [ "1.3.0-alpha.0.ci.7" ],
    "explo/some-explo": [ "0.0.0-0.some-explo" ]
  },
  "Deprecated": {
    "(stable)": [ "1.2.4" ],
    "alpha": [ "1.3.0-alpha" ]
  }
}
```

It carries versions and nothing else, because a version is enough to reach its profile
(`GetProfilePath` below). That is what makes it the thing a consumer reads to *choose* a publication
- the latest one of a branch, say - without opening every profile file.

**The grouping is not stored state.** A group name is a pure function of the version it holds, so an
index cannot disagree with itself about where a version belongs:

- `GetGroupName( SVersion )` is that function: the version's `SVersion.BranchName`, with the CI
  builds of a branch in their own `-ci` group. It requires a Conformant `SVersion`, which a profile
  version always is.
- `GetGroupName( branchName, isCI )` is the same rule from a branch, and it is the overload to look
  a branch up with. The `branchName` is the **version** side of a branch - the empty string for the
  stable line, `"alpha"` to `"zulu"`, `"explo/{name}"` - not the name a repository gives it.
- The constructor groups the versions it is given, so `Read` *checks* the group names in the file
  rather than trusting them: a version listed under a group it doesn't belong to throws.

The stable group is named `"(stable)"` rather than a root branch name, deliberately: a World's root
branch is the business of whoever models branches and can be renamed (an LTS World has its own),
while an index names the versions it contains. The parentheses cannot collide with a branch name,
and the ordinal ordering of the groups then puts `"(stable)"` first and every group immediately
before its own CI one - `)` and the end of a string both precede `-`.

`"(stable)"` is also the one group that is written even when empty (in both sets), so a consumer
always has the root list to read. Every other group exists only because a version landed in it, and
`GetAlive` / `GetDeprecated` answer an empty list for an absent one.

A version identifies a profile, hence a file, so it appears **once** across the two sets: a
duplicate - or the same version listed as both alive and deprecated - throws.

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
        "Resolutions": [
          {
            "Version": "10.0.0",
            "Repositories": [
              "AQAAAAAAAAA"
            ]
          }
        ]
      }
    ]
  }
}
```

Packages are their `PackageInstance.ToString()` - `"packageId@version"` - rather than an object:
short, diffable, and parsed back by `PackageInstance` itself. An ambiguity is the one exception: it
needs its anchor and its resolutions, so it is an object whose `"Package"` field carries the resolved
instance - the same field whatever the anchor, for a consumer that wants only the effective set.

A dependency's version is any **SemVer**: an external package is not bound to CSemVer, unlike the
profile's own `Version`.

The API:

- `Write( Utf8JsonWriter )` writes the object. `Repository.Write`, `TransitiveDependencies.Write`,
  `AmbiguousDependency.Write` and `VersionResolution.Write` do the same for one of their own, and
  each has the matching `Read`.
- `Read( ref Utf8JsonReader )` must be given a reader on the `StartObject` token (or one that has
  not started yet) and **leaves it on the `EndObject` token**. That is the usual converter contract,
  so a profile can be one property of a bigger document.
- `Parse( ReadOnlySpan<byte> )` reads a standalone document. It skips a leading utf-8 BOM, which
  `Utf8JsonReader` does not handle.
- `ToUtf8Bytes( indented )` / `ToJsonString( indented )` produce the text.

`PublishedIndex` has the same four, over the shape shown in its own section above.

Two properties the output deliberately has: the new line is always `\r\n` (`JsonWriterOptions`
otherwise follows `Environment.NewLine`, which would make the same profile differ per platform), and
the encoder is the relaxed one - these are files, never embedded in html or in a script, so a
version's `+metadata` and a package's `@` stay readable. Both are set once, for every type here.

Reading is strict about what it understands and forgiving about what it does not: an unknown
property is skipped, so the format can grow, but a missing or malformed one throws a `JsonException`
naming the property. A document that parses but does not describe a coherent profile throws the
constructor's `ArgumentException`.

The two dependency sets are the exception to "missing throws": they are **optional**, absent means
empty, and a profile written before they existed still parses. There is no file format version:
an empty `TransitiveDependencies` and an absent one say the same thing, which is why none is needed.
They are nevertheless always written, empty or not, so that every profile file has the same shape.

## Where do profiles live?

Not here. Storing profiles as files - one `v{Version}.json` per profile, in a folder subordinated to
the version's branch, next to their `index.json` - is CKli's job, and only its
[Publish plugin](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Publish.Plugin/README.md)
does it, through its `PublishedFolder`. This package stays a contract: it describes a profile, an
index of profiles and where a profile file goes (`GetProfilePath`, `IndexFileName`), knows how to
read and write both, and stops there. No `Directory`, no `File`, no url is opened.

That split is what lets a *consumer* read a publication it does not own: given the bytes of an
`index.json` - from a folder, a git tree, an http response, wherever they come from - and then the
bytes of one profile file, this package answers what was published, with no CKli in sight.
