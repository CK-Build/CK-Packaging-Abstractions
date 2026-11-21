using CK.Core;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CK.Packaging.Abstractions;

/// <summary>
/// The version of a <see cref="PublishedProfile"/>: the day of the publication ("Year.Month.Day"), a
/// <see cref="ClashNumber"/> that tells apart the publications of the same day (an optional fourth part),
/// and the branch and CI build of what was published.
/// <para>
/// This is not a package version. A profile names a multi-repository set of packages, so no package
/// version can name it: its version is the publication's own. This type is orthogonal to
/// <see cref="SVersion"/>, but its <see cref="BranchName"/>, <see cref="VersionKind"/>,
/// <see cref="ExploratoryName"/> and <see cref="CINumber"/> have exactly the semantics (and the
/// updaters) of a Conformant SVersion's ones - there is no prerelease number.
/// </para>
/// <para>
/// Its string form is the one of its <see cref="ToSVersion"/>: "Year.Month.Day", followed by the
/// <see cref="ClashNumber"/> as a fourth part when it is not 0, with the Conformant prerelease of its
/// branch and CI build ("2026.11.21", "2026.11.21.1", "2026.11.21-alpha", "2026.11.21.2--ci.0",
/// "2026.11.21-0.explo.0.ci.0"). <see cref="SVersionTimeVersionExtensions.AsTimeVersion"/> is the way back.
/// </para>
/// <para>
/// The ordering is by <see cref="Date"/>, then <see cref="ClashNumber"/>, then by the Conformant SVersion
/// ordering of the branch and CI build. Only the first two matter in practice: profile versions are
/// grouped by branch and CI build (see <see cref="PublishedIndex.GetGroupName(TimeVersion)"/>).
/// </para>
/// </summary>
public sealed class TimeVersion : IEquatable<TimeVersion>, IComparable<TimeVersion>
{
    // The branch and CI build are carried by a "0.0.0" Conformant SVersion whose PrereleaseNumber is 0:
    // this reuses the CSVersion rules (exploratory name validation, CI forms) without duplicating them.
    static readonly SVersion _stableBranch = SVersion.Create( 0, 0, 0 );

    readonly DateOnly _date;
    readonly int _clashNumber;
    readonly SVersion _branch;
    readonly SVersion _sVersion;

    TimeVersion( DateOnly date, int clashNumber, SVersion branch )
    {
        Debug.Assert( clashNumber >= 0 );
        Debug.Assert( branch.IsCSVersion
                      && branch.Major == 0 && branch.Minor == 0 && branch.Patch == 0
                      && branch.PrereleaseNumber == 0
                      && branch.BuildMetaData.Length == 0 );
        _date = date;
        _clashNumber = clashNumber;
        _branch = branch;
        _sVersion = SVersion.Create( date.Year, date.Month, date.Day, branch.Prerelease, fourthPart: clashNumber == 0 ? -1 : clashNumber );
        Debug.Assert( _sVersion.IsValid );
    }

