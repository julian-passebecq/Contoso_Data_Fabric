# B00 baseline evidence summary

Observed on 2026-09-08 in `D:/PROJ/Contoso_Data_Fabric`, branch
`feat/csharp-fabric-desktop-v1`, HEAD `47a65b75e0ec126c0c8a0372a1c27709659aa317`.
The working tree includes original uncommitted stabilization changes listed in REGISTERS.md.

Build invocation:

```text
dotnet build ContosoDGV2.sln -c Release --no-restore
Build succeeded.
0 Warning(s)
0 Error(s)
Time Elapsed 00:00:49.82
```

Test invocation:

```text
dotnet test ContosoFabric.Core.Tests/ContosoFabric.Core.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=normal"
Test Run Successful.
Total tests: 31
Passed: 31
Total time: 2.8713 Seconds
```

This is a concise transcription of tool output observed by the lead; no original TRX was
captured in this session. It does not claim a fresh restore, CI success, real generation,
WPF interaction or live tenant execution. Later verification should retain raw results
under ignored generated/verification and commit a sanitized summary with candidate identity.

`source-hashes.json` records SHA-256 of current source/project/config/workflow files and
the pre-existing changed documentation to identify this dirty baseline. It excludes build
outputs, generated data/cache and these new planning files. Hashes identify bytes; they
are not a substitute for reviewing the diff or executing acceptance checks.
