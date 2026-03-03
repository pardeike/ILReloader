# ILReloader

**ILReloaderLib** is a library that enables hot reloading of changed mod assemblies. It detects changes in mod DLLs, reloads them, and patches annotated methods automatically using [Harmony](https://github.com/pardeike/Harmony).

This repository also contains two supporting projects used for development and testing:

- **TestApplication** – a minimal console application that loads and runs a test mod
- **TestMod** – a sample mod used to verify the hot reload workflow

To run the solution:

- **Windows**: `Build/TestApplication.exe TestMod.dll`
- **Linux**:
  1. Install [Mono](https://www.mono-project.com/) and the .NET SDK.
  2. Run `dotnet build` to compile the projects.
  3. Execute `mono Build/TestApplication.exe TestMod.dll`.

A typical run has this output:
```
> .BuildTestApplication.exe TestMod.dll
[Info] Reloader starting...
[Info] Patch result: static System.Void TestApplication.App.RunLoop_Patch1(System.String modToLoad)
[Info] Reloader started
App started
Loading mod from: ModsTestMod.dll
[Info] loading ModsTestMod.dll
[Info] registered virtual System.Void TestMod.ModDialog::Show() for reloading [TestMod.ModDialog.Show()]
Found dialog: TestMod.ModDialog
Enter message (or 'bye' to exit):
```

It will wait for some test input and then continue in a loop:
```
hello
TEST 005
Showing mod dialog with message: hello
Enter message (or 'bye' to exit):
```

Now one should be able to edit the code in TestMod.ModDialog::Show and a build should replace the dll, which will automatically detected by ILReloaderLib and the reload and patching will happen:
```
[Info] reloading C:UsersBrrainzDesktopILReloaderModsTestMod.dll
// the input loop continues but with the new code

When working and debugging reload code, it is enough to start the test application and then touch the `Mods/TestMod.dll` file to trigger a reload. No need to enter a message.
```

ENJOY
/Brrainz
