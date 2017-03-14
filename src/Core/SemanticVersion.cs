﻿using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NuGet.Resources;

namespace NuGet
{
    /// <summary>
    /// A hybrid implementation of SemVer that supports semantic versioning as described at http://semver.org while not strictly enforcing it to 
    /// allow older 4-digit versioning schemes to continue working.
    /// </summary>
    [Serializable]
    [TypeConverter(typeof(SemanticVersionTypeConverter))]
    public sealed class SemanticVersion : IComparable, IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private const RegexOptions _flags = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture;
        private static readonly Regex _semanticVersionRegex = new Regex(@"^(?<Version>\d+(\s*\.\s*\d+){0,3})(?<PackageVersion>_\d+)?(?<Prerelease>-[a-z][0-9a-z-]*)?$", _flags);
        private static readonly Regex _strictSemanticVersionRegex = new Regex(@"^(?<Version>\d+(\.\d+){2})(?<PackageVersion>_\d+)?(?<Prerelease>-[a-z][0-9a-z-]*)?$", _flags);
        private readonly string _originalString;
        private string _normalizedVersionString;

        public SemanticVersion(string version)
            : this(Parse(version))
        {
            // The constructor normalizes the version string so that it we do not need to normalize it every time we need to operate on it. 
            // The original string represents the original form in which the version is represented to be used when printing.
            _originalString = version;
        }

        public SemanticVersion(int major, int minor, int build, int revision)
            : this(new Version(major, minor, build, revision))
        {
        }

        public SemanticVersion(int major, int minor, int build, string specialVersion, int packageReleaseVersion)
            : this(new Version(major, minor, build), specialVersion, packageReleaseVersion)
        {
        }

        public SemanticVersion(Version version)
            : this(version, String.Empty, 0)
        {
        }

        public SemanticVersion(Version version, string specialVersion)
            : this(version, specialVersion, 0)
        {
        } 
        
        public SemanticVersion(Version version, string specialVersion, int packageReleaseVersion)
            : this(version, specialVersion, packageReleaseVersion, null)
        {
        }

