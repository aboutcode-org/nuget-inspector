#
# Copyright (c) nexB Inc. and others. All rights reserved.
# ScanCode is a trademark of nexB Inc.
# SPDX-License-Identifier: Apache-2.0
# See http://www.apache.org/licenses/LICENSE-2.0 for the license text.
# See https://github.com/nexB/skeleton for support or download.
# See https://aboutcode.org for more information about nexB OSS projects.
#

import os
import json
import subprocess

import pytest
from commoncode import fileutils
from commoncode.testcase import FileDrivenTesting

from testing_utils import REGEN_TEST_FIXTURES
from testing_utils import ROOT_DIR
from testing_utils import TEST_DATA_DIR
import commoncode

test_env = FileDrivenTesting()
test_env.test_data_dir = str(TEST_DATA_DIR)

NUGET_INSPECTOR = str(ROOT_DIR / "build" / "nuget-inspector")

solution_tests = [
    "end-to-end1/Sample-ASP.NET-project/Sample.sln",
    "end-to-end1/Sample-ASP.NET-project/Sample.Application/Sample.Application.csproj",
    "end-to-end2/cyclonedx-dotnet/CycloneDX.sln",
    "packages.lock.json1/GenericHostConsoleApp.sln",
    "end-to-end1/Sample-ASP.NET-project/Sample.sln",
    "end-to-end5/component-detection-1.4.1/ComponentDetection.sln",
    "end-to-end4/Bonobo-Git-Server-6.5.0/Bonobo.Git.Server.sln",
    "end-to-end2/cyclonedx-dotnet/CycloneDX.sln",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/Elastic.Abstractions.sln",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Portable.sln",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Net35.sln",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Roslyn.sln",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Net20.sln",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Net40.sln",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.sln",
    "end-to-end7/Nuuvify.CommonPack-119311/CommonPack.sln",
    "end-to-end6/SignalR-a19f73/Microsoft.AspNet.SignalR.sln",
]


@pytest.mark.parametrize("test_path", solution_tests)
def test_nuget_inspector_end_to_end_with_solutions(test_path, regen=REGEN_TEST_FIXTURES):
    check_nuget_inspector_end_to_end(test_path, regen)


