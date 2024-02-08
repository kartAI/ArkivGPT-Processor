# Use the Microsoft .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /service

# Copy csproj and restore any dependencies (via NuGet)
COPY *.csproj ./
RUN dotnet restore

# Copy the project files and build our release
COPY . ./
RUN dotnet publish -c Release -o out

# Generate runtime image
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dev-env
WORKDIR /service
COPY --from=build-env /service/out .

RUN dotnet dev-certs https --trust

# Start the gRPC server
ENTRYPOINT ["dotnet", "GrpcGreeter.dll"]
