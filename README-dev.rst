# Getting started for development


- Install the .NET SDK 6.x from https://dotnet.microsoft.com/en-us/download/dotnet/6.0
- Install the VSCode from https://code.visualstudio.com/
- Install the extension: C# for Visual Studio Code (powered by OmniSharp) 
  from https://code.visualstudio.com/Docs/languages/csharp (v1.25.0 or higher)

To run the tests:

- Run build.sh to create a Linux build
- Run ./configure once to setup the Python evnvironment used for testing
- Run pytest with::

     venv/bin/pytest -vvs