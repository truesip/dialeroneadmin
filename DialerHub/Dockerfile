# Multi-stage Dockerfile for DialerHub
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
# App Platform expects the container to listen on port 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DialerHub.dll"]
