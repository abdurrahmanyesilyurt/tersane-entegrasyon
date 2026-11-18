# syntax=docker/dockerfile:1
# RPA bot image: .NET runtime + Playwright Chromium and its OS libraries.
# Build context is the repository root:  docker build -f src/Tersane.Rpa/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Tersane.Rpa/Tersane.Rpa.csproj src/Tersane.Rpa/
RUN dotnet restore src/Tersane.Rpa/Tersane.Rpa.csproj -r linux-x64
COPY src/Tersane.Rpa/ src/Tersane.Rpa/
RUN dotnet publish src/Tersane.Rpa/Tersane.Rpa.csproj -c Release -r linux-x64 --self-contained false --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
WORKDIR /app
COPY --from=build /app .
COPY samples/ samples/
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    TZ=Europe/Istanbul
# Downloads Chromium and installs its system libraries (apt) at build time, not at run time.
# libatomic1 is needed by the Node driver that ships inside the Playwright package.
RUN apt-get update && apt-get install -y --no-install-recommends libatomic1 \
    && dotnet Tersane.Rpa.dll --tarayici-kur \
    && rm -rf /var/lib/apt/lists/*
# Runs as root on purpose: it is a short batch job that writes screenshots to a bind-mounted folder
# (./rpa-out on the host). Playwright starts Chromium without the setuid sandbox inside containers.
ENTRYPOINT ["dotnet", "Tersane.Rpa.dll"]
CMD ["--excel", "samples/ornek-irsaliye.xlsx", "--out", "rpa-out"]
