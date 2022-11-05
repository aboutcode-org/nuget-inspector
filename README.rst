================================
nuget-inspector - inspect nuget and .NET projects packages dependencies and metadata
================================


Copyright (c) nexB Inc. and others.
SPDX-License-Identifier: Apache-2.0 AND MIT
Homepage: https://github.com/nexB/nuget-inspector and https://www.aboutcode.org/


``nuget-inspector`` is a collection of utilities to:

- resolve .NET project nuget packages dependencies

- parse various project and package manifests and lockfiles such as .csproj files,
  and several related formats
  
- query NuGet.org APIs for package information to support dependency resolution

It grew out of the need to have a reliable way to analyze .NET code projects and
their dependencies independently of the availability of a dotnet SDK installed
on the machine that runs the analysis; and that could run on Linux, Windows and
macOS.

The goal of nuget-inspector is to be a comprehensive tool that can handle every
style of .NET and NuGet projects and package layouts, manifests and lockfiles.


Usage
--------

- Install pre-built binaries from the release page https://github.com/nexB/nuget-inspector
  for your operating system.

- Run the command line utility with::

    nuget-inspector --help


This project is based on, depends on or embeds several fine libraries and tools.
Here are the some of the key libraries used::

- ``NuGet.Client`` by the .NET Foundation which is the core library and command
  tool for NuGet proper.
  https://github.com/NuGet/NuGet.Client/

- ``MSBuild`` and ``upgrade-assistant`` by the .NET Foundation which are the
  .NET tools and libraries for building .NET and NuGet projects and tools to
  upgrade them
  https://github.com/dotnet/msbuild/
  https://github.com/dotnet/upgrade-assistant

- ``nuget-dotnet5-inspector`` by Synopsys as forked by Mario Rivis 
  https://github.com/dxworks/nuget-dotnet5-inspector

- ``snyk-nuget-plugin`` and ``dotnet-deps-parser`` by Snyk which are NuGet
  manifests parsing libraries and tools.
  https://github.com/snyk/snyk-nuget-plugin
  https://github.com/snyk/dotnet-deps-parser
  
- ``dotnet-oudated`` by Jerrie Pelser and contributors
  https://github.com/dotnet-outdated/dotnet-outdated

- ``DependencyChecker`` by Fabrice Andréïs
  https://github.com/chwebdude/DependencyChecker

- ``build-info`` and ``nuget-deps-tree`` by JFrog
  https://github.com/jfrog/build-info
  https://github.com/jfrog/nuget-deps-tree/

- ``cyclonedx-dotnet`` by the OWASP Foundation
  https://github.com/CycloneDX/cyclonedx-dotnet
  
- ``DependencyCheck`` by Jeremy Long
  https://github.com/jeremylong/DependencyCheck


These are used either in the built executables, at build time or for testing.
The built executables are designed to be self-contained standalone exes that do
not require additional libraries on the target system.
