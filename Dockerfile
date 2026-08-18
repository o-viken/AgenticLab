# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore TheSeries.slnx

FROM build AS publish-web
RUN dotnet publish src/TheSeries.Web/TheSeries.Web.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --output /out \
    /p:UseAppHost=false

FROM build AS publish-aiservice
RUN dotnet publish src/TheSeries.AiService/TheSeries.AiService.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM build AS publish-mcpserver
RUN dotnet publish src/TheSeries.McpServer/TheSeries.McpServer.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM build AS publish-a2aserver
RUN dotnet publish src/TheSeries.A2AServer/TheSeries.A2AServer.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID

FROM runtime AS web
COPY --from=publish-web /out .
ENTRYPOINT ["dotnet", "TheSeries.Web.dll"]

FROM runtime AS aiservice
COPY --from=publish-aiservice /out .
ENTRYPOINT ["dotnet", "TheSeries.AiService.dll"]

FROM runtime AS mcpserver
COPY --from=publish-mcpserver /out .
ENTRYPOINT ["dotnet", "TheSeries.McpServer.dll"]

FROM runtime AS a2aserver
COPY --from=publish-a2aserver /out .
ENTRYPOINT ["dotnet", "TheSeries.A2AServer.dll"]
