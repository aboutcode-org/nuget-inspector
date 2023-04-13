#
# Copyright (c) nexB Inc. and others. All rights reserved.
# ScanCode is a trademark of nexB Inc.
# SPDX-License-Identifier: Apache-2.0
# See http://www.apache.org/licenses/LICENSE-2.0 for the license text.
# See https://github.com/nexB/nuget-inspector for support or download.
# See https://aboutcode.org for more information about nexB OSS projects.
#

import json
import subprocess
from pathlib import Path

import pytest
from commoncode.testcase import FileDrivenTesting

from testing_utils import REGEN_TEST_FIXTURES
from testing_utils import ROOT_DIR
from testing_utils import TEST_DATA_DIR

"""
A data-driven test suite with a number of project manifests and lockfiles
that represent a variety of cases.
"""

test_env = FileDrivenTesting()
test_env.test_data_dir = str(TEST_DATA_DIR)

NUGET_INSPECTOR = str(ROOT_DIR / "build" / "nuget-inspector")

# These test paths are failing for now and need some TLC
failing_paths = tuple([
    "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/projectRootTestDir/projectAssetsDir/another_example.csproj",
    "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/projectRootTestDir/packagesConfigDir/example.csproj",
    "complex/thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/net462/DependencyChecker.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-empty-manifest/empty-manifest.csproj",
    "project-json/datatables/datatables.aspnet-68483b7/src/DataTables.AspNet.Extensions.DapperExtensions.Tests/DataTables.AspNet.Extensions.DapperExtensions.Tests.xproj",

    # invalid XML
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-invalid-manifest/invalid.csproj",

    # downgrade
    "nuget-config/myget-and-props/Configuration-608dd8/src/Steeltoe.Extensions.Configuration.CloudFoundryBase/Steeltoe.Extensions.Configuration.CloudFoundryBase.csproj",

    # missing file in the project
    "complex/end-to-end3/Newtonsoft.Json-10.0.1/Doc/doc.shfbproj",
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/MauiSample/droid/Original/EwDavidForms/EwDavidForms.iOS/EwDavidForms.iOS.csproj",
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/MauiSample/droid/Original/EwDavidForms/EwDavidForms.Android/EwDavidForms.Android.csproj",
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/MauiSample/droid/Upgraded/EwDavidForms/EwDavidForms.iOS/EwDavidForms.iOS.csproj",
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/MauiSample/ios/Original/EwDavidForms/EwDavidForms.iOS/EwDavidForms.iOS.csproj",
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/MauiSample/ios/Original/EwDavidForms/EwDavidForms.Android/EwDavidForms.Android.csproj",
    "complex/end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.Portable.csproj",

    # invalid csproj file
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/Integration.Tests.csproj",

    # TODO: This is using central package dependencies?
    "complex/component-detection/component-detection-2a128f6/src/Microsoft.ComponentDetection.Common/Microsoft.ComponentDetection.Common.csproj",
])

# These test paths are supposed to have an error returned by design with an output
paths_expected_to_return_an_error_and_output = set([
    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/dotnet_project/dotnet_project.csproj",
    "complex/end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.Stress/Microsoft.AspNet.SignalR.Stress.csproj",
    # has a circular dependency
    "complex/end-to-end5/component-detection-1.4.1/src/Microsoft.ComponentDetection.Contracts/Microsoft.ComponentDetection.Contracts.csproj",

    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/no_csproj/no_csproj.vbproj",

    # to sort
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/MauiSample/ios/Upgraded/EwDavidForms/EwDavidForms.Android/EwDavidForms.Android.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-with-props/example.fsproj",
    "complex/thirdparty-suites/upgrade-assistant/upgrade-assistant-be3f44f/tests/tool/Integration.Tests/IntegrationScenarios/UWPSample/Original/UWPMigrationSample2.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-no-packagereference/project.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-no-packagereference/project.vbproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/reference-assemblies-with-package-reference/project.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-core-simple-project-complex-target-frameworks/simple-project.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-core-simple-project/simple-project.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-core-simple-project-empty-item-group/simple-project-empty-item-group.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-fs-package-reference-update/example.fsproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-vb-simple-project/manifest.vbproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-core-simple-project-no-version-and-version-star/foobar-no-ver.csproj",
    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/no_target_framework2/no_target_framework2.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/reference-assemblies/project.csproj",
    "complex/thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/net462Invalid/DependencyChecker.csproj",
    "complex/thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/NetStandard/NetStandard/NetStandard/NetStandard.csproj",
    "complex/thirdparty-suites/dotnet-outdated/dotnet-outdated-782521a/test/DotNetOutdated.Tests/TestData/CPVMProject.csproj",
    "complex/thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/net462withoutPackages/DependencyChecker.csproj",
    "complex/thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/netCore21/DependencyChecker.csproj",
])

