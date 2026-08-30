# Code signing policy

## Status

Frame Performance Analyzer is applying for the SignPath Foundation Open Source Code Signing program.

Once the project is approved, release binaries will use **Free code signing provided by SignPath.io, certificate by SignPath Foundation**.

Until approval and pipeline integration are complete, existing release binaries remain unsigned and Windows SmartScreen may display an unknown-publisher warning.

## Signing scope

Only official Frame Performance Analyzer release artifacts built from this repository are eligible for signing.

- Source repository: `https://github.com/StreckerMX/Frame-Performance-Analyzer`
- Signed product: `Frame Performance Analyzer`
- Primary signed binary: `FramePerformanceAnalyzer.exe`
- Build system: GitHub Actions on GitHub-hosted Windows runners
- Release source: the repository's versioned release workflow

The project will not use the SignPath Foundation certificate to sign unrelated projects, locally modified third-party binaries, or artifacts that cannot be traced to this repository and its build workflow.

## Team roles

Frame Performance Analyzer is currently maintained by a single project owner.

- **Committer / author:** [StreckerMX](https://github.com/StreckerMX)
- **Reviewer:** [StreckerMX](https://github.com/StreckerMX)
- **Signing approver:** [StreckerMX](https://github.com/StreckerMX)

Changes submitted by external contributors must be reviewed before they are merged. Release signing requests will require explicit approval under the SignPath signing policy.

## Build and release integrity

Official Windows releases are produced by GitHub Actions from repository-controlled build scripts. The release workflow restores dependencies, builds the solution, runs the automated test suite, publishes the self-contained Windows x64 executable, packages the distribution, and publishes a SHA-256 checksum alongside the release archive.

After SignPath integration is enabled, signing will happen before the final ZIP and checksum are produced. The workflow will verify the signed artifact before publishing the GitHub Release.

## Privacy

Frame Performance Analyzer does not include telemetry and does not make network calls during normal operation.

**This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.**

Application settings, benchmark metadata, Library records, and logs are stored locally on the user's computer. See the release documentation for the current storage locations.

## System changes and uninstall

Frame Performance Analyzer is distributed as a portable, self-contained Windows x64 application. It does not require an installer and does not modify system configuration as part of installation.

To uninstall it, delete the extracted application folder. Local application data can optionally be removed from the locations documented in `docs/RELEASE-README.md`.

## Verification

Official releases are published through GitHub Releases. Each release includes a SHA-256 checksum for the downloadable ZIP. Once SignPath signing is active, the Windows executable will additionally carry an Authenticode signature backed by the SignPath Foundation certificate and traceable to the approved GitHub build workflow.
