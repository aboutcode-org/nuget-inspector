# syntax=docker/dockerfile:1.4

FROM ubuntu:jammy as base-image

ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

RUN apt-get update \
    && apt-get install -y curl

RUN curl --location https://dot.net/v1/dotnet-install.sh \
         --output dotnet-install.sh \
    && chmod +x dotnet-install.sh \
    && ./dotnet-install.sh --channel 6.0 \
    && rm dotnet-install.sh
ENV DOTNET_ROOT=$HOME/.dotnet
ENV PATH=$PATH:$HOME/.dotnet:$HOME/.dotnet/tools

ENV NI_HOME=/opt/nuget-inspector/bin
ENV PATH=$PATH:$NI_HOME
ARG NI_VERSION=0.9.10
RUN mkdir -p $NI_HOME \
    && curl -L https://github.com/nexB/nuget-inspector/releases/download/v${NI_VERSION}/nuget-inspector-v${NI_VERSION}-linux-x64.tar.gz \
    | tar --strip-components=1 -C $NI_HOME -xz

ENTRYPOINT ["/opt/nuget-inspector/bin/nuget-inspector"]
