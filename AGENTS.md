\# Hot reloading prototype

\* This is the only AGENTS.md file in the repository

\* You need .NET Framework 4.7.2 and Mono (>=6.12)

\* Whitespace rules are TAB, CRLF endings. Lines up to ~140 chars are ok

\* Setup:

&nbsp;	1. Run `dotnet build` to compile all projects

\* Run \& capture output:

&nbsp;	1. `mono Build/TestApplication.exe TestMod.dll | tee run.log`

&nbsp;	2. Wait for `Enter message (or 'bye' to exit):` and leave the app running

\* Hot reload workflow:

&nbsp;	1. Edit `TestMod/ModDialog.cs` or another file under `TestMod`

&nbsp;	2. Rebuild the mod only: `dotnet build TestMod/TestMod.csproj -p:SolutionDir=/workspace/ILReloader/ | tee rebuild.log`

&nbsp;	3. Watch the running app for `\[Info] reloading Mods/TestMod.dll`; continue entering messages to test the patched code

\* Troubleshooting:

&nbsp;	- If no reload message appears, ensure the build overwrote `Mods/TestMod.dll`

&nbsp;	- `ReflectionTypeLoadException` means a dependency was not resolved; check the reloader’s `ReflectionOnlyAssemblyResolve` handler

&nbsp;	- Use `run.log` and `rebuild.log` for debugging; add `-v:n` to `dotnet build` for verbose output

\* The mod dll is supposed to be edited \*while\* the main application is still running. ILReloaderLib contains file-watcher functions that \*will\* detect changed mod dlls.

\* The overall idea is to load the changed dll without triggering side effects and then translate all meta information from it into the application's space so Harmony can properly replace the original IL.



