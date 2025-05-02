# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

RUN dotnet restore
RUN dotnet test ./ImpowerSurvey.Tests/ImpowerSurvey.Tests.csproj --settings ImpowerSurvey.Tests/ImpowerSurvey.MultiInstanceMode.runsettings --filter "TestCategory=MultiInstanceMode"
RUN dotnet test ./ImpowerSurvey.Tests/ImpowerSurvey.Tests.csproj --settings ImpowerSurvey.Tests/ImpowerSurvey.SingleInstanceMode.runsettings --filter "TestCategory=SingleInstanceMode"
RUN dotnet test ./ImpowerSurvey.Tests/ImpowerSurvey.Tests.csproj --filter "TestCategory!=MultiInstanceMode&TestCategory!=SingleInstanceMode"
RUN dotnet build ./ImpowerSurvey/ImpowerSurvey.csproj --no-restore -c Release -o /app/build
RUN dotnet publish ./ImpowerSurvey/ImpowerSurvey.csproj --no-restore -c Release -o /app/publish

# Run stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV PORT=8080
ENV ASPNETCORE_URLS=http://[::]:${PORT}
ENV ASPNETCORE_HTTPS_PORT=443
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE ${PORT}
ENTRYPOINT ["dotnet", "ImpowerSurvey.dll"]