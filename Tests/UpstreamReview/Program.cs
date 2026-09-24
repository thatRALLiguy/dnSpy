using System;
using System.IO;
using System.Threading.Tasks;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Text;
using dnSpy.Debugger.DbgUI;
using dnSpy.Documents.Tabs;

static class Program {
    static int Main() {
        var tests = new Action[] { IndependentOutputs, ParallelOutputs, TabsAndSpaces, RestoreExecutable,
            RestoreConnection, RestoredOptionsAreCloned };
        foreach (var test in tests) {
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + ex); return 1; }
        }
        return 0;
    }
    static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void IndependentOutputs() {
        var settings = new DecompilerOutputSettings(2, 4, false);
        using (var firstWriter = new StringWriter())
        using (var secondWriter = new StringWriter()) {
            var first = settings.CreateOutput(firstWriter);
            var second = settings.CreateOutput(secondWriter);
            first.IncreaseIndent();
            second.Write("root", BoxedTextColor.Text);
            Assert(secondWriter.ToString() == "root");
            first.Write("nested", BoxedTextColor.Text);
            Assert(firstWriter.ToString() == "  nested");
            // A canceled output can retain indentation; a later Save/Export must start at level zero.
            using (var retryWriter = new StringWriter()) {
                settings.CreateOutput(retryWriter).Write("retry", BoxedTextColor.Text);
                Assert(retryWriter.ToString() == "retry");
            }
        }
    }
    static void ParallelOutputs() {
        var settings = new DecompilerOutputSettings(3, 4, false);
        Parallel.For(0, 1000, i => {
            using (var writer = new StringWriter()) {
                var output = settings.CreateOutput(writer);
                int depth = i % 7;
                for (int level = 0; level < depth; level++) output.IncreaseIndent();
                output.Write("value", BoxedTextColor.Text);
                Assert(writer.ToString() == new string(' ', depth * 3) + "value");
                for (int level = 0; level < depth; level++) output.DecreaseIndent();
            }
        });
    }
    static void TabsAndSpaces() {
        using (var writer = new StringWriter()) {
            var output = new DecompilerOutputSettings(6, 4, true).CreateOutput(writer);
            output.IncreaseIndent(); output.Write("value", BoxedTextColor.Text);
            Assert(writer.ToString() == "\t  value");
        }
    }
    static void RestoreExecutable() {
        var page = Guid.NewGuid();
        var mru = new StartDebuggingOptionsMru();
        mru.RestoreLastOptions(new TestOptions { Arguments = "--saved", BreakKind = "EntryPoint" }, page, "app.exe");
        var match = mru.TryGetOptions("app.exe");
        Assert(match.HasValue && match.Value.pageGuid == page);
        Assert(((TestOptions)match!.Value.options).Arguments == "--saved");
        Assert(match.Value.options.BreakKind == "EntryPoint");
        Assert(mru.TryGetOptions("other.exe") is null);
    }
    static void RestoreConnection() {
        var mru = new StartDebuggingOptionsMru();
        var page = Guid.NewGuid();
        mru.RestoreLastOptions(new TestOptions { Arguments = "remote" }, page, null);
        Assert(mru.TryGetLastOptions()!.Value.pageGuid == page);
        Assert(mru.TryGetOptions("app.exe") is null);
    }
    static void RestoredOptionsAreCloned() {
        var original = new TestOptions { Arguments = "saved" };
        var mru = new StartDebuggingOptionsMru();
        mru.RestoreLastOptions(original, Guid.NewGuid(), "app.exe");
        original.Arguments = "changed";
        Assert(((TestOptions)mru.TryGetOptions("app.exe")!.Value.options).Arguments == "saved");
        Assert(((TestOptions)mru.TryGetLastOptions()!.Value.options).Arguments == "saved");
    }
    sealed class TestOptions : StartDebuggingOptions {
        public string? Arguments;
        public override DebugProgramOptions Clone() => (TestOptions)MemberwiseClone();
    }
}
