#!/bin/bash

set -e

>&2 echo "Removing build directories.."
rm -rf ./bin
rm -rf ./obj

>&2 echo "Waiting for FactionDB to start..."
./wait-for-it.sh db:5432 -- echo "FactionDB Started.."

>&2 echo "Waiting for RabbitMQ to start..."
./wait-for-it.sh mq:5672 -- echo "RabbitMQ Started.."

echo "Starting Core.."
dotnet run out/FactionCore.dll