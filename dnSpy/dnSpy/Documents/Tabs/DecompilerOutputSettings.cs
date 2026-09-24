using System.IO;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace dnSpy.Documents.Tabs {
	// Immutable UI-thread snapshot. Output writers run concurrently and must not share an Indenter.
	readonly struct DecompilerOutputSettings {
		readonly int indentSize;
		readonly int tabSize;
		readonly bool useTabs;

		public DecompilerOutputSettings(int indentSize, int tabSize, bool useTabs) {
			this.indentSize = indentSize;
			this.tabSize = tabSize;
			this.useTabs = useTabs;
		}

		public IDecompilerOutput CreateOutput(TextWriter writer) =>
			new TextWriterDecompilerOutput(writer, new Indenter(indentSize, tabSize, useTabs));
	}
}