        private SemanticVersion(Version version, string specialVersion, int packageReleaseVersion, string originalString)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            Version = NormalizeVersionValue(version);
            SpecialVersion = specialVersion ?? String.Empty;
            PackageReleaseVersion = packageReleaseVersion;
            _originalString = String.IsNullOrEmpty(originalString) ? version.ToString() + (packageReleaseVersion != 0 ? '_' + packageReleaseVersion.ToString() : null) + (!String.IsNullOrEmpty(specialVersion) ? '-' + specialVersion : null) : originalString;
        }

        internal SemanticVersion(SemanticVersion semVer)
        {
            _originalString = semVer.ToString();
            Version = semVer.Version;
            SpecialVersion = semVer.SpecialVersion;
            PackageReleaseVersion = semVer.PackageReleaseVersion;
        }

        /// <summary>
        /// Gets the normalized version portion.
        /// </summary>
        public Version Version { get; private set; }

        /// <summary>
        /// Gets the optional special version.
        /// </summary>
        public string SpecialVersion { get; private set; }

        public int PackageReleaseVersion { get; private set; }

        public string[] GetOriginalVersionComponents()
        {
            if (!String.IsNullOrEmpty(_originalString))
            {
                string original = _originalString;

                // search the start of the ReleaseVersion part, if any
                int packageFixIndex = original.IndexOf('_');
                if (packageFixIndex != -1)
                {
                    // remove the PackageReleaseVersion part
                    original = original.Substring(0, packageFixIndex);
                }
                
                // search the start of the SpecialVersion part, if any
                int dashIndex = original.IndexOf('-');
                if (dashIndex != -1)
                {
                    // remove the SpecialVersion part
                    original = original.Substring(0, dashIndex);
                }



                return SplitAndPadVersionString(original);
            }
            else
            {
                return SplitAndPadVersionString(Version.ToString());
            }
        }

        private static string[] SplitAndPadVersionString(string version)
        {
            string[] a = version.Split('.');
            if (a.Length == 4)
            {
                return a;
            }
            else
            {
                // if 'a' has less than 4 elements, we pad the '0' at the end 
                // to make it 4.
                var b = new string[4] { "0", "0", "0", "0" };
                Array.Copy(a, 0, b, 0, a.Length);
                return b;
            }
        }

        /// <summary>
        /// Parses a version string using loose semantic versioning rules that allows 2-4 version components followed by an optional special version.
        /// </summary>
        public static SemanticVersion Parse(string version)
        {
            if (String.IsNullOrEmpty(version))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "version");
            }

            SemanticVersion semVer;
            if (!TryParse(version, out semVer))
            {
                throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, NuGetResources.InvalidVersionString, version), "version");
            }
            return semVer;
        }

        /// <summary>
        /// Parses a version string using loose semantic versioning rules that allows 2-4 version components followed by an optional special version.
        /// </summary>
        public static bool TryParse(string version, out SemanticVersion value)
        {
            return TryParseInternal(version, _semanticVersionRegex, out value);
        }

        /// <summary>
        /// Parses a version string using strict semantic versioning rules that allows exactly 3 components and an optional special version.
        /// </summary>
        public static bool TryParseStrict(string version, out SemanticVersion value)
        {
            return TryParseInternal(version, _strictSemanticVersionRegex, out value);
        }

        private static bool TryParseInternal(string version, Regex regex, out SemanticVersion semVer)
        {
            semVer = null;
            if (String.IsNullOrEmpty(version))
            {
                return false;
            }

            var match = regex.Match(version.Trim());
            Version versionValue;
            if (!match.Success || !Version.TryParse(match.Groups["Version"].Value, out versionValue))
            {
                return false;
            }

            semVer = new SemanticVersion(NormalizeVersionValue(versionValue), match.Groups["Prerelease"].Value.TrimStart('-'), TryParseNumeric(match.Groups["PackageVersion"].Value.TrimStart('_')), version.Replace(" ", ""));
            return true;
        }

        private static int TryParseNumeric(string possibleNumeric)
        {
            if (string.IsNullOrWhiteSpace(possibleNumeric)) return 0;

            var numericalValue = 0;
            int.TryParse(possibleNumeric, out numericalValue);

            return numericalValue;
        }

        /// <summary>
        /// Attempts to parse the version token as a SemanticVersion.
        /// </summary>
        /// <returns>An instance of SemanticVersion if it parses correctly, null otherwise.</returns>
        public static SemanticVersion ParseOptionalVersion(string version)
        {
            SemanticVersion semVer;
            TryParse(version, out semVer);
            return semVer;
        }

        private static Version NormalizeVersionValue(Version version)
        {
            return new Version(version.Major,
                               version.Minor,
                               Math.Max(version.Build, 0),
                               Math.Max(version.Revision, 0));
        }

        public int CompareTo(object obj)
        {
            if (Object.ReferenceEquals(obj, null))
            {
                return 1;
            }
            SemanticVersion other = obj as SemanticVersion;
            if (other == null)
            {
                throw new ArgumentException(NuGetResources.TypeMustBeASemanticVersion, "obj");
            }
            return CompareTo(other);
        }

        public int CompareTo(SemanticVersion other)
        {
            if (Object.ReferenceEquals(other, null))
            {
                return 1;
            }

            int versionResult = Version.CompareTo(other.Version);
            if (versionResult != 0)
            {
                return versionResult;
            }

            int packageReleaseVersionResult = PackageReleaseVersion.CompareTo(other.PackageReleaseVersion);
            if (packageReleaseVersionResult != 0)
            {
                return packageReleaseVersionResult;
            }

            bool empty = String.IsNullOrEmpty(SpecialVersion);
            bool otherEmpty = String.IsNullOrEmpty(other.SpecialVersion);
            if (empty && otherEmpty)
            {
                return 0;
            }
            else if (empty)
            {
                return 1;
            }
            else if (otherEmpty)
            {
                return -1;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(SpecialVersion, other.SpecialVersion);
        }

        public static bool operator ==(SemanticVersion version1, SemanticVersion version2)
        {
            if (Object.ReferenceEquals(version1, null))
            {
                return Object.ReferenceEquals(version2, null);
            }
            return version1.Equals(version2);
        }

        public static bool operator !=(SemanticVersion version1, SemanticVersion version2)
        {
            return !(version1 == version2);
        }

        public static bool operator <(SemanticVersion version1, SemanticVersion version2)
        {
            if (version1 == null)
            {
                throw new ArgumentNullException("version1");
            }
            return version1.CompareTo(version2) < 0;
        }

        public static bool operator <=(SemanticVersion version1, SemanticVersion version2)
        {
            return (version1 == version2) || (version1 < version2);
        }

        public static bool operator >(SemanticVersion version1, SemanticVersion version2)
        {
            if (version1 == null)
            {
                throw new ArgumentNullException("version1");
            }
            return version2 < version1;
        }

        public static bool operator >=(SemanticVersion version1, SemanticVersion version2)
        {
            return (version1 == version2) || (version1 > version2);
        }

        public override string ToString()
        {
            return _originalString;
        }

        /// <summary>
        /// Returns the normalized string representation of this instance of <see cref="SemanticVersion"/>.
        /// If the instance can be strictly parsed as a <see cref="SemanticVersion"/>, the normalized version
        /// string if of the format {major}.{minor}.{build}[-{special-version}]. If the instance has a non-zero
        /// value for <see cref="Version.Revision"/>, the format is {major}.{minor}.{build}.{revision}[-{special-version}].
        /// </summary>
        /// <returns>The normalized string representation.</returns>
        public string ToNormalizedString()
        {
            if (_normalizedVersionString == null)
            {
                var builder = new StringBuilder();
                builder
                    .Append(Version.Major)
                    .Append('.')
                    .Append(Version.Minor)
                    .Append('.')
                    .Append(Math.Max(0, Version.Build));

                if (Version.Revision > 0)
                {
                    builder.Append('.')
                           .Append(Version.Revision);
                }

                if (PackageReleaseVersion != 0)
                {
                    builder.Append('_')
                           .Append(PackageReleaseVersion);
                }
                
                if (!string.IsNullOrEmpty(SpecialVersion))
                {
                    builder.Append('-')
                           .Append(SpecialVersion);
                }

                _normalizedVersionString = builder.ToString();
            }

            return _normalizedVersionString;
        }

        public bool Equals(SemanticVersion other)
        {
            return !Object.ReferenceEquals(null, other) &&
                   Version.Equals(other.Version) &&
                   PackageReleaseVersion.Equals(other.PackageReleaseVersion) &&
                   SpecialVersion.Equals(other.SpecialVersion, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            SemanticVersion semVer = obj as SemanticVersion;
            return !Object.ReferenceEquals(null, semVer) && Equals(semVer);
        }

        public override int GetHashCode()
        {
            int hashCode = Version.GetHashCode();
            if (PackageReleaseVersion != 0)
            {
                hashCode = hashCode * 123 + PackageReleaseVersion.GetHashCode();
            }
            if (SpecialVersion != null)
            {
                hashCode = hashCode * 4567 + SpecialVersion.GetHashCode();
            }

            return hashCode;
        }
    }
}
