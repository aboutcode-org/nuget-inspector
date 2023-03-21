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
A data-driven test suite with a number of solutions and project manifests
that represent a variety of cases.
"""

test_env = FileDrivenTesting()
test_env.test_data_dir = str(TEST_DATA_DIR)

NUGET_INSPECTOR = str(ROOT_DIR / "build" / "nuget-inspector")

failing_paths = (
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-invalid-project-assets/SampleProject.csproj",
    "complex/thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/net462/DependencyChecker.csproj",
    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/dummy_project_2/dummy_project_2.csproj",
    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/no_target_valid_framework/no_target_valid_framework.csproj",
    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/csproj_multiple/csproj_multiple.csproj",
    "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/projectRootTestDir/projectAssetsDir/another_example.csproj",
    "complex/thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/projectRootTestDir/packagesConfigDir/example.csproj",
    "complex/thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/no_target_framework/no_target_framework.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-empty-manifest/empty-manifest.csproj",
    "complex/thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-invalid-manifest/invalid.csproj",
    "project-json/datatables/datatables.aspnet-68483b7/src/DataTables.AspNet.Extensions.DapperExtensions.Tests/DataTables.AspNet.Extensions.DapperExtensions.Tests.xproj",
)


def get_test_file_paths(base_dir, pattern, excludes=failing_paths):
    """
    Return a list of test file paths under ``base_dir`` Path matching the glob
    ``pattern``. This used to collect lists of test files.
    """
    paths = (str(p.relative_to(base_dir)) for p in Path(base_dir).glob(pattern))
    if excludes:
        paths = [p for p in paths if not p.endswith(excludes)]
    return paths


project_tests = get_test_file_paths(base_dir=TEST_DATA_DIR, pattern="**/*.*proj")


@pytest.mark.parametrize("test_path", project_tests)
def test_nuget_inspector_end_to_end_with_projects(test_path):
    check_nuget_inspector_end_to_end(test_path=test_path, regen=REGEN_TEST_FIXTURES)


expected_tests = get_test_file_paths(base_dir=TEST_DATA_DIR, pattern="**/*.*-expected*.json")

#
# @pytest.mark.parametrize("json_path", expected_tests)
# def test_fix_nuget_inspector(json_path):
    # location = test_env.get_test_loc(json_path)
    #
    # with open(location) as inp:
        # text = inp.read()
    # if text and text.strip():
        # data = json.loads(text)
        #
        # for pkg in data["packages"]:
            # dependencies = flatten_deps(pkg["dependencies"])
            # dependencies = {d["purl"]: d for d in dependencies}
            # dependencies = list(dependencies.values())
            # sort_deps(dependencies)
            #
            # pkg["dependencies"] = dependencies
            #
        # with open(location, "w") as o:
            # o.write(json.dumps(data, indent=2))

DEPS = [
    {
        "name": "baz",
        "dependencies": []
    },
    {
        "name": "foo",
        "dependencies": [
            {
                "name": "foo1",
                "dependencies": [
                    {
                        "name": "foo11",
                        "dependencies": [
                            {
                                "name": "foo111",
                                "dependencies": [
                                ]
                            },
                            {
                                "name": "foo112",
                                "dependencies": [
                                ]
                            },

                        ]
                    },

                ]
            },
            {
                "name": "foo2",
                "dependencies": [
                ]
            },
        ]
    },
    {
        "name": "bar",
        "dependencies": [
            {
                "name": "bar1",
                "dependencies": [
                    {
                        "name": "bar11",
                        "dependencies": [
                            {
                                "name": "bar111",
                                "dependencies": [
                                ]
                            },
                            {
                                "name": "bar112",
                                "dependencies": [
                                ]
                            },

                        ]
                    },

                ]
            }
        ]
    }

]

EXPECTED_DEPS = [
    {"name": "baz", "dependencies": []},
    {"name": "foo", "dependencies": []},
    {"name": "foo1", "dependencies": []},
    {"name": "foo11", "dependencies": []},
    {"name": "foo111", "dependencies": []},
    {"name": "foo112", "dependencies": []},
    {"name": "foo2", "dependencies": []},
    {"name": "bar", "dependencies": []},
    {"name": "bar1", "dependencies": []},
    {"name": "bar11", "dependencies": []},
    {"name": "bar111", "dependencies": []},
    {"name": "bar112", "dependencies": []}
]


def test_flatten_deps():
    flat = flatten_deps(DEPS)
    assert flat == EXPECTED_DEPS


def flatten_deps(dependencies):
    """
    Flatten recursively a tree of dependencies. Remove subdeps as the flattening goes.
    """

    flattened = []
    for dep in dependencies:
        depdeps = dep["dependencies"]
        dep["dependencies"] = []
        flattened.append(dep)
        flattened.extend(flatten_deps(depdeps))
    return flattened


def sort_deps(lst):

    def purl_key(dep):
        return (
            dep["type"] or "",
            dep["namespace"] or "",
            (dep["name"] or "").lower(),
            (dep["version"] or "").lower(),
            dep["qualifiers"],
            dep["subpath"]
        )

    lst.sort(key=purl_key)

    for dep in lst:
        sort_deps(dep["dependencies"])


def test_nuget_inspector_end_to_end_proj_file_target_with_framework_and_nuget_config_1():
    test_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj"
    expected_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj-expected-netcoreapp3.1.json"
    nuget_config = "complex/thirdparty-suites/ort-tests/dotnet/nuget.config"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=f' --target-framework "netcoreapp3.1" --nuget-config {nuget_config}',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_file_target_with_framework_and_nuget_config_2():
    test_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj"
    expected_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj-expected-net45.json"
    nuget_config = "complex/thirdparty-suites/ort-tests/dotnet/nuget.config"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=f' --target-framework "net45" --nuget-config {nuget_config}',
        regen=REGEN_TEST_FIXTURES,
    )


def test_nuget_inspector_end_to_end_file_target_with_default_framework_and_nuget_config_3():
    test_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj"
    expected_path = "complex/thirdparty-suites/ort-tests/dotnet/subProjectTest/test.csproj-expected-no-target.json"
    nuget_config = "complex/thirdparty-suites/ort-tests/dotnet/nuget.config"
    check_nuget_inspector_end_to_end(
        test_path=test_path,
        expected_path=expected_path,
        extra_args=f' --nuget-config {nuget_config}',
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


@pytest.mark.xfail(reason="Failing tests to review")
@pytest.mark.parametrize("test_path", failing_paths)
def test_nuget_inspector_end_to_end_with_failing(test_path):
    check_nuget_inspector_end_to_end(test_path=test_path, regen=REGEN_TEST_FIXTURES)


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


def check_nuget_inspector_end_to_end(test_path, expected_path=None, extra_args="", regen=REGEN_TEST_FIXTURES):
    """
    Run nuget-inspector on ``test_path`` string and check that results match the
    expected ``test_path``-expected.json file.
    """
    test_loc = test_env.get_test_loc(test_path)
    result_file = test_env.get_temp_file(extension=".json")

    cmd = [
        f"{NUGET_INSPECTOR} "
        f"--project-file \"{test_loc}\" "
        f"--json \"{result_file}\" "
        +extra_args
    ]
    try:
        subprocess.check_output(cmd, shell=True)
    except subprocess.CalledProcessError:
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

