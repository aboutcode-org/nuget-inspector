=======================================================
  NuGet and .Net package dependencies resolver design
=======================================================


This is a design to create a new command line tool to resolve NuGet and
Microsoft .NET package dependencies using the same approach as native tools but
without actually building the full software stack and without dependencies
on Windows and the ability to select a specific base .NET framework version.
The name for this new tool is “nuget-inspector”.


***************
Context
***************

Per NugGet.org:
    "NuGet is the package manager for .NET. The NuGet client tools provide the
    ability to produce and consume packages. The NuGet Gallery is the central
    package repository used by all package authors and consumers."

To collect the set of dependent packages of a .NET project, the package
management tools, build tools or IDE -- typically nuget, msbuild and Visual
Studio or VSCode will collect the first level of declared dependencies
and then query the NuGet Gallery APIs aka. "feed" to collect the dependencies of
each dependency.

Each dependency is identified by its name, with a version, allowed 
versions and target .NET framework conditions either required to install and run
a package or defining a subset of framwork version-specific dependencies.


***************
Problem
***************

NuGet dependency resolution is complex short of running a complete build
using an environment specially-configured for a given NuGet and Visual
Studio project. When running an analysis task on a Linux, this is a task further
complicated by the non-Windows operating system.

From https://martinbjorkstrom.com/posts/2018-09-19-revisiting-nuget-client-libraries :

    There's more than one way to skin a cat. But even more ways to install NuGet packages.
    -- Anonymous


Some of the key issues are listed below.


There are multiple manifest file formats:
-----------------------------------------------

- "<ProjectName>.sln" files VisualStudio "solutions" and 
  "<ProjectName>.csproj" or ".*proj" VisualStudio "project files". 
  Project files use version ranges for "package references" and no pinned versions
  with different resolution strategies based on NuGet versions. 
  Project files PackageReference can also define complex nested "Condition" using
  properties that are resolved dynamically such as evaluating a target .NET framework
  requirement for a dependency or having different dependencies for a given .NET
  framework version. The "PackageReference" format used here is designed to
  eventually subsume other formats and data structures.
  A project file "PackageReference" can designate some reference content as
  "PrivateAssets" when used only in development. There are also provisions to
  IncludeAssets and ExcludeAssets.

- .nuspec files when the project is designed itself to be a NuGet. They use the
  same definitions as project files.

- packages.config files have both a version pinned and allowedVersions range
  used only for certain update operations. They are essentially a "lockfile".
  This is legacy but still supported.

- project.assets.json (transient file) and packages.lock.json are lock-style
  files used with project files. This is legacy.

- project.json (in NuGet 3.x+) contains a list of dependent packages. 
  It replaces the older packages.config (which is still supported) and is
  replaced by PackageReference in NuGet 4.0+. This is legacy.

- nuget.config stores key configuration data.

- "<ProjectName>.deps.json" files contain details for resolved package and framework
  dependencies. This is machine-generated during the build and these files may
  exist only with the "PreserveCompilationContext" project property set.


NuGet has complex version notations and semantics and mixed dependency resolution strategies:
----------------------------------------------------------------------------------------------

- Version use semver or not, or a modified semver version syntax. Most use four
  dot-separated version segments that are not compliant with Semver. Semver 2.0
  is supported with Nuget 4.3.0 and up.

- NuGet version 2 and 3 use different approaches to normalize and compare versions.

- The version range notation can also combine a resolution strategy:

  - 1.0: the default single version is a in fact a minimum version with NuGet 3.x
  - [1.0] is for an exact version
  - [1.0,2.0) are ranges with inclusive and exclusive bounds
  - a star "*" means picking the highest version matching this pattern
    as in 1.1.* (aka. Floating versions) and this for any segments of the version.
    These are not supported in the "packages.config" files.
  - pre-release, rc and beta suffixes impact the version ordering and have
    varying level of support before or after NuGet 4.30


NuGet has complex resolution strategies that depend on the type of manifest used, tool and version
-------------------------------------------------------------------------------------------------------

