FROM mcr.microsoft.com/dotnet/core/sdk:3.0
WORKDIR /app
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
# copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# copy and build everything else
COPY . ./
RUN dotnet tool install --global dotnet-ef --version 3.0.0
RUN dotnet publish -c Release -o out
RUN chmod 777 ./wait-for-it.sh
ENTRYPOINT [ "/bin/bash", "startup.sh" ]