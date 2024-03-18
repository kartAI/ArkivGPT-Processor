# Use the Microsoft .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /service

# Copy csproj and restore any dependencies (via NuGet)
COPY ./ArkivGPT_Processor/*.csproj ./
RUN dotnet restore

# Copy the project files and build our release
COPY ./ArkivGPT_Processor/ ./
RUN dotnet publish -c Release -o out

# Generate runtime image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dev-env
WORKDIR /service
COPY --from=build-env /service/out .
COPY GPT.* ./../

RUN dotnet dev-certs https --trust

# Start the gRPC server
ENTRYPOINT ["dotnet", "ArkivGPT_Processor.dll"]
