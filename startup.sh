#!/bin/bash

# fail on errors
set -e;

function dotnet_restore() {
    should_restore_flag="$1";
    if [ ${#should_restore_flag} -gt 0 ]; then
        if [ "$should_restore_flag" = "1" ]; then
            dotnet restore;
        else
            echo "Skipping dotnet restore...";
        fi
    else 
        # if no flag is passed, attempt to restore
        dotnet restore;
    fi     
}

function dotnet_publish() {
    should_publish_flag="$1"
    if [ ${#should_publish_flag} -gt 0 ]; then
        if [ "$should_publish_flag" = "1" ]; then
            dotnet publish -c Release -o out;
        else
            echo "Skipping dotnet publish...";
        fi
    else 
        # if no flag is passed, attempt to publish
        dotnet publish -c Release -o out;
    fi     
}

function wait_for_services() {
    >&2 echo "Waiting for FactionDB to start..."
    ./wait-for-it.sh db:5432 -- echo "FactionDB Started..";

    >&2 echo "Waiting for RabbitMQ to start..."
    ./wait-for-it.sh mq:5672 -- echo "RabbitMQ Started..";
}

function dotnet_run_watch() {
    wait_for_services;
    echo "Running Core and watching for changes...";
    dotnet watch run;
}

function dotnet_run_published() {
    wait_for_services;
    if [ "$DOCKER_PUBLISH_ON_RUN" = "1" ]; then 
        dotnet_publish;
    fi
    echo "Running published assembly...";
    dotnet run out/FactionCore.dll;
}

function dotnet_run() {
    run_target="$1";
    echo "Running target: $run_target";

    if [ "$run_target" = "watch" ]; then
        dotnet_run_watch;
    elif [ "$run_target" = "published" ]; then
        dotnet_run_published;
    else
        dotnet_run_published;
    fi
}

if [ "$1" = "restore" ]; then
    should_restore_flag="$2";
    dotnet_restore $should_restore_flag;
elif [ "$1" = "publish" ]; then
    should_publish_flag="$2";
    dotnet_publish $should_publish_flag;
else 
    run_target="published";
    # check if an argument was passed in
    if [ ${#2} -gt 0 ]; then 
        run_target="$2"
    elif [ ${#DOCKER_RUN_TARGET} -gt 0 ]; then
        run_target="$DOCKER_RUN_TARGET";
    fi
    dotnet_run $run_target;
fi