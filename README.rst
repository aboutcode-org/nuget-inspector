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


Its companion libraries are:

- ``.NET`` proper. This is based on the latest .NET 6.

- ``NuGet.Client``, which is the core library and command tool for NuGet proper.

- ``MSBuild``, the .NET tools and libraries for building .NET and NuGet projects.

These are included in the built executables that are designed to be self-contained
standalone exes that do not require additional libraries on the target system.
