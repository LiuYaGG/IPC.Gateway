FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/IPC.Gateway.Arbiter/IPC.Gateway.Arbiter.csproj -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish/ ./
ENV IPC_ARBITER_CONFIG=/etc/ipc-arbiter/member.json \
    Arbiter__StateDirectory=/var/lib/ipc-arbiter
EXPOSE 7281 7282
ENTRYPOINT ["dotnet", "IPC.Gateway.Arbiter.dll"]
