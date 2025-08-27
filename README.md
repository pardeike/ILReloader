# ILReloader Demo

This repository is a playground for a library 'ILReloaderLib' that allows for hot reloading of changed mods. In order to develop and test it, a more complex setup is needed and is grouped here under one solution containing three projects:

- TestApplication
- ILReloaderLib
- TestMod

The idea is to create a minimal console application 'TestApplication' that will load a test mod 'TestMod' and run it to implement some features. The application uses the 'ILReloaderLib' to detect changes in the mods dll and the library will reload the changed dll and patch the annotated methods.

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
