#!/usr/bin/env bash

FILE_NAME="ChakraCore.dll"
TO_FILE=$1${FILE_NAME}
FROM_FILE=

if [ -d "./ChakraCoreFiless" ]; then
    mkdir -p scripts/.tmp/chakracore/
    cp -r ./ChakraCoreFiles scripts/.tmp/chakracore/
else
    wget https://aka.ms/chakracore/cc_linux_x64_1_11_1
    tar xf cc_linux_x64_1_11_1
fi

if [[ -f "$TO_FILE" ]]; then
    echo "$TO_FILE exists, no need to download again"
    exit
fi

GET_OS()
{
    UNAME_OUTPUT=$(uname -mrsn)

    if [[ "${UNAME_OUTPUT}" =~ 'Darwin' ]]; then
        CURRENT_OS="macOS"
        FROM_FILE="libChakraCore.dylib"
        return
    fi

    if [[ "${UNAME_OUTPUT}" =~ 'Linux' ]]; then
        CURRENT_OS="Linux"
        FROM_FILE="libChakraCore.so"
        return
    fi

    exit
}

GET_OS

WORK_PATH=`pwd`
SCRIPTS_PATH=$(cd "$(dirname "$0")";pwd)
DOWNLOAD_PATH=${SCRIPTS_PATH}/.tmp/chakracore

CHAKRACORE_FILE=${DOWNLOAD_PATH}/ChakraCoreFiles/lib/${FROM_FILE}

if [[ ! -f "$CHAKRACORE_FILE" ]]; then
    rm -rf ${DOWNLOAD_PATH} && mkdir -p ${DOWNLOAD_PATH}
    cd ${DOWNLOAD_PATH}
    bash ${SCRIPTS_PATH}/download_chakracore.sh 1_11_1
fi

cd ${WORK_PATH}
cp ${CHAKRACORE_FILE} ${TO_FILE}
