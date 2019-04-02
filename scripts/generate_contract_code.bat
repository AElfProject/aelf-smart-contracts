SET scriptdir=%~dp0

protoc --proto_path=../protobuf ^
--csharp_out=./Protobuf\Generated ^
--csharp_opt=file_extension=.g.cs ^
--contract_out=./Protobuf/Generated ^
--plugin=protoc-gen-contract=contract_csharp_plugin.exe ^
%*
