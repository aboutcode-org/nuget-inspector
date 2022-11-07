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
from commoncode import fileutils
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

failing_paths = [
    "thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-variables-resolved/Steeltoe.Extensions.Configuration.CloudFoundryAutofac.Test.csproj",
    "thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-variables/Steeltoe.Extensions.Configuration.CloudFoundryAutofac.Test.csproj",
    "thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-variables/Steeltoe.Extensions.Configuration.CloudFoundryAutofac.Test.csproj",
    "thirdparty-suites/nuget-deps-tree/nuget-deps-tree-608b32e/test/resources/packagereferences/noproject/noproject.csproj",
    "thirdparty-suites/dependencychecker/DependencyChecker-22983ae/DependencyChecker.Test/TestProjects/net462/DependencyChecker.csproj",
    "thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/dummy_project_2/dummy_project_2.csproj",
    "thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/no_target_valid_framework/no_target_valid_framework.csproj",
    "thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/csproj_multiple/csproj_multiple.csproj",
    "thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/projectRootTestDir/projectAssetsDir/another_example.csproj",
    "thirdparty-suites/buildinfo/build-info-9bd00bd/build-info-extractor-nuget/extractor/projectRootTestDir/packagesConfigDir/example.csproj",
    "thirdparty-suites/snyk-nuget-plugin/snyk-nuget-plugin-201af77/test/stubs/target_framework/no_target_framework/no_target_framework.csproj",
    "thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-empty-manifest/empty-manifest.csproj",
    "thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-with-props/example.fsproj",
    "thirdparty-suites/snyk-dotnet-parser/dotnet-deps-parser-ebd0e1b/test/fixtures/dotnet-invalid-manifest/invalid.csproj",
]


def get_test_file_paths(base_dir, pattern, excludes=failing_paths):
    """
    Return a list of test file paths under ``base_dir`` Path matching the glob
    ``pattern``. This used to collect lists of test files.
    """
    paths = (str(p.relative_to(base_dir)) for p in Path(base_dir).glob(pattern))
    if excludes:
        paths = [p for p in paths if p not in excludes]
    return paths


solution_tests = get_test_file_paths(base_dir=TEST_DATA_DIR, pattern="**/*.sln")


@pytest.mark.parametrize("test_path", solution_tests)
def test_nuget_inspector_end_to_end_with_solutions(test_path, regen=REGEN_TEST_FIXTURES):
    check_nuget_inspector_end_to_end(test_path, regen)


project_tests = get_test_file_paths(base_dir=TEST_DATA_DIR, pattern="**/*.??proj")


@pytest.mark.parametrize("test_path", project_tests)
def test_nuget_inspector_end_to_end_with_projects(test_path, regen=REGEN_TEST_FIXTURES):
    check_nuget_inspector_end_to_end(test_path, regen)


@pytest.mark.xfail(reason="Failng tests to review")
@pytest.mark.parametrize("test_path", failing_paths)
def test_nuget_inspector_end_to_end_with_failing(test_path, regen=REGEN_TEST_FIXTURES):
    check_nuget_inspector_end_to_end(test_path, regen)


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


def load_cleaned_json(location):
    """
    Clean a JSON results file at ``location`` from harcoded ``path`` and
    """
    text = clean_text_file(location)
    return json.loads(text)


def check_nuget_inspector_end_to_end(test_path, regen=REGEN_TEST_FIXTURES):
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
    ]

    try:
        subprocess.check_output(cmd, shell=True)
    except subprocess.CalledProcessError as e:
        out = e.output.decode("utf-8")
        print(out)
        raise Exception(
            "Failed to run", " ".join(cmd),
            "with output:", e.output.decode("utf-8"),
        ) from e

    expected_path = test_path + "-expected.json"
    expected_file = test_env.get_test_loc(expected_path, must_exist=False)

    if regen:
        clean_text_file(location=result_file)
        fileutils.copyfile(src=result_file, dst=expected_file)
    else:
        result = load_cleaned_json(result_file)
        with open(expected_file) as ef:
            expected = json.load(ef)
        try:
            assert result == expected
        except:
            assert json.dumps(result, indent=2) == json.dumps(expected, indent=2)

