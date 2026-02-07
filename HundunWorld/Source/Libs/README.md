# Third-Party Libraries

Please place any custom or private DLLs that are not available via NuGet in this directory.

## Required files:
- `Horizon.Game.Message.dll`: Required for network message definitions.

## NuGet Packages
Standard libraries (Arch, LiteDB, TouchSocket, etc.) are managed via `Game.Build.cs` using NuGet PackageReferences and will be restored automatically during the build.
