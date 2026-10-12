# 多阶段构建。构建上下文必须是仓库根目录：
#   docker build -f src/services/administration/host/Meta.Dow.Administration.HttpApi.Host/Dockerfile -t meta-dow:meta.dow.administration.httpapi.host .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet publish "src/services/administration/host/Meta.Dow.Administration.HttpApi.Host/Meta.Dow.Administration.HttpApi.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Meta.Dow.Administration.HttpApi.Host.dll"]