# These test paths are supposed to have an error returned by design and no usable output
paths_expected_to_return_an_error_and_no_output = set([
    "nuget-config/private-nuget/example.csproj",
    "nuget-config/api-v2-sunnydrive-7f6e4b/src/MusicStore/MusicStore.xproj",
    "complex/end-to-end5/component-detection-1.4.1/src/Microsoft.ComponentDetection.Common/Microsoft.ComponentDetection.Common.csproj",
    "basic/csproj5/mini.csproj",
    "properties/project-with-packages.props1/Foo.csproj",
])

# These test path are launched with extra nuget-inspector CLI arguments
paths_with_extra_arguments = {
    "nuget-config/private-nuget/example.csproj": "--with-fallback",
    "nuget-config/api-v2-sunnydrive-7f6e4b/src/MusicStore/MusicStore.xproj": "--with-fallback",
    "basic/csproj1/CycloneDX.csproj": "--with-details",
    "basic/csproj2/example.csproj": "--with-details",
    "basic/metadata/sample.csproj": "--with-details",
}


def get_test_file_paths(base_dir, pattern, excludes=failing_paths):
    """
    Return a list of test file paths under ``base_dir`` Path matching the glob
    ``pattern``. This used to collect lists of test files.
    """
    paths = (str(p.relative_to(base_dir)) for p in Path(base_dir).glob(pattern))
    if excludes:
        paths = [p for p in paths if not p.endswith(excludes)]
    return paths


project_tests = get_test_file_paths(
    base_dir=TEST_DATA_DIR,
    pattern="**/*.*proj",
    excludes=failing_paths)


@pytest.mark.parametrize("test_path", project_tests)
def test_nuget_inspector_end_to_end_with_projects(test_path):
    check_nuget_inspector_end_to_end(test_path=test_path, regen=REGEN_TEST_FIXTURES)


@pytest.mark.xfail(reason="Failing tests to review")
@pytest.mark.parametrize("test_path", failing_paths)
def test_nuget_inspector_end_to_end_with_failing(test_path):
    check_nuget_inspector_end_to_end(test_path=test_path, regen=REGEN_TEST_FIXTURES)


