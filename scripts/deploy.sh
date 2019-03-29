#!/bin/bash
set -ev

TAG=$1
DOCKER_USERNAME=$2
DOCKER_PASSWORD=$3
NUGET_API_KEY=$4
VERSION=`echo $TAG | cut -b 2-`


# publish docker
# AElf node
dotnet publish  AElf.sln -o ~/aelf/

docker build -t aelf/node:$TAG ~/aelf/.
docker tag aelf/node:$TAG aelf/node:latest
docker login -u="$DOCKER_USERNAME" -p="$DOCKER_PASSWORD"
docker push aelf/node:$TAG
docker push aelf/node:latest


# publish nuget
# build
for name in `ls -lh | grep ^d | grep AElf |grep -v Test|awk '{print $NF}'`;
do
  dotnet build ${name}/${name}.csproj --configuration Release -P:Version=${VERSION} -P:Authors=AElf -o ../
done

dotnet build AElf.Contracts.TestKit/AElf.Contracts.TestKit.csproj --configuration Release -P:Version=${VERSION} -P:Authors=AElf -o ../


# push
for name  in `ls *.nupkg`;
do
  echo $name
  dotnet nuget push ${name}  -k $NUGET_API_KEY  -s https://api.nuget.org/v3/index.json
done
