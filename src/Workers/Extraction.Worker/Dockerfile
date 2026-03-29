FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Northwoods.slnx .
COPY BuildingBlocks/Northwoods.Contracts/Northwoods.Contracts.csproj BuildingBlocks/Northwoods.Contracts/
COPY BuildingBlocks/Northwoods.Tenancy/Northwoods.Tenancy.csproj BuildingBlocks/Northwoods.Tenancy/
COPY Workers/Extraction.Worker/Extraction.Worker.csproj Workers/Extraction.Worker/
RUN dotnet restore Workers/Extraction.Worker/Extraction.Worker.csproj

COPY BuildingBlocks/ BuildingBlocks/
COPY Workers/Extraction.Worker/ Workers/Extraction.Worker/
RUN dotnet publish Workers/Extraction.Worker/Extraction.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Extraction.Worker.dll"]
