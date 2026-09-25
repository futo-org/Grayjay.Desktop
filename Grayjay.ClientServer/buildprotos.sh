#!/bin/sh
protoc -I=./Protobuffers --csharp_out=./Protobuffers ./Protobuffers/Chromecast.proto
protoc -I=./Protobuffers --csharp_out=./Protobuffers/sabr --csharp_opt=file_extension=.g.cs ./Protobuffers/sabr/common.proto ./Protobuffers/sabr/ump_parts.proto ./Protobuffers/sabr/video_playback_abr_request.proto
