/*
    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Reflection;

namespace dnSpy.MainApp {
	/// <summary>
	/// Info about private (non-official) builds of this fork, see DnSpyPrivateBuild.targets
	/// </summary>
	static class PrivateBuildInfo {
		/// <summary>Label, eg. "rc1-private", or null if it's not a private build</summary>
		public static string? Label { get; } = GetMetadata("DnSpyPrivateBuildLabel");

		/// <summary>Version of dnSpyEx the build is based on, eg. "v6.6.0"</summary>
		public static string? BaseVersion { get; } = GetMetadata("DnSpyBaseVersion");

		/// <summary>Git commit the build was made from or null if it's not known</summary>
		public static string? Commit { get; } = GetMetadata("DnSpyBuildCommit");

		public static bool IsPrivateBuild => !string2.IsNullOrEmpty(Label);

		// Not localized: it's only used by private builds
		public static string Description =>
			$"Private build ({Label}) based on dnSpyEx {BaseVersion}. This is not an official dnSpyEx release.";

		public static string? CommitDescription => Commit is null ? null : $"Built from commit {Commit}";

		static string? GetMetadata(string key) {
			foreach (var attr in typeof(PrivateBuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()) {
				if (attr.Key == key)
					return string2.IsNullOrEmpty(attr.Value) ? null : attr.Value;
			}
			return null;
		}
	}
}
