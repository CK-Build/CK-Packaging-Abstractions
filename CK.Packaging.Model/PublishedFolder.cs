using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CK.Packaging.Model;

public sealed partial class PublishedFolder
{
    readonly Dictionary<string, Branch> _branches;

    /// <summary>
    /// Initializes a new empty folder.
    /// </summary>
    public PublishedFolder()
    {
        _branches = new Dictionary<string, Branch>();
    }

    /// <summary>
    /// Gets the <see cref="Branch"/> that contains the <see cref="Branch.Profiles"/>.
    /// </summary>
    public IReadOnlyDictionary<string, Branch> Branches => _branches;


    /// <summary>
    /// Finds a <see cref="PublishedProfile"/> from its <see cref="PublishedProfile.Version"/>.
    /// </summary>
    /// <param name="version">The profile version.</param>
    /// <returns>The profile or null.</returns>
    public PublishedProfile? Find( SVersion version )
    {
        var bName = version.BranchName;
        if( ) return null;
        if( bName == null
            || !_branches.TryGetValue( bName, out var branch ) )
        {
            return null;
        }
        return branch.Find( version );
    }

    /// <summary>
    /// Adds a profile or throw an <see cref="InvalidOperationException "/> if a profile
    /// with the same <see cref="PublishedProfile.Version"/> already exists.
    /// </summary>
    /// <param name="profile"></param>
    public void Add( PublishedProfile profile )
    {
        var version = profile.Version;
        Debug.Assert( version.BranchName != null, "Version is a CSVersion." );
        var bName = version.BranchName;
        if( !_branches.TryGetValue( bName, out var branch ) )
        {
            branch = new Branch( bName, version.VersionKind );
        }
        branch.Add( profile );
    }
}


