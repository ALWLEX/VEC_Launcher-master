FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY VEC.AuthServer.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

EXPOSE 8080
VOLUME /app/data

ENTRYPOINT ["dotnet", "VEC.AuthServer.dll"]