When using PackageReference in project files, these rules are used:
- **Lowest version**: the default in latest releases of NuGet.

- **Highest version**: aka. floating versions (using the star notation explained above)

- **Nearest wins**: when multiple compatible version range constraints apply, the
  one that closest to the root wins (e.g. the one that's the most "direct"
  dependecy definition).

- **Cousin dependencies**: when there are multiple compatible version range
  constraints that are at the same depth in "nearest win", then use the lowest
  version that satisfies all constraints.

When using packages.config, NuGet looks at all versions at once and picks
the lowest major.minor version

The semantics of dependency resolution are changing with tooling versions:
there are notable differences between NuGet 2, 3 and 4 on the resolution
approach and versions syntaxes and meaning.

- without a version or version range, until NuGet 2.8, NuGet picked the
  latest available package version, but NuGet 3 and up picks the lowest package
  version that satisfies the constraints.
- Semver 2.0 is supported only in NuGet 4.3 and up.


See these documentations and articles for more details:

- https://docs.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
- https://docs.microsoft.com/en-us/nuget/archive/project-json
- https://docs.microsoft.com/en-us/nuget/reference/nuspec
- https://docs.microsoft.com/en-us/nuget/concepts/package-versioning
- https://docs.microsoft.com/en-us/nuget/concepts/dependency-resolution
- https://codeopinion.com/nuget-packagereference-versions-solution-wide/
- https://codeopinion.com/migrating-to-sdk-csproj/
- https://www.jerriepelser.com/blog/analyze-dotnet-project-dependencies-part-1/
- https://www.jerriepelser.com/blog/analyze-dotnet-project-dependencies-part-2/
- https://fossa.com/blog/managing-dependencies-net-csproj-packagesconfig/
- https://www.mytechramblings.com/posts/centrally-manage-nuget-versions/
- https://martinbjorkstrom.com/posts/2018-09-19-revisiting-nuget-client-libraries
- https://github.com/NuGet/docs.microsoft.com-nuget/blob/49e53388c117a5eed2c6947aeac714a5b8e9d143/docs/concepts/Dependency-Resolution.md
- https://github.com/skijit/notes/blob/master/programming/dotnet/nuget.md
- package references: https://docs.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#controlling-dependency-assets
- deps.json: https://github.com/dotnet/sdk/blob/main/documentation/specs/runtime-configuration-file.md#appnamedepsjson
  See for instance https://github.com/microsoft/bion/blob/012b2292acd941ccb4a92e6aa646d688d389d96d/csharp/BSOA/BSOA.FromJschema/ref/Microsoft.Json.Schema.deps.json


***************
Solution
***************

One approach is to attempt resolving versions by parsing manifests
such as project files PackageReference and then perform a resolution using Nuget
API calls and a simple lowest version. This can work for simpler cases but cannot
handle more complex cases when faced with some of the issues detailed above such
as supporting target .NET frameworks, multiple versions of NuGet, cousin, nearest 
or highest versions and version conflict resolution and backtracking. 

For instance this is the approach used in ORT combined with minimal property
resolution: this is essentially a rewrite of subset of NuGet and .NET
dependency resolution and it proved to be incomplete in practice.

The solution approach designed here is instead to create a NuGet dependencies 
resolution client that is using the native libraries and code of NuGet and .NET
themselves to ensure that the eventually complex resolution process applied is
the same that is used by NuGet and .NET because it uses the same underlying code.
This will eventually request resolution of dependencies for a target .NET
framework and operating system and architectures that may not be the current
.NET version.

The proposed solution will be a new repository and command line tool
that can be installed to resolve dependencies from .NET projects for any
provided .NET target framework and OS/architecture as an argument
(which may not be the same as the installed .NET runtime version). The output
will be a JSON file listing the resolved dependencies in two ways:

1. as a flat list of unique name/versions (using Package URLs)

2. as a nested dependency tree, with possible duplicates because a given
   name/version may be the dependency of more than one packages

This is essentially the same design as for the related python-inspector project.

For instance, if we have these immediate direct dependencies (using
exact versions for easier illustration):

