using CK.Core;
using System;

namespace CK.Packaging.Model;

/// <summary>
/// A repository is identified by its <paramref name="Id"/> and located by its <see cref="Url"/>.
/// </summary>
/// <param name="Url">The repository locator.</param>
/// <param name="Id">The repository identifier.</param>
public readonly record struct RepositoryKey( Uri Url, RandomId Id );


