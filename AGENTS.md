# Hot reloading prototype
  - This is the only AGENTS.md file in the repository
  - You need .NET Framework 4.7.2 and Mono (>=6.12)
  - Whitespace rules are TAB, CRLF endings. Lines up to ~140 chars are ok

## Setup:
  1. Run `dotnet build` to compile all projects

## Run & capture output:
  1. `mono Build/TestApplication.exe TestMod.dll | tee run.log`
  2. Wait for `Enter message (or 'bye' to exit):` and leave the app running

## Hot reload workflow:
  1. Edit `TestMod/ModDialog.cs` or another file under `TestMod`
  2. Rebuild the mod only: `dotnet build TestMod/TestMod.csproj -p:SolutionDir=/workspace/ILReloader/ | tee rebuild.log`
  3. Watch the running app for `[Info] reloading Mods/TestMod.dll`; continue entering messages to test the patched code

## Troubleshooting:
  - If no reload message appears, ensure the build overwrote `Mods/TestMod.dll`
  - `ReflectionTypeLoadException` means a dependency was not resolved; check the reloader’s `ReflectionOnlyAssemblyResolve` handler
  - Use `run.log` and `rebuild.log` for debugging; add `-v:n` to `dotnet build` for verbose output

## Notes
  - The mod dll is supposed to be edited *while* the main application is still running. ILReloaderLib contains file-watcher functions that *will* detect changed mod dlls.
  - The overall idea is to load the changed dll without triggering side effects and then translate all meta information from it into the application's space so Harmony can properly replace the original IL.
  - To speed up development cycles when working on reload code, it is enough to start the application (no need to enter a message), then simply touch the `Mods/TestMod.dll` file to trigger a reload.