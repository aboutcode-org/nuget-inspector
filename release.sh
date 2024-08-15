#!/usr/bin/env bash
#
# Copyright (c) nexB Inc. and others. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
# See http://www.apache.org/licenses/LICENSE-2.0 for the license text.
# See https://github.com/aboutcode-org/nuget-inpector for support or download.
# See https://aboutcode.org for more information about nexB OSS projects.
#

# TODO: add --framework
# TODO: add --version-suffix based on git
# TODO: add --arch
# TODO: add --os
#  -p:PublishSingleFile=true \

rm -rf release/
mkdir release

VERSION=0.9.12

TARGET_BASE=nuget-inspector-$(git describe)

# see https://learn.microsoft.com/en-us/dotnet/core/rid-catalog
for platform in "linux-x64" "win-x64" "osx-x64"
do
    TARGET=$TARGET_BASE-$platform
    RELEASE_DIR=release/nuget-inspector
    rm -rf $RELEASE_DIR
    mkdir -p $RELEASE_DIR

    dotnet publish \
      --runtime $platform \
      --self-contained true \
      --configuration Release \
      -p:Version=$VERSION \
      --output $RELEASE_DIR \
      src/nuget-inspector/nuget-inspector.csproj ;

    cp apache-2.0.LICENSE \
       mit.LICENSE        \
       AUTHORS.rst        \
       CHANGELOG.rst      \
       NOTICE             \
       README.rst         \
       $RELEASE_DIR

    rm release/nuget-inspector/createdump

    tar -czf release/$TARGET.tar.gz -C release/ nuget-inspector
done
