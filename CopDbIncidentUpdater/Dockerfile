FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 65020
EXPOSE 44381

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY CopDbIncidentUpdater/CopDbIncidentUpdater.csproj CopDbIncidentUpdater/
RUN dotnet restore CopDbIncidentUpdater/CopDbIncidentUpdater.csproj
COPY . .
WORKDIR /src/CopDbIncidentUpdater
RUN dotnet build CopDbIncidentUpdater.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish CopDbIncidentUpdater.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "CopDbIncidentUpdater.dll"]