failing_project_tests = [
    "packages.lock.json1/GenericHostConsoleApp/GenericHostConsoleApp.csproj",
    "end-to-end2/cyclonedx-dotnet/CycloneDX.Tests/CycloneDX.Tests.csproj",
    "end-to-end2/cyclonedx-dotnet/CycloneDX/CycloneDX.csproj",

    "end-to-end3/Newtonsoft.Json-10.0.1/Doc/doc.shfbproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Tests/Newtonsoft.Json.Tests.Net35.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Tests/Newtonsoft.Json.Tests.Net40.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Tests/Newtonsoft.Json.Tests.Portable.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Tests/Newtonsoft.Json.Tests.Roslyn.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Tests/Newtonsoft.Json.Tests.Net20.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.Tests/Newtonsoft.Json.Tests.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.Net35.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.Portable.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.Net20.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.Net40.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.Roslyn.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json/Newtonsoft.Json.csproj",
    "end-to-end3/Newtonsoft.Json-10.0.1/Src/Newtonsoft.Json.TestConsole/Newtonsoft.Json.TestConsole.csproj",

    "end-to-end5/component-detection-1.4.1/src/Microsoft.ComponentDetection/Microsoft.ComponentDetection.csproj",
    "end-to-end5/component-detection-1.4.1/src/Microsoft.ComponentDetection.Common/Microsoft.ComponentDetection.Common.csproj",
    "end-to-end5/component-detection-1.4.1/src/Microsoft.ComponentDetection.Detectors/Microsoft.ComponentDetection.Detectors.csproj",
    "end-to-end5/component-detection-1.4.1/src/Microsoft.ComponentDetection.Contracts/Microsoft.ComponentDetection.Contracts.csproj",
    "end-to-end5/component-detection-1.4.1/test/Microsoft.ComponentDetection.Common.Tests/Microsoft.ComponentDetection.Common.Tests.csproj",
    "end-to-end5/component-detection-1.4.1/test/Microsoft.ComponentDetection.Contracts.Tests/Microsoft.ComponentDetection.Contracts.Tests.csproj",
    "end-to-end5/component-detection-1.4.1/test/Microsoft.ComponentDetection.VerificationTests/Microsoft.DependencyDetective.VerificationTests.csproj",
    "end-to-end5/component-detection-1.4.1/test/Microsoft.ComponentDetection.TestsUtilities/Microsoft.ComponentDetection.TestsUtilities.csproj",
    "end-to-end5/component-detection-1.4.1/test/Microsoft.ComponentDetection.Orchestrator.Tests/Microsoft.ComponentDetection.Orchestrator.Tests.csproj",
    "end-to-end5/component-detection-1.4.1/test/Microsoft.ComponentDetection.Detectors.Tests/Microsoft.ComponentDetection.Detectors.Tests.csproj",

    "end-to-end6/SignalR-a19f73/samples/Microsoft.AspNet.SignalR.LoadTestHarness/Microsoft.AspNet.SignalR.LoadTestHarness.csproj",
    "end-to-end6/SignalR-a19f73/samples/Microsoft.AspNet.SignalR.Client.Samples/Microsoft.AspNet.SignalR.Client.Samples.csproj",
    "end-to-end6/SignalR-a19f73/samples/Microsoft.AspNet.SelfHost.Samples/Microsoft.AspNet.SelfHost.Samples.csproj",
    "end-to-end6/SignalR-a19f73/samples/Microsoft.AspNet.SignalR.Samples.VB/Microsoft.AspNet.SignalR.Samples.VB.vbproj",
    "end-to-end6/SignalR-a19f73/samples/Microsoft.AspNet.SignalR.Samples/Microsoft.AspNet.SignalR.Samples.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.Core/Microsoft.AspNet.SignalR.Core.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.StackExchangeRedis/Microsoft.AspNet.SignalR.StackExchangeRedis.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.ServiceBus/Microsoft.AspNet.SignalR.ServiceBus.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.SelfHost/Microsoft.AspNet.SignalR.SelfHost.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.JS/Microsoft.AspNet.SignalR.JS.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.SqlServer/Microsoft.AspNet.SignalR.SqlServer.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.Client/Microsoft.AspNet.SignalR.Client.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.Stress/Microsoft.AspNet.SignalR.Stress.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR/Microsoft.AspNet.SignalR.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.StressServer/Microsoft.AspNet.SignalR.StressServer.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.ServiceBus3/Microsoft.AspNet.SignalR.ServiceBus3.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.Redis/Microsoft.AspNet.SignalR.Redis.csproj",
    "end-to-end6/SignalR-a19f73/src/Microsoft.AspNet.SignalR.SystemWeb/Microsoft.AspNet.SignalR.SystemWeb.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Client.Tests/Microsoft.AspNet.SignalR.Client.Tests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Client.UWP.TestHost/Microsoft.AspNet.SignalR.Client.UWP.TestHost.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Tests.Common/Microsoft.AspNet.SignalR.Tests.Common.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Client.UWP.Tests/Microsoft.AspNet.SignalR.Client.UWP.Tests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Tests/Microsoft.AspNet.SignalR.Tests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Redis.Tests/Microsoft.AspNet.SignalR.Redis.Tests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.Client.JS.Tests/Microsoft.AspNet.SignalR.Client.JS.Tests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.FunctionalTests/Microsoft.AspNet.SignalR.FunctionalTests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.StackExchangeRedis.Tests/Microsoft.AspNet.SignalR.StackExchangeRedis.Tests.csproj",
    "end-to-end6/SignalR-a19f73/test/Microsoft.AspNet.SignalR.SqlServer.Tests/Microsoft.AspNet.SignalR.SqlServer.Tests.csproj",
    "end-to-end6/SignalR-a19f73/build/loc/lsbuild.proj",
    "end-to-end6/SignalR-a19f73/build/loc/SatellitePackage.csproj",
    "end-to-end6/SignalR-a19f73/build/sign.proj",

    "end-to-end7/Nuuvify.CommonPack-119311/src/Nuuvify.CommonPack.Security/Nuuvify.CommonPack.Security.csproj",
    "end-to-end7/Nuuvify.CommonPack-119311/src/Nuuvify.CommonPack.EF.Exceptions.Db2/Nuuvify.CommonPack.EF.Exceptions.Db2.csproj",

    "end-to-end8/elasticsearch-net-abstractions-0.3.5/src/Nest.TypescriptExporter/Nest.TypescriptExporter.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/src/Elastic.Elasticsearch.Managed/Elastic.Elasticsearch.Managed.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/src/Elastic.Elasticsearch.Xunit/Elastic.Elasticsearch.Xunit.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/src/Elastic.Stack.ArtifactsApi/Elastic.Stack.ArtifactsApi.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/src/Elastic.Elasticsearch.Ephemeral/Elastic.Elasticsearch.Ephemeral.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/examples/ScratchPad/ScratchPad.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/examples/Elastic.Xunit.ExampleComplex/Elastic.Xunit.ExampleComplex.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/examples/Elastic.Managed.Example/Elastic.Managed.Example.csproj",
    "end-to-end8/elasticsearch-net-abstractions-0.3.5/examples/Elastic.Xunit.ExampleMinimal/Elastic.Xunit.ExampleMinimal.csproj",

    "Packages.props2/SampleProject.csproj",
    "Packages.props4/SampleProject.csproj",
    "misc/csproj1/CycloneDX.csproj",
    "Packages.props3/SampleProject.csproj",

    "Packages.props1/Foo.csproj",
]
project_tests = [
    "end-to-end1/Sample-ASP.NET-project/Sample.Core/Sample.Core.csproj",
    "end-to-end1/Sample-ASP.NET-project/Sample.Web/Sample.Web.csproj",
    "end-to-end1/Sample-ASP.NET-project/Sample.WebApi/Sample.WebApi.csproj",
    "end-to-end1/Sample-ASP.NET-project/Sample.Application/Sample.Application.csproj",

    "end-to-end4/Bonobo-Git-Server-6.5.0/Bonobo.Git.Server.Test/Bonobo.Git.Server.Test.csproj",
    "end-to-end4/Bonobo-Git-Server-6.5.0/Bonobo.Git.Server/Bonobo.Git.Server.csproj",
]


