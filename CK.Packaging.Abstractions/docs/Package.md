Defines a Profile as a coherent set of package instances.

A `PublishedProfile` is the immutable set of packages a CKli World has published under one version:
its `Repositories` hold the `PackageInstance` they produced and the profile offers at most one version
per package identifier. A profile can be deprecated (`Deprecate`, `OnDeprecatedPackage`), which yields
a new profile: the model is a set of immutable values with updaters.

Serialization uses the basic `Utf8JsonWriter`/`Utf8JsonReader`: `PublishedProfile.Write`,
`PublishedProfile.Read` (that can be used inside a bigger document) and `PublishedProfile.Parse`.
Storing profiles as files is not this package's job: CKli's Publish plugin owns the folder that
holds them.
