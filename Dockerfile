# Build context: repository root. Use --target api (default) or --target worker.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
# Project files first, so the restore layer is only invalidated by a dependency change.
COPY src/OrderApi.Domain/OrderApi.Domain.csproj src/OrderApi.Domain/
COPY src/OrderApi.Application/OrderApi.Application.csproj src/OrderApi.Application/
COPY src/OrderApi.Infrastructure/OrderApi.Infrastructure.csproj src/OrderApi.Infrastructure/
COPY src/OrderApi.Api/OrderApi.Api.csproj src/OrderApi.Api/
COPY src/OrderApi.Worker/OrderApi.Worker.csproj src/OrderApi.Worker/
RUN dotnet restore src/OrderApi.Api/OrderApi.Api.csproj \
 && dotnet restore src/OrderApi.Worker/OrderApi.Worker.csproj
COPY src/ src/
RUN dotnet publish src/OrderApi.Api/OrderApi.Api.csproj -c Release -o /out/api --no-restore --no-self-contained \
 && dotnet publish src/OrderApi.Worker/OrderApi.Worker.csproj -c Release -o /out/worker --no-restore --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS api
WORKDIR /app
COPY --from=build /out/api .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderApi.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:11.0@sha256:3fe5f5495141c97986a49d3a4e76061f6225c3f93f94e8a7e522781376193863 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "OrderApi.Worker.dll"]
