using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CK.Packaging.Model;

/// <summary>
/// Mutable set of <see cref="PublishedProfile"/> identified by their <see cref="PublishedProfile.Version"/>.
/// </summary>
public sealed partial class PublishedFolder
{
    readonly string _rootPath;
    readonly Dictionary<SVersion, FileCacheInfo> _profiles;
    bool _allLoaded;

    sealed class FileCacheInfo
    {
        // The version is the key.
        readonly SVersion _version;
        // The full path to the json profile file (FileProfilePath method).
        readonly string _jsonFilePath;
        // If the file exists and has been successfully read: the profile it contains.
        readonly PublishedProfile? _loaded;
        // The exception when loaded if any (_loaded is obviously null).
        readonly Exception? _loadError;
        // Current profile (initially _loaded).
        PublishedProfile? _current;

        public FileCacheInfo( SVersion version, string jsonFilePath, Exception? ex )
        {
            _version = version;
            _jsonFilePath = jsonFilePath;
            _loadError = ex;
        }

        public PublishedProfile? Current { get => _current; set => _current = value; }

        internal static FileCacheInfo Read( SVersion version, string jsonFilePath )
        {
            try
            {
                var bytes = File.ReadAllBytes( jsonFilePath );
                return PublishedProfile.Parse( bytes );
            }
            catch( Exception ex )
            {
                return new FileCacheInfo( version, jsonFilePath, ex );
            }
        }
    }

    /// <summary>
    /// Initializes a new empty folder.
    /// </summary>
    public PublishedFolder( string rootPath )
    {
        rootPath = Path.GetFullPath( rootPath );
        if( !Directory.Exists( rootPath ) )
        {
            throw new ArgumentException( $"Published folder directory must exist." );
        }
        _rootPath = rootPath[^1] != Path.DirectorySeparatorChar
                        ? (Path.EndsInDirectorySeparator( rootPath ) ? rootPath[..^1] : rootPath) + Path.DirectorySeparatorChar
                        : rootPath;
        _profiles = new Dictionary<SVersion, FileCacheInfo>();
    }

    string FileProfilePath( SVersion version ) => version.IsStable
                                                    ? $"{_rootPath}v{version}.json"
                                                    : $"{_rootPath}{version.BranchName}/v{version}.json";

    FileCacheInfo LoadInfo( SVersion version )
    {
        if( _profiles.TryGetValue( version, out FileCacheInfo? loaded ) )
        {
            return loaded;
        }
        var path = FileProfilePath( version );
        if( File.Exists( path ) )
        {
            loaded = FileCacheInfo.Read( version, path );
        }
        else
        {
            loaded = new FileCacheInfo( version, path, null );
        }
        _profiles.Add( version, loaded );
        return loaded;
    }

    void LoadAll()
    {
        if( !_allLoaded )
        {
            foreach( var f in Directory.EnumerateFiles( _rootPath, "*.json", SearchOption.AllDirectories ) )
            {
                var sF = f.AsSpan();
                var fName = Path.GetFileNameWithoutExtension( sF );
                if( SVersion.TryMatch( ref fName, out var version, mustBeCSVersion: true ) )
                {
                    // Skip any duplicate. Already loaded, bad +metadata...
                    if( !_profiles.ContainsKey( version ) )
                    {
                        _profiles.Add( version, FileCacheInfo.Read( version, FileProfilePath( version ) ) );
                    }
                }
            }
            _allLoaded = true;
        }
    }

    /// <summary>
    /// Adds a profile. Throw if a profile with the same <see cref="PublishedProfile.Version"/> already exists.
    /// </summary>
    /// <param name="profile">The profile to add.</param>
    public void Add( PublishedProfile profile )
    {
        var info = LoadInfo( profile.Version ); 
        if( info.Current != null )
        {
            throw new InvalidOperationException( $"Profile '{profile.Version}' already exists." );
        }
        info.Current = profile;
    }

    /// <summary>
    /// Removes a profile.
    /// </summary>
    /// <param name="version">The profile's version to remove.</param>
    /// <returns>True if the profile has been found and removed, false otherwise.</returns>
    public bool Remove( SVersion version )
    {
        if( _profiles.TryGetValue( version, out var info ) && info.Current != null )
        {
            info.Current = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Deprecates a profile. This is idempotent and doesn't require the profile to exist.
    /// </summary>
    /// <param name="version">The version to deprecate.</param>
    /// <returns>True if the profile has been found and actually deprecated. False otherwise.</returns>
    public bool Deprecate( SVersion version )
    {
        if( _profiles.TryGetValue( version, out var info )
            && info.Current != null
            && !info.Current.IsDeprecated )
        {
            info.Current = info.Current.Deprecate();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Deprecates all the profiles that contains the provided package.
    /// </summary>
    /// <param name="packageId">The deprecated package identifier.</param>
    /// <param name="version">The deprecated package version.</param>
    /// <returns>True if at least one profile has been deprecated. False otherwise.</returns>
    public bool OnDeprecatedPackage( string packageId, SVersion version )
    {
        bool found = false;
        LoadAll();
        foreach( var info in _profiles.Values )
        {
            var p = info.Current;
            if( p != null )
            {
                var n = p.OnDeprecatedPackage( packageId, version );
                if( p != n )
                {
                    info.Current = n;
                    found = true;
                }
            }
        }
        return found;
    }

}


