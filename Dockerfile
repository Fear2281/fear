# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0.425 AS build
WORKDIR /src
COPY Encore.csproj packages.lock.json global.json Directory.Build.props ./
# Optional build secret is only needed in environments with a TLS inspection proxy.
RUN --mount=type=secret,id=proxy_ca_bundle \
    if [ -f /run/secrets/proxy_ca_bundle ]; then export SSL_CERT_FILE=/run/secrets/proxy_ca_bundle; fi; \
    dotnet restore Encore.csproj --locked-mode
COPY . .
RUN dotnet publish Encore.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ENCORE_DATA_DIR=/data
RUN mkdir -p /data && chown app:app /data
USER app
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "Encore.dll"]
