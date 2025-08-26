# Hot reloading prototype
* This is the only AGENTS.md file in the repository
* You need .NET Framework 4.7.2 to run and test this solution. Use Mono.
* Whitespace rules are TAB, CRLF endings. Longer than usual lines (~ 140 chars are ok)
* Read the instructions in the root level README.md file. It explains in detail how to run the tests.
* Pay extra attention during testing that the main application stops at one point and reads a string from stdin.
* The mod dll is suppose to be edited *while* the main application is still running.
* The overall idea is to load the changed dll without triggering side effects and then translate all meta information from it into the applications space so it can properly replace the original IL with Harmony.