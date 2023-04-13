Changelog
=========


v0.9.10
--------

This is a feature update release with this update:

* Collect copyright and VCS URL with the --with-details flag if available.


v0.9.9
-------

This is a minor bug fix release with this update:

* Try harder to return a download URL for a NuGet
  The NuGet API does not expose a way to get the downlaod URL, so we
  are fetching and probing in sequence a few places like the NuGet Client
  does internally.



v0.9.8
-------

This is a major feature update release with these updates and API breaking changes:

* Add new "--with-nuget-org" command line option to optionally include the
  nuget.org API feeds to the set of nuget.config-provided endpoints. Note that
  this is always included if there is no provided or available nuget.config

* Add extra debug tracing with the --debug option

* Do not create API or download URLs using a default. Only use API-provided values.

* Ensure we do not fail with a Package reference with no version.

* Ensure we do not fail when a package detailed metadata cannot be fetched when
  using the --with-details option.


v0.9.7
-------

This is a major feature update release with these updates and API breaking changes:

* Use the full tree of nuget.config files that exist, including any package
  mappings. See details on how to create nuget config files:

  * https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file
  * https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior

* Report and fail on errors. We now collect errors and warnings in the JSON
  output at the top level or at the dependency level and fail the execution if
  there is a problem reported.

* Add new dependency resolver that works best for modern PackageReference-based
  projects. 

* Remove the "--nuget-url" command line option to configure an alternative
  NuGet API URL. A nuget.config should be used instead, either the standard one,
  or a provided one with the proper NuGet API repository sources

* Add new "--with-details" command line option to optionally include package
  metadata details (such as checksum and size) when available.
  The SHA512 and size are no longer automatically provided. These are
  expensive API calls that are not used by NuGet as explained in this issue
  https://github.com/NuGet/NuGetGallery/issues/9433

* Add new "--with-fallback" command line option to optionally use a plain XML
  project file parser as fallback from failures." when parsing a project file
  using the NuGet and MSBuild APIs and this parsing fails. As a result the
  processing of some project file will fail without this option.

* Improve caching of NuGet API calls.

* Drop one-at-a-time dependency resolution which is mostly useless in practice.

* Use a cleaner build that does not include path to the original build machine
  in the binaries: we strip paths from the build now.


v0.9.6
-------

This is a feature update and bug fix release with these updates:

* Honor again PrivateAssets, IncludeAssets and ExcludeAssets flags of
  PackageReferences.

* Accept duplicated PackageReferences and use the first one the same way dotnet
  does it.


v0.9.5
-------

This is a major feature update release with these updates and API breaking changes:

* Remove nested "packages". Instead report only the "dependencies", nested as
  needed. Many processors return a flat list of dependencies. This is towards
  https://github.com/nexB/nuget-inspector/issues/24

* Resolve packages removing duplicates to fix 
  https://github.com/nexB/nuget-inspector/issues/23


v0.9.1
-------

This is a feature update release with these updates:

* Add package SHA512, size and update download URL to metadata
  We now collect the download URL from the API (as opposed to compute this
  from a template). We also collect the size. This is however about
  7 times slower (the test suite takes 7 times more to complete).

* We also use the registration URL for the API URL.

* Bump to use NuGet libraries 6.4.x


v0.9.0
-------

This is a major release with these updates:

* Correctly consider target framework conditions when processing project files.
  This was a bug that has been fixed and the fix has been validated against
  running the corresponding dotnet commands

* Correctly collect target framework(s) from a project file when there are more
  than one target, and when there are for legacy version references.

* Include the "project_framework" target framework moniker in the scan headers
  using the effective target framework used for the resolution, either the one
  provided at the command line of the default first framework.


v0.8.0
-------

This is a major release with these updates:

* Ignore prerelease in resolution. By default we should not includePrerelease
  and not includeUnlisted in the resolution. This should be a command line
  option in the future. (except for metadata where we fetch pre-release if requested)

* Respect the TargetFramework in packages.config. Test for framework compatibility
  between project and package and skip non-compatible packages.

* Correctly extract target framework from legacy project files

* Ensure that transitive dependencies are reported correctly

* Include keywords from tags

* Include owners as Parties and improve reporting of authors

* Ensure we correctly report dependency URLs and do not fail when Home URL is missing


v0.7.2
-------

This is a minor release with these updates:

* Add new command line options for --version and --about

* Ensure that we collect metadata for nested dependencies


v0.7.1
-------

This is a minor release to create proper release archives


v0.7.0
-------

This is a major release with extensive changes, including:

* Major changes to the output format. It is now flatter (now more package.package
  double nesting) and similar to the python-inspector and scancode-toolkit
  overall layout. This is not final

* Support for packagereference dependencies without a version or version range
* Addition of package metadata fetched from the NuGet API #2
* Improves support for target framework including adding a new CLI option #4
* Improve handling overall based on issues reported #3
* Overall code simplification and streamlining. Improved tracing.


v0.6.0
------

- Improve tests.


v0.5.0
------

- Initial release.
