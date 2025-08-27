# ILReloader

This library provides a framework for reloading .NET assemblies at runtime, allowing for dynamic updates to code without requiring a full application restart.

## API

### Reloader

The `Reloader` class is the main entry point for using the ILReloader library. It provides methods for watching directories, fixing assembly loading, and patching methods.

#### Methods

`Reloader.FixAssemblyLoading(MethodBase method)`
Finds and replaces occurrences of `Assembly.LoadFrom` in `method` with `Assembly.Load` to avoid in-use problems.

- `Reloader.Watch(string directory)`.
Starts watching the specified directory recursively for changes to DLL files. Can be called multiple times.

ENJOY
/Brrainz