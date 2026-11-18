# syntax=docker/dockerfile:1
# One Dockerfile for the .NET services: docker build --build-arg APP=Web|Worker|ErpMock|EskiAmbar .
# (The RPA bot needs a browser and has its own Dockerfile in src/Tersane.Rpa.)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG APP
WORKDIR /src

# Restore first, from project files only, so source edits do not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Tersane.Data/Tersane.Data.csproj src/Tersane.Data/
COPY src/Tersane.Web/Tersane.Web.csproj src/Tersane.Web/
COPY src/Tersane.Worker/Tersane.Worker.csproj src/Tersane.Worker/
COPY src/Tersane.ErpMock/Tersane.ErpMock.csproj src/Tersane.ErpMock/
COPY src/Tersane.EskiAmbar/Tersane.EskiAmbar.csproj src/Tersane.EskiAmbar/
RUN dotnet restore src/Tersane.${APP}/Tersane.${APP}.csproj

COPY src/ src/
RUN dotnet publish src/Tersane.${APP}/Tersane.${APP}.csproj -c Release --no-restore -o /app /p:UseAppHost=false

# Full (non-chiseled) image on purpose: the UI uses tr-TR culture, which needs ICU.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APP
ENV APP_DLL=Tersane.${APP}.dll \
    TZ=Europe/Istanbul
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]
