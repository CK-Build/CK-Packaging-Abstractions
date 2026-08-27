using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CK.Packaging.Model;

/// <summary>
/// Defines a set of package instances that have been produced by a CKli world and are coherent: their dependencies
/// are homogeneous, no discrepancy exists among them.
/// <para>
/// Note that the coherency can be only a shallow one. deeper, transitive, dependencies are not guaranteed to be aligned.
/// </para>
/// </summary>
public sealed class Profile
{
    readonly Uri _stackUrl;
    readonly string _worldName;
    readonly string _branchName;
    readonly DateTime _date;
    readonly Dictionary<string,Version> _packages;

    public Profile( Uri stackUrl, string worldName, string branchName, DateTime date, Dictionary<string, Version> packages )
    {
        _stackUrl = stackUrl;
        _worldName = worldName;
        _branchName = branchName;
        _date = date;
        _packages = packages;
    }

}


