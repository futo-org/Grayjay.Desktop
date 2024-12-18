#!/bin/sh
protoc -I=./Protobuffers --csharp_out=./Protobuffers ./Protobuffers/Chromecast.proto
