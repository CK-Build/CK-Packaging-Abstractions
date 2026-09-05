Defines a Profile as a coherent set of package instances.

A `PublishedProfile` is the immutable set of packages a CKli World has published under one version:
its `Repositories` hold the `PackageInstance` they produced and the profile offers at most one version
per package identifier. A profile can be deprecated (`Deprecate`, `OnDeprecatedPackage`), which yields
a new profile: the model is a set of immutable values with updaters.

A `PublishedFolder` is the mutable, file based set of the profiles of a World. Each profile is a Json
file named `v{Version}.json` that lies in the root folder for stable versions (and their CI builds)
and in a subordinated folder named after the version's branch for the others (`alpha` ... `zulu`,
`explo/{name}`). Files are read on demand and any modification stays in memory until `Save` is called.

Serialization uses the basic `Utf8JsonWriter`/`Utf8JsonReader`: `PublishedProfile.Write`,
`PublishedProfile.Read` (that can be used inside a bigger document) and `PublishedProfile.Parse`.
