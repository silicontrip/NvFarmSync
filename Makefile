SSH_USER    ?= S_MHeath
REMOTE_DIR  ?= C:\Users\$(SSH_USER)\bin
PROJECT_DIR := src/NvFarmSync
PUBLISH_DIR := $(PROJECT_DIR)/bin/Release/net8.0-windows/win-x64/publish
STAMP_DIR   := .stamp
ZIP         := $(STAMP_DIR)/NvFarmSync.zip

HOSTS := $(shell grep -v '^\s*\#' hosts.txt 2>/dev/null | grep -v '^\s*$$')

.PHONY: publish deploy clean

publish: $(STAMP_DIR)/publish

# Self-contained + single-file: the fleet's system-wide .NET install can't
# be trusted to be consistent (seen versions from 6.0 through 8.0 across
# otherwise-identical hosts), and fixing that means touching shared system
# state on every machine for this tool's sake. Bundling the runtime instead
# means every host runs the exact same bits regardless of what else is
# installed there.
$(STAMP_DIR)/publish: $(shell find $(PROJECT_DIR) -name '*.cs' -o -name '*.csproj') | $(STAMP_DIR)
	dotnet publish $(PROJECT_DIR) -r win-x64
	touch $@

$(ZIP): $(STAMP_DIR)/publish
	rm -f $(ZIP)
	zip -qj -X $(ZIP) $(PUBLISH_DIR)/*

# One stamp file per host. Make only re-deploys to a host when the zip
# is newer than that host's last successful deploy.
$(STAMP_DIR)/deployed-%: $(ZIP)
	scripts/deploy-host.sh $* $(SSH_USER) '$(REMOTE_DIR)' $(ZIP)
	touch $@

deploy: $(addprefix $(STAMP_DIR)/deployed-,$(HOSTS))

$(STAMP_DIR):
	mkdir -p $(STAMP_DIR)

clean:
	rm -rf $(STAMP_DIR)
