using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CK.Packaging.Model;

/// <summary>
/// Mutable set of <see cref="PublishedProfile"/> identified by their <see cref="PublishedProfile.Version"/>.
/// </summary>
public sealed partial class PublishedFolder
{
    readonly Dictionary<SVersion, PublishedProfile> _profiles;

    /// <summary>
    /// Initializes a new empty folder.
    /// </summary>
    public PublishedFolder()
    {
        _profiles = new Dictionary<SVersion, PublishedProfile>();
    }

    /// <summary>
    /// Gets the profiles in indexed by their <see cref="PublishedProfile.Version"/>.
    /// </summary>
    public IReadOnlyDictionary<SVersion, PublishedProfile> Profiles => _profiles;

    /// <summary>
    /// Adds a profile. Throw if a profile with the same <see cref="PublishedProfile.Version"/> already exists.
    /// </summary>
    /// <param name="profile">The profile to add.</param>
    public void Add( PublishedProfile profile )
    {
        _profiles.Add( profile.Version, profile );
    }

    /// <summary>
    /// Deprecates a profile. This is idempotent and doesn't require the profile to exist.
    /// </summary>
    /// <param name="version">The version to deprecate.</param>
    /// <returns>True if the profile has been found and actually deprecated. False otherwise.</returns>
    public bool Deprecate( SVersion version )
    {
        if( _profiles.TryGetValue( version, out var profile ) && !profile.IsDeprecated )
        {
            _profiles[version] = profile.Deprecate();
            return true;
        }
        return false;
    }

}


