FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Discord Ban/Discord Ban.csproj", "Discord Ban/"]
RUN dotnet restore "Discord Ban/Discord Ban.csproj"
COPY . .
WORKDIR "/src/Discord Ban"
RUN dotnet build "Discord Ban.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Discord Ban.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Discord Ban.dll"]
