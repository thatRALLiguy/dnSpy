# Bundle regression checks

Run on Windows with the .NET 10 SDK and .NET Framework 4.8 installed:

```powershell
dotnet run --project Tests/SingleFileBundle/SingleFileBundle.Tests.csproj -c Release -f net10.0-windows
dotnet run --project Tests/SingleFileBundle/SingleFileBundle.Tests.csproj -c Release -f net48
```

The executable links the production bundle reader and exits nonzero on failure.
Fixtures cover bundle formats 1, 2 and 6; compressed and empty entries; oversized
allocations and strings; short and overlong decompressed data; compressed entry
boundaries; symbol lookup; traversal paths; cancellation; and atomic replacement.
GitHub CI runs these checks before packaging the corresponding runtime builds.

Entries larger than 256 MiB are rejected by `GetData()` before allocation. Use
`CopyTo()` or `ExtractToFile()` to extract large entries with a fixed-size buffer.
This is a per-entry allocation limit, not a total limit on loaded assemblies.
