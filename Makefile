.PHONY: verify

# Local quality gate (mirrors ci.yml): hard-fails on any format/analyzer violation, failing test,
# or line coverage under the minimum. Integration tests need Docker (Testcontainers); coverage is
# measured over unit + integration together, like the Python project.
COVERAGE_MIN ?= 80

verify:
	dotnet format --verify-no-changes
	dotnet build -c Release -warnaserror
	dotnet test src/Test/OrderApi.Test -c Release --no-build -p:CollectCoverage=true -p:CoverletOutput=../../../coverage/unit -p:CoverletOutputFormat=json
	dotnet test src/Test/OrderApi.IntegrationTest -c Release --no-build -p:CollectCoverage=true -p:CoverletOutput=../../../coverage/merged -p:CoverletOutputFormat=json -p:MergeWith=$(CURDIR)/coverage/unit.json -p:Threshold=$(COVERAGE_MIN) -p:ThresholdStat=total