def test_nuget_inspector_end_to_end_proj_file_target_with_framework_and_nuget_config_1():
    test_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj"
    expected_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj-expected-netcoreapp3.1.json"
    nuget_config = test_env.get_test_loc("complex/thirdparty-suites/ort-tests/dotnet/nuget.config")
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=f' --target-framework "netcoreapp3.1" --nuget-config {nuget_config}',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_file_target_with_framework_and_nuget_config_2():
    test_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj"
    expected_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj-expected-net45.json"
    nuget_config = test_env.get_test_loc("complex/thirdparty-suites/ort-tests/dotnet/nuget.config")
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=f' --target-framework "net45" --nuget-config "{nuget_config}" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_file_target_with_default_framework_and_nuget_config_3():
    test_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj"
    expected_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj-expected-no-target.json"
    nuget_config = test_env.get_test_loc("complex/thirdparty-suites/ort-tests/dotnet/nuget.config")
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=f' --nuget-config "{nuget_config}" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net60():
    test_path = "packages-config/packages.config4/Sample.Nexb.csproj"
    expected_path = "packages-config/packages.config4/Sample.Nexb.csproj-expected-net6.0.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net6.0" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_netcoreapp31():
    test_path = "packages-config/packages.config4/Sample.Nexb.csproj"
    expected_path = "packages-config/packages.config4/Sample.Nexb.csproj-expected-netcoreapp3.1.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "netcoreapp3.1" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net45():
    test_path = "packages-config/packages.config4/Sample.Nexb.csproj"
    expected_path = "packages-config/packages.config4/Sample.Nexb.csproj-expected-net45.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net45" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_nuget_config_with_details_does_not_fail():
    test_path = "nuget-config/mini-config2/Sample/Sample.csproj"
    expected_path = "nuget-config/mini-config2/Sample/Sample.csproj-expected-details.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --with-details ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net461_1():
    test_path = "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/multipackagesconfig/proj1/proj1.csproj"
    expected_path = "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/multipackagesconfig/proj1/proj1.csproj-expected-net461.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net461" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net461_2():
    test_path = "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/multipackagesconfig/proj2/proj2.csproj"
    expected_path = "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/multipackagesconfig/proj2/proj2.csproj-expected-net461.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net461" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net461_3():
    test_path = "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/packagesconfig/packagesconfig.csproj"
    expected_path = "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/packagesconfig/packagesconfig.csproj-expected-net461.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net461" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net461_4():
    test_path = "complex/thirdparty-suites/nuget-deps-tree/nuget-deps-tree-608b32e/test/resources/packagesconfig/packagesconfig.csproj"
    expected_path = "complex/thirdparty-suites/nuget-deps-tree/nuget-deps-tree-608b32e/test/resources/packagesconfig/packagesconfig.csproj-expected-net461.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net461" ',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_packages_config_with_target_framework_net451_1():
    test_path = "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-movie-hunter-api/SampleProject.csproj"
    expected_path = "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-movie-hunter-api/SampleProject.csproj-expected-net451.json"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=' --target-framework "net451" ',
        regen=REGEN_TEST_FIXTURES,
    )


def clean_text_file(location, path=test_env.test_data_dir):
    """
    Clean a text file at ``location`` from harcoded ``path`` and return the
    cleaned text
    """
    with open(location) as inp:
        text = inp.read().replace(path, "")

    with open(location, "w") as out:
        out.write(text)

    return text


def load_and_clean_json(location):
    """
    Clean a JSON results file at ``location`` from harcoded ``path`` and
    """
    text = clean_text_file(location)
    data = json.loads(text)
    header = data["headers"][0]

    # this can change on each version
    header["tool_version"] = "0.0.0"
    # this can change on each run
    options = [h for h in header["options"] if not h.startswith("--json")]
    header["options"] = options
    return data


def check_nuget_inspector_end_to_end(
    test_path,
    expected_path=None,
    extra_args="",
    regen=REGEN_TEST_FIXTURES
):
    """
    Run nuget-inspector on ``test_path`` string and check that results match the
    expected ``test_path``-expected.json file.
    """
    is_expected_with_error_and_out = test_path in paths_expected_to_return_an_error_and_output
    is_expected_with_error_no_out = test_path in paths_expected_to_return_an_error_and_no_output

    extra_arguments = paths_with_extra_arguments.get(test_path) or ""
    test_loc = test_env.get_test_loc(test_path)
    result_file = test_env.get_temp_file(extension=".json")

    cmd = [
        f"{NUGET_INSPECTOR} "
        f"--project-file \"{test_loc}\" "
        f"--json \"{result_file}\" "
        f" {extra_args}"
        f" {extra_arguments}"
    ]
    try:
        subprocess.check_output(cmd, shell=True)
    except subprocess.CalledProcessError:
        if is_expected_with_error_no_out:
            return

        if not is_expected_with_error_and_out:
            cmd = [cmd[0] + " --verbose"]
            try:
                subprocess.check_output(cmd, shell=True)
            except subprocess.CalledProcessError as ex:
                out = ex.output.decode("utf-8")
                print("==================")
                print(out)
                print("==================")
                raise Exception(
                    "Failed to run", " ".join(cmd),
                    "with output:", out,
                )

    if expected_path is None:
        expected_path = test_path + "-expected.json"

    expected_file = test_env.get_test_loc(expected_path, must_exist=False)

    clean_text_file(location=result_file)
    result = load_and_clean_json(result_file)
    if regen:
        with open(expected_file, "w") as o:
            o.write(json.dumps(result, indent=2))
    else:
        expected = load_and_clean_json(expected_file)

        try:
            assert result == expected
        except:
            assert json.dumps(result, indent=2) == json.dumps(expected, indent=2)

