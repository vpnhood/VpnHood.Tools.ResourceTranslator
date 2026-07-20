# Publishes the tool to NuGet: verify -> (optional bump) -> push main -> tag -> CI publishes.
#
# Publishing is an explicit act by a developer. The tag is the trigger and the single source of
# truth for the released version: .github/workflows/publish.yml packs with -p:Version taken from
# the tag name, so nothing here needs to hand CI a version separately.
#
# No API key is involved anywhere. The workflow uses nuget.org Trusted Publishing: it requests a
# GitHub OIDC token, exchanges it via NuGet/login@v1 for a key valid for one hour, and pushes with
# that. The only repository secret is NUGET_USER (the nuget.org profile name).
#
# Flow:
#   1. Refuse a dirty tree, and refuse to publish from anywhere but main.
#   2. Pull main (picks up anything published from another machine).
#   3. Resolve the version from Directory.Build.props, or set it when -Version is passed, and
#      commit that bump.
#   4. Run the tests locally. A tag is awkward to retract once pushed, so a broken build should
#      fail here rather than after the tag exists.
#   5. Push main, then push an annotated tag v<version>, which starts the publish workflow.
#
# Usage:
#   ./_publish.ps1                  # publish the version already in Directory.Build.props
#   ./_publish.ps1 -Version 1.2.0   # bump to 1.2.0, commit, then publish
#   ./_publish.ps1 -SkipTests       # skip the local test run (CI still runs them)
#
# Requires the GitHub CLI (gh) authenticated for the vpnhood org.

param(
	# Set VersionPrefix in Directory.Build.props to this value and commit it before publishing.
	# Omit to publish whatever version is already committed there.
	[string]$Version,

	# Skip the local test run. CI runs the tests regardless; this only removes the local gate.
	[switch]$SkipTests
);

$ErrorActionPreference = "Stop";
$repo = "vpnhood/VpnHood.Tools.ResourceTranslator";
$solution = "VpnHood.Tools.ResourceTranslator.slnx";
$propsFile = "Directory.Build.props";

Push-Location $PSScriptRoot;
try {
	$branch = git branch --show-current;
	if ($branch -ne "main") { throw "_publish: publish from 'main' (currently on '$branch')."; }

	# CI publishes what is on GitHub, not what is on this machine; git pull below needs a clean tree.
	$dirty = git status --porcelain;
	if ($dirty) { throw "_publish: working tree has uncommitted changes; commit or stash them first.`n$($dirty -join "`n")"; }

	git pull origin main --no-rebase;
	if ($LASTEXITCODE -ne 0) { throw "_publish: git pull failed (exit $LASTEXITCODE) — resolve and retry."; }

	# Trusted Publishing needs the nuget.org profile name; without it the login step fails after
	# the tag already exists, which is a confusing place to discover a missing secret.
	$secrets = gh secret list --repo $repo | Out-String;
	if ($secrets -notmatch "NUGET_USER") {
		throw "_publish: repository secret NUGET_USER is not set on $repo — the OIDC login step needs it.";
	}

	# Regex rather than an XML round-trip, so the file's formatting and comments survive untouched.
	$propsText = Get-Content $propsFile -Raw;
	if ($Version) {
		if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "_publish: -Version must look like X.Y.Z (got '$Version')."; }

		$propsText = [regex]::Replace($propsText, '<VersionPrefix>[^<]*</VersionPrefix>', "<VersionPrefix>$Version</VersionPrefix>");
		Set-Content $propsFile -Value $propsText -NoNewline;

		git add $propsFile;
		git commit -m "Publish $Version";
		if ($LASTEXITCODE -ne 0) { throw "_publish: git commit of the version bump failed (exit $LASTEXITCODE)."; }
	}

	if ($propsText -notmatch '<VersionPrefix>([^<]+)</VersionPrefix>') {
		throw "_publish: could not read VersionPrefix from $propsFile.";
	}
	$publishVersion = $Matches[1];
	$tag = "v$publishVersion";

	# A tag is immutable in practice: CI derives the package version from it, and NuGet refuses to
	# overwrite a published version. Refuse up front rather than fail halfway.
	git rev-parse -q --verify "refs/tags/$tag" > $null 2>&1;
	if ($LASTEXITCODE -eq 0) { throw "_publish: tag $tag already exists locally. Bump the version instead of reusing a tag."; }

	$remoteTag = git ls-remote --tags origin "refs/tags/$tag";
	if ($remoteTag) { throw "_publish: tag $tag already exists on origin. Bump the version instead of reusing a tag."; }

	if (-not $SkipTests) {
		Write-Host "Running tests ..." -ForegroundColor Magenta;
		dotnet test $solution --configuration Release;
		if ($LASTEXITCODE -ne 0) { throw "_publish: tests failed (exit $LASTEXITCODE) — nothing was tagged or pushed."; }
	}

	git push origin main;
	if ($LASTEXITCODE -ne 0) { throw "_publish: git push to main failed (exit $LASTEXITCODE)."; }

	git tag -a $tag -m "Publish $publishVersion";
	if ($LASTEXITCODE -ne 0) { throw "_publish: git tag $tag failed (exit $LASTEXITCODE)."; }

	# Pushing the tag is what starts publish.yml.
	git push origin $tag;
	if ($LASTEXITCODE -ne 0) { throw "_publish: git push of tag $tag failed (exit $LASTEXITCODE) — remove it with 'git tag -d $tag' and retry."; }

	Write-Host "Publishing $publishVersion (tag $tag). CI builds, tests, packs and pushes the NuGet via Trusted Publishing." -ForegroundColor Green;
	Start-Sleep -Seconds 5;
	gh run list --repo $repo --workflow publish.yml --limit 1;
	Write-Host "Watch it with: gh run watch --repo $repo" -ForegroundColor DarkGray;
}
finally {
	Pop-Location;
}
