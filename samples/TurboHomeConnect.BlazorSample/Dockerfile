# Build context must be the repo root.
#
#   docker compose up --build dashboard

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything in one shot. Splitting restore from publish loses the framework's
# static-web-assets manifest under the .NET 10 SDK Web pipeline — wwwroot/_framework
# (including blazor.web.js) ends up missing from the publish output, so Blazor's
# interactive circuit can't bootstrap in the browser.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TurboHomeConnect/ src/TurboHomeConnect/
COPY samples/TurboHomeConnect.BlazorSample/ samples/TurboHomeConnect.BlazorSample/

RUN dotnet publish samples/TurboHomeConnect.BlazorSample/TurboHomeConnect.BlazorSample.csproj \
    -c Release \
    -o /app \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /data && chmod 700 /data
VOLUME ["/data"]

COPY --from=build /app ./

EXPOSE 5099

ENV ASPNETCORE_URLS=http://+:5099

ENTRYPOINT ["dotnet", "TurboHomeConnect.BlazorSample.dll"]
