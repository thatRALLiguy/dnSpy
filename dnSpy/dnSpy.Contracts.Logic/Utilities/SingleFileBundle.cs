/*
    Copyright (C) 2026 de4dot@gmail.com

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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using dnlib.DotNet;

namespace dnSpy.Contracts.Utilities {
	/// <summary>
	/// Type of a file stored in a .NET single-file bundle
	/// </summary>
	public enum BundleFileType : byte {
		/// <summary>Unknown file type</summary>
		Unknown,
		/// <summary>Managed assembly</summary>
		Assembly,
		/// <summary>Native binary</summary>
		NativeBinary,
		/// <summary>*.deps.json file</summary>
		DepsJson,
		/// <summary>*.runtimeconfig.json file</summary>
		RuntimeConfigJson,
		/// <summary>Symbol file (eg. a PDB file)</summary>
		Symbols,
	}

	/// <summary>
	/// A file stored in a .NET single-file bundle
	/// </summary>
	public sealed class BundleEntry {
		readonly SingleFileBundle bundle;

		/// <summary>Type of file</summary>
		public BundleFileType Type { get; }

		/// <summary>Path of the file relative to the application directory, as stored in the bundle. Don't use it
		/// as a filesystem path without sanitizing it, see <see cref="GetSafeRelativePath"/></summary>
		public string RelativePath { get; }

		/// <summary>Offset of the file data in the bundle</summary>
		public long Offset { get; }

		/// <summary>Size of the uncompressed file data</summary>
		public long Size { get; }

		/// <summary>Size of the compressed file data or 0 if it's not compressed</summary>
		public long CompressedSize { get; }

		/// <summary>true if the data is compressed</summary>
		public bool IsCompressed => CompressedSize != 0;

		/// <summary>
		/// true if this file could be a managed assembly. Bundles created by .NET Core 3.x don't store
		/// the file type so it's also true for all *.dll and *.exe files of unknown type. The caller must
		/// still verify that the file is a .NET file.
		/// </summary>
		public bool IsPossibleAssembly {
			get {
				if (Type == BundleFileType.Assembly)
					return true;
				if (Type != BundleFileType.Unknown)
					return false;
				return RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
					RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
			}
		}

		internal BundleEntry(SingleFileBundle bundle, BundleFileType type, string relativePath, long offset, long size, long compressedSize) {
			this.bundle = bundle;
			Type = type;
			RelativePath = relativePath;
			Offset = offset;
			Size = size;
			CompressedSize = compressedSize;
		}

		/// <summary>
		/// Reads and, if needed, decompresses the file data
		/// </summary>
		/// <returns></returns>
		public byte[] GetData() => bundle.ReadData(this);

		/// <summary>Copies the entry without buffering the entire file in memory.</summary>
		public void CopyTo(Stream destination, CancellationToken cancellationToken = default) =>
			bundle.CopyData(this, destination, cancellationToken);

		/// <summary>Extracts to a temporary file, replacing the destination only after a successful copy.</summary>
		public void ExtractToFile(string filename, bool overwrite, CancellationToken cancellationToken = default) {
			filename = Path.GetFullPath(filename);
			Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
			var temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try {
				using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					CopyTo(output, cancellationToken);
				cancellationToken.ThrowIfCancellationRequested();
				if (overwrite && File.Exists(filename))
					File.Replace(temporary, filename, null);
				else
					File.Move(temporary, filename);
			}
			finally {
				File.Delete(temporary);
			}
		}

		/// <summary>
		/// Gets <see cref="RelativePath"/> converted to a relative path that can't escape the directory it's
		/// combined with, or null if the path is invalid (eg. it contains '..' components)
		/// </summary>
		/// <returns></returns>
		public string? GetSafeRelativePath() {
			var invalidChars = Path.GetInvalidFileNameChars();
			var parts = new List<string>();
			foreach (var part in RelativePath.Split('/', '\\')) {
				if (part.Length == 0 || part == ".")
					continue;
				if (part == ".." || part.IndexOfAny(invalidChars) >= 0)
					return null;
				parts.Add(part);
			}
			if (parts.Count == 0)
				return null;
			return string.Join(Path.DirectorySeparatorChar.ToString(), parts.ToArray());
		}

		/// <inheritdoc/>
		public override string ToString() => $"{RelativePath} ({Type})";
	}

	/// <summary>
	/// Reads .NET single-file bundles (apps published with PublishSingleFile=true). The managed assemblies
	/// are stored after the native apphost and aren't visible to normal PE/metadata readers.
	/// </summary>
	public sealed class SingleFileBundle {
		// SHA-256 hash of ".net core bundle". It's stored in the apphost, right after the 64-bit header offset.
		static readonly byte[] bundleSignature = new byte[] {
			0x8B, 0x12, 0x02, 0xB9, 0x6A, 0x61, 0x20, 0x38,
			0x72, 0x7B, 0x93, 0x02, 0x14, 0xD7, 0xA0, 0x32,
			0x13, 0xF5, 0xB9, 0xE6, 0xEF, 0xAE, 0x33, 0x18,
			0xEE, 0x3B, 0x2D, 0xCE, 0x24, 0xB3, 0x6A, 0xAE,
		};
		const int MaxEntries = 0x100000;
		/// <summary>Maximum size of one entry loaded into memory. Larger entries can still be extracted.</summary>
		public const int MaxInMemoryEntrySize = 256 * 1024 * 1024;

		readonly Func<Stream> openStream;

		/// <summary>Major version of the bundle format (1 = .NET Core 3.x, 2 = .NET 5, 6 = .NET 6+)</summary>
		public uint MajorVersion { get; }

		/// <summary>Minor version of the bundle format</summary>
		public uint MinorVersion { get; }

		/// <summary>Bundle ID</summary>
		public string BundleId { get; }

		/// <summary>All files stored in the bundle</summary>
		public IReadOnlyList<BundleEntry> Entries => entries;
		readonly List<BundleEntry> entries;
		readonly Dictionary<string, BundleEntry> entriesByPath = new Dictionary<string, BundleEntry>(StringComparer.OrdinalIgnoreCase);

		SingleFileBundle(Func<Stream> openStream, uint majorVersion, uint minorVersion, string bundleId) {
			this.openStream = openStream;
			MajorVersion = majorVersion;
			MinorVersion = minorVersion;
			BundleId = bundleId;
			entries = new List<BundleEntry>();
		}

		/// <summary>
		/// Reads a bundle from a file. Returns null if it's not a bundle or if the bundle is corrupt.
		/// </summary>
		/// <param name="filename">Filename</param>
		/// <returns></returns>
		public static SingleFileBundle? TryRead(string filename) {
			if (!File.Exists(filename))
				return null;
			return TryRead(() => new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
		}

		/// <summary>
		/// Reads a bundle from memory. Returns null if it's not a bundle or if the bundle is corrupt.
		/// </summary>
		/// <param name="data">File data</param>
		/// <returns></returns>
		public static SingleFileBundle? TryRead(byte[] data) {
			if (data is null)
				throw new ArgumentNullException(nameof(data));
			return TryRead(() => new MemoryStream(data, false));
		}

		/// <summary>
		/// Finds the symbol file (*.pdb) of an assembly stored in this bundle or returns null if there's none
		/// </summary>
		/// <param name="assembly">Assembly entry</param>
		/// <returns></returns>
		public BundleEntry? FindSymbols(BundleEntry assembly) {
			if (assembly is null)
				throw new ArgumentNullException(nameof(assembly));
			var path = assembly.RelativePath;
			int index = path.LastIndexOf('.');
			if (index < 0 || path.IndexOfAny(new[] { '/', '\\' }, index) >= 0)
				return null;
			var pdbPath = path.Substring(0, index) + ".pdb";
			return entriesByPath.TryGetValue(pdbPath, out var entry) && entry != assembly ? entry : null;
		}

		/// <summary>
		/// Checks if an assembly is part of the .NET runtime or a Microsoft library. Self-contained bundles
		/// include the whole runtime and usually only the app's own assemblies are of interest.
		/// </summary>
		/// <param name="assembly">Assembly</param>
		/// <returns></returns>
		public static bool IsFrameworkAssembly(AssemblyDef? assembly) {
			if (assembly is null)
				return false;
			var token = PublicKeyBase.ToPublicKeyToken(assembly.PublicKeyOrToken);
			if (token is null || !frameworkPublicKeyTokens.Contains(token.ToString()))
				return false;
			var name = assembly.Name.String;
			return name == "mscorlib" || name == "netstandard" || name == "WindowsBase" ||
				name == "ReachFramework" || name == "Accessibility" || name == "DirectWriteForwarder" ||
				name.StartsWith("System", StringComparison.Ordinal) ||
				name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
				name.StartsWith("PresentationCore", StringComparison.Ordinal) ||
				name.StartsWith("PresentationFramework", StringComparison.Ordinal) ||
				name.StartsWith("UIAutomation", StringComparison.Ordinal);
		}
		static readonly HashSet<string> frameworkPublicKeyTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"b77a5c561934e089",
			"b03f5f7f11d50a3a",
			"7cec85d7bea7798e",
			"cc7b13ffcd2ddd51",
			"31bf3856ad364e35",
			"adb9793829ddae60",
		};

		static SingleFileBundle? TryRead(Func<Stream> openStream) {
			try {
				using (var stream = openStream()) {
					if (!IsExecutable(stream))
						return null;
					stream.Position = 0;
					long headerOffset = FindHeaderOffset(stream);
					if (headerOffset <= 0 || headerOffset >= stream.Length)
						return null;
					stream.Position = headerOffset;
					using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
						return ReadHeader(openStream, reader, stream.Length);
				}
			}
			catch (IOException) {
			}
			catch (UnauthorizedAccessException) {
			}
			catch (NotSupportedException) {
			}
			catch (ArgumentException) {
			}
			catch (FormatException) {
			}
			return null;
		}

		static SingleFileBundle? ReadHeader(Func<Stream> openStream, BinaryReader reader, long length) {
			uint majorVersion = reader.ReadUInt32();
			uint minorVersion = reader.ReadUInt32();
			int numEntries = reader.ReadInt32();
			if (majorVersion == 0 || majorVersion > 0xFF || numEntries < 0 || numEntries > MaxEntries)
				return null;
			var bundleId = ReadBoundedString(reader);
			if (majorVersion >= 2) {
				// deps.json offset + size, runtimeconfig.json offset + size, flags
				reader.ReadInt64();
				reader.ReadInt64();
				reader.ReadInt64();
				reader.ReadInt64();
				reader.ReadUInt64();
			}

			var bundle = new SingleFileBundle(openStream, majorVersion, minorVersion, bundleId);
			for (int i = 0; i < numEntries; i++) {
				long offset = reader.ReadInt64();
				long size = reader.ReadInt64();
				long compressedSize = majorVersion >= 6 ? reader.ReadInt64() : 0;
				var type = (BundleFileType)reader.ReadByte();
				var relativePath = ReadBoundedString(reader);

				long storedSize = compressedSize != 0 ? compressedSize : size;
				if (offset < 0 || size < 0 || compressedSize < 0 || storedSize > length || offset > length - storedSize)
					return null;
				var entry = new BundleEntry(bundle, type, relativePath, offset, size, compressedSize);
				bundle.entries.Add(entry);
				if (!bundle.entriesByPath.ContainsKey(relativePath))
					bundle.entriesByPath.Add(relativePath, entry);
			}
			return bundle;
		}

		static string ReadBoundedString(BinaryReader reader) {
			// BinaryWriter strings have a 7-bit encoded byte length. Bound it before allocating.
			uint length = 0;
			for (int shift = 0; shift < 35; shift += 7) {
				byte value = reader.ReadByte();
				if (shift == 28 && value > 7)
					throw new FormatException("Invalid bundle string length");
				length |= (uint)(value & 0x7F) << shift;
				if (length > 32768)
					throw new FormatException("Bundle string is too long");
				if ((value & 0x80) == 0)
					return Encoding.UTF8.GetString(ReadExactly(reader.BaseStream, (int)length));
			}
			throw new FormatException("Invalid bundle string length");
		}

		// The apphost is a PE (Windows), ELF (Linux) or Mach-O (macOS) file. Checking it first avoids reading
		// all of the file when it's not a bundle.
		static bool IsExecutable(Stream stream) {
			var header = new byte[4];
			if (ReadAtMost(stream, header, 0, header.Length) != header.Length)
				return false;
			if (header[0] == 'M' && header[1] == 'Z')
				return true;
			uint magic = BitConverter.ToUInt32(header, 0);
			switch (magic) {
			case 0x464C457F:	// ELF
			case 0xFEEDFACE:	// Mach-O 32-bit
			case 0xFEEDFACF:	// Mach-O 64-bit
			case 0xCEFAEDFE:	// Mach-O 32-bit, reversed byte order
			case 0xCFFAEDFE:	// Mach-O 64-bit, reversed byte order
				return true;
			default:
				return false;
			}
		}

		static long FindHeaderOffset(Stream stream) {
			const int bufferSize = 0x10000;
			int sigLen = bundleSignature.Length;
			var buffer = new byte[bufferSize + sigLen + 8];
			// Number of bytes kept from the previous read so a signature spanning two reads is still found.
			// It includes the 8 bytes before the signature (the header offset).
			int keep = 0;
			while (true) {
				int read = ReadAtMost(stream, buffer, keep, bufferSize);
				if (read == 0)
					return 0;
				int end = keep + read;
				for (int i = 8; i + sigLen <= end; i++) {
					if (buffer[i] != bundleSignature[0])
						continue;
					if (!IsSignatureAt(buffer, i))
						continue;
					return BitConverter.ToInt64(buffer, i - 8);
				}
				keep = Math.Min(end, sigLen + 8 - 1);
				Array.Copy(buffer, end - keep, buffer, 0, keep);
			}
		}

		static bool IsSignatureAt(byte[] buffer, int index) {
			for (int j = 0; j < bundleSignature.Length; j++) {
				if (buffer[index + j] != bundleSignature[j])
					return false;
			}
			return true;
		}

		static int ReadAtMost(Stream stream, byte[] buffer, int offset, int count) {
			int total = 0;
			while (total < count) {
				int read = stream.Read(buffer, offset + total, count - total);
				if (read <= 0)
					break;
				total += read;
			}
			return total;
		}

		internal byte[] ReadData(BundleEntry entry) {
			if (entry.Size > MaxInMemoryEntrySize)
				throw new IOException($"Bundle file is too big to load into memory: {entry.RelativePath}. Extract it to disk instead.");
			var data = new byte[(int)entry.Size];
			using (var destination = new MemoryStream(data, true))
				CopyData(entry, destination, CancellationToken.None);
			return data;
		}

		internal void CopyData(BundleEntry entry, Stream destination, CancellationToken cancellationToken) {
			if (destination is null)
				throw new ArgumentNullException(nameof(destination));
			cancellationToken.ThrowIfCancellationRequested();
			using (var stream = openStream()) {
				stream.Position = entry.Offset;
				using (var bounded = new EntryStream(stream, entry.IsCompressed ? entry.CompressedSize : entry.Size)) {
					if (entry.IsCompressed) {
						using (var deflate = new DeflateStream(bounded, CompressionMode.Decompress))
							CopyExactly(deflate, destination, entry.Size, cancellationToken);
					}
					else
						CopyExactly(bounded, destination, entry.Size, cancellationToken);
				}
			}
		}

		static void CopyExactly(Stream source, Stream destination, long size, CancellationToken cancellationToken) {
			var buffer = new byte[65536];
			while (size > 0) {
				cancellationToken.ThrowIfCancellationRequested();
				int read = source.Read(buffer, 0, (int)Math.Min(size, buffer.Length));
				if (read == 0)
					throw new IOException("Unexpected end of bundle data");
				destination.Write(buffer, 0, read);
				size -= read;
			}
			cancellationToken.ThrowIfCancellationRequested();
			if (source.ReadByte() != -1)
				throw new IOException("Bundle data exceeds its declared size");
		}

		sealed class EntryStream : Stream {
			readonly Stream source;
			long remaining;
			public EntryStream(Stream source, long length) {
				this.source = source;
				remaining = length;
			}
			public override int Read(byte[] buffer, int offset, int count) {
				int read = source.Read(buffer, offset, (int)Math.Min(count, remaining));
				remaining -= read;
				return read;
			}
			public override bool CanRead => true;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
			public override void Flush() { }
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}

		static byte[] ReadExactly(Stream stream, int size) {
			var data = new byte[size];
			if (ReadAtMost(stream, data, 0, size) != size)
				throw new IOException("Unexpected end of bundle data");
			return data;
		}
	}
}
