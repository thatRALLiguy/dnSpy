using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using dnSpy.Contracts.Utilities;

static class Program {
    static int Main() {
        var tests = new Action[] { RoundTrips, RejectsOversizedAllocation, RejectsTruncatedData,
            RejectsOverlongData, BoundsCompressedInput, RejectsLongStrings, SymbolsAndPaths,
            CancelDuringCopy, AtomicExtraction };
        foreach (var test in tests) {
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + ex); return 1; }
        }
        return 0;
    }

    static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Throws<T>(Action action) where T : Exception {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    // Independent fixtures encode the documented bundle layout, not the reader's implementation.
    static byte[] Fixture(uint version, byte[] data, bool compress = false, long? declaredSize = null,
        string name = "app.dll", bool pdb = false, long? compressedSize = null) {
        byte[] stored = data;
        if (compress) {
            using (var compressed = new MemoryStream()) {
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                    deflate.Write(data, 0, data.Length);
                stored = compressed.ToArray();
            }
        }
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
            writer.Write(new byte[128]);
            writer.Write(stored);
            long header = stream.Position;
            writer.Write(version); writer.Write(0U); writer.Write(pdb ? 2 : 1); writer.Write("fixture");
            if (version >= 2) writer.Write(new byte[40]);
            writer.Write(128L); writer.Write(declaredSize ?? data.LongLength);
            if (version >= 6) writer.Write(compress ? compressedSize ?? stored.LongLength : 0L);
            writer.Write((byte)BundleFileType.Assembly); writer.Write(name);
            if (pdb) {
                writer.Write(128L); writer.Write(data.LongLength);
                if (version >= 6) writer.Write(0L);
                writer.Write((byte)BundleFileType.Symbols); writer.Write("APP.pdb");
            }
            stream.Position = 0; writer.Write((byte)'M'); writer.Write((byte)'Z');
            stream.Position = 32; writer.Write(header);
            writer.Write(new byte[] { 0x8B,0x12,0x02,0xB9,0x6A,0x61,0x20,0x38,0x72,0x7B,0x93,0x02,0x14,0xD7,0xA0,0x32,
                0x13,0xF5,0xB9,0xE6,0xEF,0xAE,0x33,0x18,0xEE,0x3B,0x2D,0xCE,0x24,0xB3,0x6A,0xAE });
            return stream.ToArray();
        }
    }

    static BundleEntry Entry(byte[] bytes) => SingleFileBundle.TryRead(bytes)!.Entries[0];
    static void RoundTrips() {
        var data = Enumerable.Range(0, 200000).Select(i => (byte)i).ToArray();
        foreach (uint version in new uint[] { 1, 2, 6 }) {
            foreach (bool compressed in version == 6 ? new[] { false, true } : new[] { false }) {
                var entry = Entry(Fixture(version, data, compressed));
                Assert(entry.GetData().SequenceEqual(data));
                using (var output = new MemoryStream()) { entry.CopyTo(output); Assert(output.ToArray().SequenceEqual(data)); }
            }
        }
        Assert(Entry(Fixture(6, Array.Empty<byte>(), true)).GetData().Length == 0);
    }
    static void RejectsOversizedAllocation() {
        var entry = Entry(Fixture(6, new byte[1], true, (long)SingleFileBundle.MaxInMemoryEntrySize + 1));
        Throws<IOException>(() => entry.GetData());
    }
    static void RejectsTruncatedData() {
        var entry = Entry(Fixture(6, new byte[10], true, 11));
        Throws<IOException>(() => entry.CopyTo(Stream.Null));
    }
    static void RejectsOverlongData() {
        var entry = Entry(Fixture(6, new byte[10], true, 9));
        Throws<IOException>(() => entry.CopyTo(Stream.Null));
    }
    static void BoundsCompressedInput() {
        // Bytes following the declared compressed extent must not complete the stream.
        var entry = Entry(Fixture(6, new byte[10000], true, compressedSize: 1));
        Throws<IOException>(() => entry.CopyTo(Stream.Null));
    }
    static void RejectsLongStrings() {
        Assert(SingleFileBundle.TryRead(Fixture(6, new byte[1], name: new string('x', 32769))) is null);
    }
    static void SymbolsAndPaths() {
        var bundle = SingleFileBundle.TryRead(Fixture(6, new byte[1], pdb: true))!;
        Assert(bundle.FindSymbols(bundle.Entries[0]) == bundle.Entries[1]);
        Assert(Entry(Fixture(6, new byte[1], name: "../outside.dll")).GetSafeRelativePath() is null);
    }
    static void CancelDuringCopy() {
        var entry = Entry(Fixture(6, new byte[200000], true));
        using (var cancellation = new CancellationTokenSource())
        using (var output = new CancelingStream(cancellation))
            Throws<OperationCanceledException>(() => entry.CopyTo(output, cancellation.Token));
    }
    sealed class CancelingStream : MemoryStream {
        readonly CancellationTokenSource cancellation;
        public CancelingStream(CancellationTokenSource cancellation) => this.cancellation = cancellation;
        public override void Write(byte[] buffer, int offset, int count) {
            base.Write(buffer, offset, count); cancellation.Cancel();
        }
    }
    static void AtomicExtraction() {
        var dir = Path.Combine(Path.GetTempPath(), "dnSpy-bundle-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var filename = Path.Combine(dir, "app.dll");
            File.WriteAllText(filename, "original");
            var bad = Entry(Fixture(6, new byte[10], true, 11));
            Throws<IOException>(() => bad.ExtractToFile(filename, true));
            Assert(File.ReadAllText(filename) == "original");
            var good = Entry(Fixture(6, Encoding.UTF8.GetBytes("replacement"), true));
            Throws<IOException>(() => good.ExtractToFile(filename, false));
            Assert(File.ReadAllText(filename) == "original");
            Throws<OperationCanceledException>(() => good.ExtractToFile(filename, true, new CancellationToken(true)));
            Assert(File.ReadAllText(filename) == "original");
            good.ExtractToFile(filename, true);
            Assert(File.ReadAllText(filename) == "replacement");
            Assert(Directory.GetFiles(dir).Length == 1);
        }
        finally { Directory.Delete(dir, true); }
    }
}