    /// <summary>
    /// Creates a stable, non CI, version.
    /// </summary>
    /// <param name="date">The date.</param>
    /// <param name="clashNumber">The clash number. Must be 0 or positive.</param>
    /// <returns>The version.</returns>
    public static TimeVersion Create( DateOnly date, int clashNumber = 0 )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( clashNumber );
        return new TimeVersion( date, clashNumber, _stableBranch );
    }

    /// <summary>
    /// Gets the date of the publication.
    /// </summary>
    public DateOnly Date => _date;

    /// <summary>
    /// Gets the year of the <see cref="Date"/>: the first part.
    /// </summary>
    public int Year => _date.Year;

    /// <summary>
    /// Gets the month of the <see cref="Date"/>: the second part.
    /// </summary>
    public int Month => _date.Month;

    /// <summary>
    /// Gets the day of the <see cref="Date"/>: the third part.
    /// </summary>
    public int Day => _date.Day;

    /// <summary>
    /// Gets the number that tells apart the versions of the same <see cref="Date"/>: 0 for the first one
    /// (there is no fourth part), then 1, 2, etc. (the fourth part).
    /// </summary>
    public int ClashNumber => _clashNumber;

    /// <summary>
    /// Gets the kind of the branch: <see cref="CSVersionKind.Stable"/>, <see cref="CSVersionKind.Exploratory"/>
    /// or one of the <see cref="CSVersionKind.Alpha"/> to <see cref="CSVersionKind.Zulu"/> prereleases.
    /// Never <see cref="CSVersionKind.None"/>.
    /// </summary>
    public CSVersionKind VersionKind => _branch.VersionKind;

    /// <summary>
    /// Gets the branch name: the empty string for <see cref="CSVersionKind.Stable"/>, "alpha" to "zulu" or
    /// "explo/<see cref="ExploratoryName"/>". See <see cref="SVersion.BranchName"/>.
    /// </summary>
    public string BranchName => _branch.BranchName!;

    /// <summary>
    /// Gets the exploratory name. Empty when <see cref="VersionKind"/> is not <see cref="CSVersionKind.Exploratory"/>.
    /// </summary>
    public ReadOnlySpan<char> ExploratoryName => _branch.ExploratoryName;

    /// <summary>
    /// Gets the CI build number. -1 when this is not a CI build version.
    /// </summary>
    public int CINumber => _branch.CINumber;

    /// <summary>
    /// Gets whether this is a CI build version: <see cref="CINumber"/> is 0 or positive.
    /// </summary>
    public bool IsCI => _branch.IsCI;

    /// <summary>
    /// Returns a version with the same branch and CI build and another <see cref="Date"/> and <see cref="ClashNumber"/>.
    /// </summary>
    /// <param name="date">The new date.</param>
    /// <param name="clashNumber">The new clash number. Must be 0 or positive.</param>
    /// <returns>This or a new version.</returns>
    public TimeVersion SetDate( DateOnly date, int clashNumber )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( clashNumber );
        return date == _date && clashNumber == _clashNumber ? this : new TimeVersion( date, clashNumber, _branch );
    }

    /// <summary>
    /// Returns a version with another <see cref="ClashNumber"/>.
    /// </summary>
    /// <param name="clashNumber">The new clash number. Must be 0 or positive.</param>
    /// <returns>This or a new version.</returns>
    public TimeVersion SetClashNumber( int clashNumber ) => SetDate( _date, clashNumber );

    /// <summary>
    /// Returns a version with the specified CI build number or clears it by setting it to -1.
    /// See <see cref="SVersion.SetCINumber(int, bool)"/>: unlike it, no other part is impacted.
    /// </summary>
    /// <param name="ciNumber">-1 to remove the CI build number, must be 0 or positive otherwise.</param>
    /// <returns>This or a new version.</returns>
    public TimeVersion SetCINumber( int ciNumber ) => With( _branch.SetCINumber( ciNumber, impactStablePatchNumber: false ) );

    /// <summary>
    /// Sets one of the <see cref="CSVersionKind.Alpha"/> to <see cref="CSVersionKind.Zulu"/> branches.
    /// The <see cref="CINumber"/> is preserved. See <see cref="SVersion.SetBranchName(CSVersionKind)"/>.
    /// </summary>
    /// <param name="prereleaseName">The prerelease kind.</param>
    /// <returns>This or a new version.</returns>
    public TimeVersion SetBranchName( CSVersionKind prereleaseName ) => With( _branch.SetBranchName( prereleaseName ) );

    /// <summary>
    /// Sets the branch from its name: the empty string for the stable branch, "alpha" to "zulu" or
    /// "explo/{name}". The <see cref="CINumber"/> is preserved. See <see cref="SVersion.SetBranchName(string)"/>.
    /// </summary>
    /// <param name="validBranchName">The branch name. Any other value throws an <see cref="ArgumentException"/>.</param>
    /// <returns>This or a new version.</returns>
    public TimeVersion SetBranchName( string validBranchName ) => With( _branch.SetBranchName( validBranchName ) );

    /// <summary>
    /// Sets an exploratory branch. The <see cref="CINumber"/> is preserved.
    /// See <see cref="SVersion.SetExploratoryName(string)"/>.
    /// </summary>
    /// <param name="name">The exploratory name. May start with "explo/".</param>
    /// <returns>This or a new version.</returns>
    public TimeVersion SetExploratoryName( string name ) => With( _branch.SetExploratoryName( name ) );

    TimeVersion With( SVersion branch ) => branch == _branch ? this : new TimeVersion( _date, _clashNumber, branch );

    /// <summary>
    /// Gets the <see cref="SVersion"/> of this version: "Year.Month.Day" with the <see cref="ClashNumber"/>
    /// as its <see cref="SVersion.FourthPart"/> when it is not 0. <see cref="SVersionTimeVersionExtensions.AsTimeVersion"/>
    /// converts it back.
    /// </summary>
    /// <returns>The SVersion.</returns>
    public SVersion ToSVersion() => _sVersion;

    /// <summary>
    /// Gets the string form of this version, the one of <see cref="ToSVersion"/>.
    /// </summary>
    /// <returns>The version string (without 'v' prefix).</returns>
    public override string ToString() => _sVersion.ToString();

    /// <summary>
    /// Tries to parse a version. See <see cref="SVersionTimeVersionExtensions.AsTimeVersion"/>.
    /// </summary>
    /// <param name="s">The string to parse. A leading 'v' is allowed.</param>
    /// <param name="version">The version on success.</param>
    /// <param name="acceptDayOfYear">True to accept the "Year.DayOfYear.Number" legacy form.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string? s, [NotNullWhen( true )] out TimeVersion? version, bool acceptDayOfYear = false )
    {
        version = SVersion.TryParse( s, out var v ) ? v.AsTimeVersion( acceptDayOfYear ) : null;
        return version != null;
    }

    /// <summary>
    /// Parses a version or throws an <see cref="ArgumentException"/>.
    /// See <see cref="SVersionTimeVersionExtensions.AsTimeVersion"/>.
    /// </summary>
    /// <param name="s">The string to parse. A leading 'v' is allowed.</param>
    /// <param name="acceptDayOfYear">True to accept the "Year.DayOfYear.Number" legacy form.</param>
    /// <returns>The version.</returns>
    public static TimeVersion Parse( string? s, bool acceptDayOfYear = false )
    {
        return TryParse( s, out var v, acceptDayOfYear )
                ? v
                : throw new ArgumentException( $"Invalid TimeVersion: '{s}'.", nameof( s ) );
    }

    // Called by SVersionTimeVersionExtensions.AsTimeVersion.
    internal static TimeVersion? Create( SVersion v, bool acceptDayOfYear )
    {
        if( !v.IsValid || v.BuildMetaData.Length != 0 || v.Major < 1 || v.Major > 9999 ) return null;
        DateOnly date;
        int clashNumber;
        if( v.Minor > 12 )
        {
            // The "Year.DayOfYear.Number" legacy form is a three-part Conformant SVersion. These versions have
            // been published since September 2026: their DayOfYear is always greater than 12.
            if( !acceptDayOfYear
                || !v.IsCSVersion
                || v.Minor > (DateTime.IsLeapYear( v.Major ) ? 366 : 365) )
            {
                return null;
            }
            date = new DateOnly( v.Major, 1, 1 ).AddDays( v.Minor - 1 );
            clashNumber = v.Patch;
        }
        else
        {
            // An explicit ".0" fourth part is not the canonical form: a version has only one string.
            if( v.FourthPart == 0
                || v.Minor < 1
                || v.Patch < 1 || v.Patch > DateTime.DaysInMonth( v.Major, v.Minor ) )
            {
                return null;
            }
            date = new DateOnly( v.Major, v.Minor, v.Patch );
            clashNumber = Math.Max( v.FourthPart, 0 );
        }
        var branch = v.Prerelease.Length == 0
                        ? _stableBranch
                        : SVersion.ParseNoThrow( "0.0.0-" + v.Prerelease, mustBeCSVersion: true );
        if( !branch.IsValid || branch.PrereleaseNumber != 0 ) return null;
        return new TimeVersion( date, clashNumber, branch );
    }

    /// <summary>
    /// Compares this version with another one: by <see cref="Date"/>, then <see cref="ClashNumber"/>, then by
    /// branch and CI build (the Conformant SVersion ordering). Null is lower than any version.
    /// </summary>
    /// <param name="other">The other version.</param>
    /// <returns>Standard positive, negative or zero value.</returns>
    public int CompareTo( TimeVersion? other )
    {
        if( other is null ) return 1;
        int r = _date.CompareTo( other._date );
        if( r != 0 ) return r;
        r = _clashNumber - other._clashNumber;
        if( r != 0 ) return r;
        return _branch.CompareTo( other._branch );
    }

    /// <summary>
    /// Gets whether the other version is the same: same <see cref="Date"/>, <see cref="ClashNumber"/>, branch
    /// and CI build.
    /// </summary>
    /// <param name="other">The other version.</param>
    /// <returns>True when equal, false otherwise.</returns>
    public bool Equals( TimeVersion? other ) => other is not null
                                                && _date == other._date
                                                && _clashNumber == other._clashNumber
                                                && _branch == other._branch;

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is TimeVersion v && Equals( v );

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine( _date, _clashNumber, _branch );

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static bool operator ==( TimeVersion? left, TimeVersion? right ) => left is null ? right is null : left.Equals( right );

    public static bool operator !=( TimeVersion? left, TimeVersion? right ) => !(left == right);

    public static bool operator <( TimeVersion? left, TimeVersion? right ) => left is null ? right is not null : left.CompareTo( right ) < 0;

    public static bool operator <=( TimeVersion? left, TimeVersion? right ) => left is null || left.CompareTo( right ) <= 0;

    public static bool operator >( TimeVersion? left, TimeVersion? right ) => left is not null && left.CompareTo( right ) > 0;

    public static bool operator >=( TimeVersion? left, TimeVersion? right ) => left is null ? right is null : left.CompareTo( right ) >= 0;
