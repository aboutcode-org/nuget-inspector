========================================================================
nuget-inspector - inspect nuget and .NET projects packages dependencies
========================================================================

Homepage: https://github.com/nexB/nuget-inspector and https://www.aboutcode.org/


``nuget-inspector`` is a utilities to:

- resolve .NET project nuget packages dependencies

- parse various project and package manifests and lockfiles such as .csproj files,
  and several related formats (including legacy formats)

- query NuGet.org APIs for package information to support dependency resolution

It grew out of the need to have a reliable way to analyze .NET code projects and
their dependencies independently of the availability of a dotnet SDK installed
on the machine that runs this analysis; and that could run on Linux, Windows and
macOS.

The goal of nuget-inspector is to be a comprehensive tool that can handle every
style of .NET and NuGet projects and package layouts, manifests and lockfiles.


    WARNING! this tool is under heavy development and its CLI options and output
    format are evolving quickly.


Usage
--------

- Download and extract the pre-built binary release archive from the release page
  https://github.com/nexB/nuget-inspector for your operating system. (Linux-only
  for now)

- Run the command line utility with::

    nuget-inspector --help

For instance, you can fetch nuget-inspector own project file at::

    https://raw.githubusercontent.com/nexB/nuget-inspector/main/src/nuget-inspector/nuget-inspector.csproj

And then run::

    nuget-inspector --project-file nuget-inspector.csproj --json nuget-inspector.json

And review the ``nuget-inspector.json`` JSON output file with its resolved dependencies.
Note that the output data structure is evolving and not final.



License
-------------

Copyright (c) nexB Inc. and others.
Copyright (c) the .NET Foundation, Microsoft and others.
Portions Copyright (c) 2018 Black Duck Software, Inc.
Portions Copyright (c) Mario Rivis https://github.com/dxworks

SPDX-License-Identifier: Apache-2.0 AND MIT


This project is based on, depends on or embeds several fine libraries and tools.
Here are credits for some of these key projects without which it would not exist::

- ``NuGet.Client``, ``MSBuild`` and ``upgrade-assistant`` from the .NET
  Foundation which are the core .NET tools and libraries to handled .NET and
  NuGet projects.
  https://github.com/NuGet/NuGet.Client/
  https://github.com/dotnet/msbuild/
  https://github.com/dotnet/upgrade-assistant

- ``nuget-dotnet5-inspector`` from Synopsys as forked by Mario Rivis 
  https://github.com/dxworks/nuget-dotnet5-inspector

- ``audit.net`` ``NugetAuditor`` and ``DevAudit`` from Sonatype
  https://github.com/sonatype-nexus-community/DevAudit/
  https://github.com/sonatype-nexus-community/audit.net

- ``build-info`` and ``nuget-deps-tree`` from JFrog
  https://github.com/jfrog/build-info
  https://github.com/jfrog/nuget-deps-tree/

- ``Component Detection`` and ``OSSGadget`` from Microsoft
  https://github.com/microsoft/component-detection/
  https://github.com/microsoft/OSSGadget

- ``cyclonedx-dotnet`` from the OWASP Foundation
  https://github.com/CycloneDX/cyclonedx-dotnet

- ``DependencyCheck`` from Jeremy Long
  https://github.com/jeremylong/DependencyCheck

- ``DependencyChecker`` from Fabrice Andréïs
  https://github.com/chwebdude/DependencyChecker

- ``dotnet-oudated`` from Jerrie Pelser and contributors
  https://github.com/dotnet-outdated/dotnet-outdated

- ``NugetDefense`` from Curtis Carter
  https://github.com/digitalcoyote/NuGetDefense

- ``snyk-nuget-plugin`` and ``dotnet-deps-parser`` from Snyk
  https://github.com/snyk/snyk-nuget-plugin
  https://github.com/snyk/dotnet-deps-parser

- ``verademo-dotnet`` and ``verademo-dotnetcore`` and from Veracode
  https://github.com/veracode/verademo-dotnet
  https://github.com/veracode/verademo-dotnetcore


These projects are used either in the built executables, at build time or for
testing. The built executables are designed to be self-contained exes that do
not require additional libraries to run on the target system.
