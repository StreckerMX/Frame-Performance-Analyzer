# Microsoft Store publishing (MSIX)

FrameView Analyzer is published to GitHub as a self-contained single-file Windows app. For Microsoft Store distribution, the same application payload is wrapped in an MSIX package so the Store can certify and sign it.

## Why MSIX

For Microsoft Store MSIX/AppX submissions, Microsoft signs the package after certification. A separate CA-trusted code-signing certificate is not required for the Store-distributed copy.

The GitHub ZIP remains a separate distribution channel and is not signed by the Store.

## Repository support

The Store package is intentionally separate from the normal GitHub release pipeline:

- `packaging/store/AppxManifest.xml.template` contains the Store package manifest.
- `packaging/store/StoreIdentity.json` stores the public Partner Center identity assigned to FrameView Analyzer.
- `scripts/build-store-msix.ps1` publishes the existing WPF app and creates an unsigned x64 `.msix` ready for Partner Center upload.
- `.github/workflows/store-msix-ci.yml` builds the real production-identity package on pull requests and exposes it as a temporary workflow artifact.
- Generated Store artifacts are written under `artifacts/store/` and remain ignored by Git.

The Store package uses the same self-contained, single-file application payload as the normal release. Users therefore do not need to install the .NET runtime separately.

## Partner Center identity

The production identity was copied directly from **Partner Center → Product identity** and is tracked in `packaging/store/StoreIdentity.json`:

- **Package/Identity/Name:** `Strecker.FrameViewAnalyzer`
- **Package/Identity/Publisher:** `CN=A37E4A45-43E1-42F1-866D-B4B9249062DE`
- **Package/Properties/PublisherDisplayName:** `Strecker`
- **Package Family Name:** `Strecker.FrameViewAnalyzer_9aqbg1gb4p26y`
- **Store ID:** `9P49TT4BJ798`

These values are public Store identity data. Do not normalize, shorten, or replace them when building the production package.

## Build the Store package

From PowerShell 7 at the repository root:

```powershell
./scripts/build-store-msix.ps1
```

The script reads the real Store identity automatically from `packaging/store/StoreIdentity.json`.

The script resolves the product version from `Directory.Build.props`. Version `3.0.0` becomes MSIX package version `3.0.0.0`; the fourth component stays `0` for Microsoft Store use.

Output:

```text
artifacts/store/FrameViewAnalyzer-Store-3.0.0.0-x64.msix
artifacts/store/FrameViewAnalyzer-Store-3.0.0.0-x64.msix.sha256
```

The MSIX is deliberately unsigned. Do not purchase a certificate just to submit this package to Microsoft Store. Partner Center signs the Store-distributed package after it passes certification.

For validation scenarios, the three identity arguments can still be supplied explicitly to override the tracked production identity.

## Partner Center submission checklist

After the production MSIX is generated:

1. On the reserved **FrameView Analyzer** product overview, select **Start submission**.
2. Complete **Pricing and availability**.
3. Complete **Properties** and choose the appropriate app category.
4. Complete the **Age ratings** questionnaire.
5. Upload `FrameViewAnalyzer-Store-3.0.0.0-x64.msix` on the **Packages** page.
6. Complete the **Store listings** page with description, features, screenshots, logos, and support/contact data where requested.
7. Review **Submission options** and add certification notes if useful.
8. Submit the completed draft for certification.

Partner Center accepts `.msix` directly. Microsoft recommends `.msixupload` for some Store packaging workflows, but `.msix` remains a supported submission package format.

Before final submission, run the Windows App Certification Kit against the production package when possible and test installation/launch behavior on a clean Windows machine.

## Updating the Store later

For a later product release, bump the application version normally in `Directory.Build.props`, build a new MSIX, and submit it as an update in Partner Center. For example:

- App `3.0.1` → MSIX `3.0.1.0`
- App `3.1.0` → MSIX `3.1.0.0`
- App `4.0.0` → MSIX `4.0.0.0`

Never reuse a lower package version for a Store update.