#pragma warning restore CS1591
}

/// <summary>
/// Extends <see cref="SVersion"/> with the conversion to a <see cref="TimeVersion"/>.
/// </summary>
public static class SVersionTimeVersionExtensions
{
    /// <summary>
    /// Gets the <see cref="TimeVersion"/> of a "Year.Month.Day" or "Year.Month.Day.ClashNumber" version (the
    /// clash number is never 0) whose prerelease (if any) is a Conformant SVersion's one without prerelease number:
    /// the empty string, "alpha" to "zulu", "0.{exploratory name}", possibly with their CI build forms ("-ci.N",
    /// "alpha.0.ci.N", "0.{name}.0.ci.N"). The date must be valid and there must be no build metadata.
    /// <para>
    /// With <paramref name="acceptDayOfYear"/>, the "Year.DayOfYear.Number" Conformant SVersion that was the
    /// profile version before is also converted (2026.325.1 is 2026.11.21.1): it has been used since September 2026,
    /// so its second part is greater than 12 and cannot be confused with a month.
    /// </para>
    /// </summary>
    /// <param name="v">This version.</param>
    /// <param name="acceptDayOfYear">True to accept the "Year.DayOfYear.Number" legacy form.</param>
    /// <returns>The time version or null if this version cannot be one.</returns>
    public static TimeVersion? AsTimeVersion( this SVersion v, bool acceptDayOfYear = false )
    {
        ArgumentNullException.ThrowIfNull( v );
        return TimeVersion.Create( v, acceptDayOfYear );
    }
}