-  foo 1.0 and bar 2.0
-  foo 1.0 depends in turn on baz 2.0 and thing 3.0
-  bar 2.0 depends in turn on shebang 1.0 and thing 3.0

Then complete dependency list (including duplicates) is:

-  foo 1.0
-  bar 2.0
-  baz 2.0
-  thing 3.0
-  shebang 1.0
-  thing 3.0

And the dependency tree is:

-  foo 1.0

   -  baz 2.0
   -  thing 3.0

-  bar 2.0

   -  shebang 1.0
   -  thing 3.0

And a flat list of unique dependencies would be:

-  foo 1.0
-  bar 2.0
-  baz 2.0
-  thing 3.0
-  shebang 1.0

The implementation will borrow and resue various existing utilities and open
source libraries and will rely on the actual NuGet and .NET libraries for core
work. For simplification the supported .NET runtime will be constrained
to a recent version such as 5 or 6.

The expected benefit of this tool is a correct way to resolve NuGet .NET dependencies
that will not require a prior installation of a .NET toolchain specific to a
given project environment when the goal is only to resolve dependencies. In
particular the key capability is to run this tool to obtain proper results
when targeting a certain .NET framework version for Windows even though the tool
would run on Linux and resolve properly the properties and conditions
found in .NET project files and honor the more advanced and complex NuGet
dependency conflict resolution rules and this without having to install all the
packages from the dependency tree.


***************
Design
***************

Processing outline
------------------

The outline of the processing is to:

- Parse and validate a project or solution file as input. For a solution file
  collect all referenced projects.

- For each project file:

  - Determine the best strategy to collect dependdencies based on available
    manifests and lockfiles.

  - Used either locked versions or resolve versions using the NuGet API

-  Dump the results as JSON


User experience:
----------------

The goal of the command line interface and user experience is to be
obvious and familiar to a command line user and adopt the same look and feel
as the python-inspector.

Create a new CLI with these key options:

Inputs:
~~~~~~~~~

We use one option to determine what are the input projects to resolve:

-  ``--project-file <.sln solution or .csproj project file>``: a path to a .sln solution or project file.


Environment:
~~~~~~~~~~~~

Two options to select the target .NET Framework and OS/architecture to use for
dependency resolution:

- ``--target-framework <framework short codename>``: the .NET framework to use
  for dependency resolution.

- ``--runtime <os>`` : The target runtime id (e.g. OS and architecture) to use
  using short codenames.

Notes: the assumption is that we will only support X86/64 architectures on
Linux for now. We will refine this later with support for other OS and architectures.


Configuration:
~~~~~~~~~~~~~~

One option to point to alternative, local or private NuGet indexes and
repositories.

-  ``--repository-url URL``: NuGet source repository API URL to use for packages
   data and files. The default is to use the public
   NuGet Gallery repository.A source must support the V3 API protocol.


Strategy and error processing:
~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~

The initial approach is to use the NuGet 4.x+ default dependency resolution
strategy which combines the lowest versions/highest versions/nearest/cousin
approaches explained above.

This strategy is strict and may fail to resolve certain dependencies that
would be otherwise correct and installable - i.e., the same way NuGet would fail.


Output:
~~~~~~~

One option to point to JSON output file to create

-  ``--json FILE``: Write output as pretty-printed JSON to FILE.

The JSON output will be a JSON "object" of name/value pairs with:

1. a "headers" list of objects with technical information on the command
   line run options, inputs and arguments (similar to ScanCode Toolkit
   headers). This will include an "errors" list of error messages if any.

2. a "dependencies" list of objects as a flat list of unique
   name/versions (using Package URLs) listing all dependencies at full
   depth.

-   We can later consider adding extra data such as: package medatada
    and the list of actual downloadable archive URLs for each package

3. a "dependency_tree" combination of nested lists and objects to
   represent the resolved dependencies in a tree the "root" notes in
   this tree are the requirements and specifiers provided as input (e.g.
   assumed to be direct dependencies) (with possible duplicates because
   a given name/version may be the dependency of more than one packages)
