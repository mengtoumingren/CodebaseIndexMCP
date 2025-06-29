FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CodebaseMcpServer_v2/CodebaseMcpServer_v2.csproj", "CodebaseMcpServer_v2/"]
RUN dotnet restore "CodebaseMcpServer_v2/CodebaseMcpServer_v2.csproj"
COPY . .
WORKDIR "/src/CodebaseMcpServer_v2"
RUN dotnet build "CodebaseMcpServer_v2.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CodebaseMcpServer_v2.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CodebaseMcpServer_v2.dll"]