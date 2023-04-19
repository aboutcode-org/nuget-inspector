#!/usr/bin/env bash
#
# Copyright (c) nexB Inc. and others. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
# See http://www.apache.org/licenses/LICENSE-2.0 for the license text.
# See https://github.com/nexB/nuget-inpector for support or download.
# See https://aboutcode.org for more information about nexB OSS projects.
#

# TODO: add --framework
# TODO: add --version-suffix based on git
# TODO: add --arch
# TODO: add --os
#  -p:PublishSingleFile=true \

dotnet publish \
  --runtime linux-x64 \
  --self-contained true \
  --configuration Release \
  -p:Version=0.9.12 \
  --output build \
  src/nuget-inspector/nuget-inspector.csproj
