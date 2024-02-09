FROM mcr.microsoft.com/dotnet/sdk:8.0 as build
WORKDIR /build

COPY Sok8t.sln .
COPY ./Sok8t/Sok8t.csproj ./Sok8t/Sok8t.csproj
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app /app
ENTRYPOINT ["dotnet", "Sok8t.dll"]
