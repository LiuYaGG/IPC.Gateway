# syntax=docker/dockerfile:1

FROM node:22-alpine AS web-build
WORKDIR /build/web
COPY src/IPC.Gateway.Web/package*.json ./
RUN npm ci
COPY src/IPC.Gateway.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /build/repository
# Include every referenced project and Directory.Build.props before restore.
COPY . .
RUN dotnet restore src/IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj
RUN dotnet restore src/IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj
COPY --from=web-build /build/web/dist ./src/IPC.Gateway.Web/dist
RUN dotnet publish src/IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj -c Release -o /app/plugins/Drivers --no-restore /p:UseAppHost=false
RUN dotnet publish src/IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false
RUN mkdir -p /app/publish/Drivers && cp -a /app/plugins/Drivers/. /app/publish/Drivers/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:5184
ENV Gateway__Watchdog__RequestHostStopOnUnrecoverable=true
ENV Gateway__History__Directory=/app/Data/History
ENV Gateway__Mqtt__OutboxDirectory=/app/Data/MqttOutbox
ENV Gateway__Maintenance__Updates__UpdateDirectory=/app/Data/Updates
ENV Gateway__OpcUa__CertificateStorePath=/app/Data/OpcUa/pki
ENV Gateway__Watchdog__StateDirectory=/app/Data/Watchdog
ENV Gateway__Inference__ModelDirectory=/app/Data/Models
ENV Gateway__Scripting__ConfigurationFile=/app/Data/Scripting/config.json
EXPOSE 5184
VOLUME ["/app/Data"]
COPY --from=publish /app/publish ./
ENTRYPOINT ["dotnet", "IPC.Gateway.WebHost.dll"]