@pytest.mark.parametrize("test_path", project_tests)
def test_nuget_inspector_end_to_end_with_projects(test_path, regen=REGEN_TEST_FIXTURES):
    check_nuget_inspector_end_to_end(test_path, regen)


@pytest.mark.parametrize("test_path", failing_project_tests)
@pytest.mark.xfail(reason="Failing for now, needs research")
def test_nuget_inspector_end_to_end_with_projects_failing(test_path, regen=REGEN_TEST_FIXTURES):
    check_nuget_inspector_end_to_end(test_path, regen=regen)


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
    test_loc = test_env.get_test_loc(test_path)
    result_loc = test_env.get_temp_dir()
    cmd = [
        f"{NUGET_INSPECTOR} "
        f"--project-file \"{test_loc}\" "
        f"--json \"{result_loc}\" "
    ]
    output = None
    try:
        output = subprocess.check_output(cmd, shell=True)
    except subprocess.CalledProcessError as e:
        raise Exception(
            "Failed to run", " ".join(cmd),
            "with output:", str(output),
            e.output,
        ) from e

    expected_path = test_path + "-expected"
    expected_loc = test_env.get_test_loc(expected_path, must_exist=False)
    fileutils.create_dir(expected_loc)
    expected_files = sorted(os.listdir(expected_loc))

    result_files = sorted(os.listdir(result_loc))
    if regen:
        commoncode.fileutils.delete(location=expected_loc)
        commoncode.fileutils.create_dir(location=expected_loc)
        for result_file in result_files:
            expected_file = os.path.join(expected_loc, result_file)
            result_file = os.path.join(result_loc, result_file)

            clean_text_file(location=result_file)
            commoncode.fileutils.copyfile(src=result_file, dst=expected_file)
    else:
        for expected_file in expected_files:
            result_file = os.path.join(result_loc, expected_file)
            result = load_cleaned_json(result_file)
            expected_file = os.path.join(expected_loc, expected_file)
            with open(expected_file) as ef:
                expected = json.load(ef)
            assert expected == result

    # make sure we do not have dangling files
    expected_files = sorted(os.listdir(expected_loc))
    result_files = sorted(os.listdir(result_loc))
    assert result_files
    assert result_files == expected_files

