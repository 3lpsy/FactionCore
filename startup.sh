#!/bin/bash

set -e

>&2 echo "Removing build directories.."
rm -rf ./bin
rm -rf ./obj

>&2 echo "Waiting for FactionDB to start..."
./wait-for-it.sh db:5432 -- echo "FactionDB Started.."

>&2 echo "Waiting for RabbitMQ to start..."
./wait-for-it.sh mq:5672 -- echo "RabbitMQ Started.."

>&2 echo "Checking if we need to do an initial migration"
result=$(dotnet ef migrations list)
if [ "$result" == "No migrations were found." ]; then
  echo "Creating Migration"
  dotnet ef migrations add InitialSetup
  dotnet ef database update
else
  echo "Migrations already exist. Not creating a new one."
fi

echo "Starting Core.."
dotnet run out/FactionCore.dll