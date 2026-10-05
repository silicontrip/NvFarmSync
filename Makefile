SSH_USER    ?= S_MHeath
REMOTE_DIR  ?= C:\Users\$(SSH_USER)\bin
STAMP_DIR   := .stamp
TOOLS       := NvFarmSync NvMosaic NvGSync

HOSTS := $(shell grep -v '^\s*\#' hosts.txt 2>/dev/null | grep -v '^\s*$$')

.PHONY: publish deploy clean $(addprefix publish-,$(TOOLS)) $(addprefix deploy-,$(TOOLS))

publish: $(addprefix publish-,$(TOOLS))
deploy: $(addprefix deploy-,$(TOOLS))

# One instantiation of this per tool -- publish-<tool>, deploy-<tool>, and
# per-host deploy stamps, all following the same self-contained-single-file
# / zip-and-push shape as NvFarmSync originally had hardcoded. Adding a new
# tool (e.g. NvGSync later) means adding its name to $(TOOLS) above and one
# more $(eval $(call TOOL_RULES,...)) line below.
#
# Deliberately not $(foreach t,$(TOOLS),$(eval $(call TOOL_RULES,$(t))))
# here: that one-liner breaks unpredictably ("commands commence before
# first target") for some tool names and not others, for reasons that
# didn't trace back to anything wrong in the generated text itself
# (confirmed identical via $(info)) -- explicit per-tool eval calls don't
# have the problem, so that's what's used.
define TOOL_RULES
publish-$(1): $(STAMP_DIR)/publish-$(1)

$(STAMP_DIR)/publish-$(1): $(wildcard src/$(1)/*.cs) src/$(1)/$(1).csproj | $(STAMP_DIR)
	dotnet publish src/$(1) -r win-x64
	touch $$@

$(STAMP_DIR)/$(1).zip: $(STAMP_DIR)/publish-$(1)
	rm -f $$@
	zip -qj -X $$@ src/$(1)/bin/Release/net8.0-windows/win-x64/publish/*

$(STAMP_DIR)/deployed-$(1)-%: $(STAMP_DIR)/$(1).zip
	scripts/deploy-host.sh $$* $(SSH_USER) '$(REMOTE_DIR)' $(STAMP_DIR)/$(1).zip
	touch $$@

deploy-$(1): $(addprefix $(STAMP_DIR)/deployed-$(1)-,$(HOSTS))
endef

$(eval $(call TOOL_RULES,NvFarmSync))
$(eval $(call TOOL_RULES,NvMosaic))
$(eval $(call TOOL_RULES,NvGSync))

$(STAMP_DIR):
	mkdir -p $(STAMP_DIR)

clean:
	rm -rf $(STAMP_DIR)
