# syntax=docker/dockerfile:1

FROM node:22-alpine AS web-build
WORKDIR /src/IPC.Gateway.Web
COPY IPC.Gateway.Web/package*.json ./
RUN npm ci
COPY IPC.Gateway.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY IPC.Gateway.slnx ./
COPY IPC.Gateway.Core/IPC.Gateway.Core.csproj IPC.Gateway.Core/
COPY IPC.Gateway.Mqtt/IPC.Gateway.Mqtt.csproj IPC.Gateway.Mqtt/
COPY IPC.Gateway.Watchdog/IPC.Gateway.Watchdog.csproj IPC.Gateway.Watchdog/
COPY IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj IPC.Gateway.LegacyProtocolPlugins/
COPY IPC.Gateway.TestProtocolPlugin/IPC.Gateway.TestProtocolPlugin.csproj IPC.Gateway.TestProtocolPlugin/
COPY IPC.Gateway.LoadTests/IPC.Gateway.LoadTests.csproj IPC.Gateway.LoadTests/
COPY IPC.Gateway.Tests/IPC.Gateway.Tests.csproj IPC.Gateway.Tests/
COPY IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj IPC.Gateway.WebHost/
RUN dotnet restore IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj
RUN dotnet restore IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj
COPY . .
COPY --from=web-build /src/IPC.Gateway.Web/dist ./IPC.Gateway.Web/dist
RUN dotnet publish IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj -c Release -o /app/plugins/Drivers --no-restore /p:UseAppHost=false
RUN dotnet publish IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false
RUN mkdir -p /app/publish/Drivers && cp -a /app/plugins/Drivers/. /app/publish/Drivers/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:5184
ENV Gateway__Watchdog__RequestHostStopOnUnrecoverable=true
EXPOSE 5184
VOLUME ["/app/Data"]
COPY --from=publish /app/publish ./
ENTRYPOINT ["dotnet", "IPC.Gateway.WebHost.dll"]
