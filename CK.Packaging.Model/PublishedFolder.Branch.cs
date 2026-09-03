using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CK.Packaging.Model;

public sealed partial class PublishedFolder
{
    /// <summary>
    /// Groups <see cref="PublishedProfile"/> by their <see cref="SVersion.BranchName"/>: this
    /// is the subfolder of the profiles.
    /// </summary>
    public sealed class Branch
    {
        readonly string _branchName;
        readonly CSVersionKind _kind;
        readonly List<PublishedProfile> _profiles;

        internal Branch( string branchName, CSVersionKind kind )
        {
            _branchName = branchName;
            _kind = kind;
            _profiles = new List<PublishedProfile>();
        }

        /// <summary>
        /// Gets the branch name.
        /// </summary>
        public string BranchName => _branchName;

        /// <summary>
        /// Gets the Conformant SVersion kind (never <see cref="CSVersionKind.None"/>).
        /// </summary>
        public CSVersionKind Kind => _kind;

        /// <summary>
        /// Gets the profiles in descending order of their <see cref="PublishedProfile.Version"/>: the latest
        /// is the first one.
        /// </summary>
        public IReadOnlyList<PublishedProfile> Profiles => _profiles;

        readonly struct VersionFinder : IComparable<PublishedProfile>
        {
            readonly SVersion _version;

            public VersionFinder( SVersion version ) => _version = version;

            public int CompareTo( PublishedProfile? other )
            {
                Debug.Assert( other != null );
                return other.Version.CompareTo( _version );
            }
        }

        internal PublishedProfile? Find( SVersion version )
        {
            var idx = CollectionsMarshal.AsSpan( _profiles ).BinarySearch( new VersionFinder( version ) );
            return idx >= 0 ? _profiles[idx] : null;
        }

        internal void Add( PublishedProfile profile )
        {
            var idx = CollectionsMarshal.AsSpan( _profiles ).BinarySearch( new VersionFinder( profile.Version ) );
            if( idx >= 0 ) throw new InvalidOperationException( $"A profile already exists for version '{profile.Version}'." );
            _profiles.Insert( ~idx, profile );
        }
    }
}


