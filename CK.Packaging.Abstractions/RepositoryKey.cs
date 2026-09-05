using CK.Core;
using System;

namespace CK.Packaging.Abstractions;

/// <summary>
/// A repository is identified by its <see cref="Id"/> and located by its <see cref="Url"/>.
/// <para>
/// The <see cref="Id"/> is the identity: a repository can be moved (its <see cref="Url"/> changes)
/// without losing its identity.
/// </para>
/// </summary>
/// <param name="Url">The repository locator. Must be an absolute url.</param>
/// <param name="Id">The repository identifier. Must be <see cref="RandomId.IsValid"/>.</param>
public readonly record struct RepositoryKey( Uri Url, RandomId Id )
{
    /// <summary>
    /// Gets whether this key is valid: <see cref="Url"/> is a non null absolute url and
    /// <see cref="Id"/> is <see cref="RandomId.IsValid"/>.
    /// </summary>
    public bool IsValid => Url is not null && Url.IsAbsoluteUri && Id.IsValid;

    /// <summary>
    /// Overridden to return "<see cref="Url"/> (<see cref="Id"/>)".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{Url} ({Id})";
}
