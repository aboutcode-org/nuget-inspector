# syntax=docker/dockerfile:1.4

FROM ubuntu:jammy as base-image

ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

# curl and nuget/donet system dependencies for jammy
RUN apt-get update \
    && apt-get install -y \
    curl \
    libc6 \
    libgcc1 \
    libgcc-s1 \
    libgssapi-krb5-2 \
    libicu70 \
    liblttng-ust1 \
    libssl3 \
    libstdc++6 \
    libunwind8 \
    zlib1g

ENV NI_ROOT=/opt/nuget-inspector
ENV NI_HOME=$NI_ROOT/bin
ENV NI_DOTNET_HOME=$NI_ROOT/dotnet

ENV PATH=$PATH:$NI_DOTNET_HOME:$NI_DOTNET_HOME/tools:$NI_HOME

RUN mkdir -p $NI_DOTNET_HOME \
    && curl --location https://aka.ms/dotnet/6.0/dotnet-sdk-linux-x64.tar.gz \
    | tar -C $NI_DOTNET_HOME -xz

ARG NI_VERSION=0.9.12
RUN mkdir -p $NI_HOME \
    && curl -L https://github.com/aboutcode-org/nuget-inspector/releases/download/v${NI_VERSION}/nuget-inspector-v${NI_VERSION}-linux-x64.tar.gz \
    | tar --strip-components=1 -C $NI_HOME -xz

ENTRYPOINT ["/opt/nuget-inspector/bin/nuget-inspector"]
