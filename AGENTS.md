##### Coding Style

- Prefer R3 (reactive) rather than event handler.
- AOT compatible code for all projects.
- Treat `TouchChanX.UWP` as a normal .NET project: retain all UWP features but operate with zero sandbox restrictions.

##### Compile

- `TouchChanX.UWP` need to be compiled with the VS MSBuild. Other is fine with dotnet CLI.

##### UI/UX

- The application's interactions should prioritize tablet touch devices.
