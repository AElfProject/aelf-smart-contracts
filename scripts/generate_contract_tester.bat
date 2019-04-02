SET scriptdir=%~dp0

protoc --proto_path=../protobuf ^
--csharp_out=internal_access:./Protobuf\Generated ^
--csharp_opt=file_extension=.g.cs ^
--contract_opt=tester ^
--contract_opt=internal_access ^
--contract_out=./Protobuf/Generated ^
--plugin=protoc-gen-contract=contract_csharp_plugin.exe ^
%*
