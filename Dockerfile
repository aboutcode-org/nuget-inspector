# syntax=docker/dockerfile:1.4

FROM ubuntu:jammy as base-image

ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

RUN apt-get update \
    && apt-get install -y curl

RUN curl --location https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb \
         --output packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb

RUN apt-get update && \
  sudo apt-get install -y dotnet-sdk-6.0

ENV NI_HOME=/opt/nuget-inspector/bin
ENV PATH=$PATH:$NI_HOME
ARG NI_VERSION=0.9.10
RUN mkdir -p $NI_HOME \
    && curl -L https://github.com/nexB/nuget-inspector/releases/download/v${NI_VERSION}/nuget-inspector-v${NI_VERSION}-linux-x64.tar.gz \
    | tar --strip-components=1 -C $NI_HOME -xz

ENTRYPOINT ["/opt/nuget-inspector/bin/nuget-inspector"]
