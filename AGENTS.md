##### Coding Style

- AOT compatible code for all projects.
- Always use CRLF as line endings after modified. 
- Treat `TouchChanX.UWP` as a full-trust .NET project (net10.0-windows10.0.26100.0): retain all UWP features but operate with zero sandbox restrictions.

##### Compile

- `TouchChanX.UWP` need to be compiled with the VS MSBuild. Other is fine with dotnet CLI.

##### UI/UX

- The application's interactions should prioritize tablet touch devices.
