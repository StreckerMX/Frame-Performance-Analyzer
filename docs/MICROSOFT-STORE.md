# Microsoft Store publishing (MSIX)

FrameView Analyzer is published to GitHub as a self-contained single-file Windows app. For Microsoft Store distribution, the same application payload is wrapped in an MSIX package so the Store can certify and sign it.

## Why MSIX

For Microsoft Store MSIX/AppX submissions, Microsoft signs the package after certification. A separate CA-trusted code-signing certificate is not required for the Store-distributed copy.

The GitHub ZIP remains a separate distribution channel and is not signed by the Store.

## Repository support

The Store package is intentionally separate from the normal GitHub release pipeline:

- `packaging/store/AppxManifest.xml.template` contains the Store package manifest.
- `scripts/build-store-msix.ps1` publishes the existing WPF app and creates an unsigned x64 `.msix` ready for Partner Center upload.
- `.github/workflows/store-msix-ci.yml` validates the packaging process with a non-production identity on pull requests.
- Generated Store artifacts are written under `artifacts/store/` and remain ignored by Git.

The Store package uses the same self-contained, single-file application payload as the normal release. Users therefore do not need to install the .NET runtime separately.

## Partner Center identity required

Before producing the real Store artifact, copy the following values exactly from **Partner Center → Product identity**:

1. **Package/Identity/Name**
2. **Package/Identity/Publisher**
3. **Package/Properties/PublisherDisplayName**

Do not invent or normalize these strings. The manifest identity used in the uploaded MSIX must match the identity assigned by Partner Center.

## Build the Store package

From PowerShell 7 at the repository root:

```powershell
./scripts/build-store-msix.ps1 `
  -PackageIdentityName "<Partner Center Package/Identity/Name>" `
  -Publisher "<Partner Center Package/Identity/Publisher>" `
  -PublisherDisplayName "<Partner Center PublisherDisplayName>"
```

The script resolves the product version from `Directory.Build.props`. Version `3.0.0` becomes MSIX package version `3.0.0.0`; the fourth component stays `0` for Microsoft Store use.

Output:

```text
artifacts/store/FrameViewAnalyzer-Store-3.0.0.0-x64.msix
artifacts/store/FrameViewAnalyzer-Store-3.0.0.0-x64.msix.sha256
```

The MSIX is deliberately unsigned. Do not purchase a certificate just to submit this package to Microsoft Store. Partner Center signs the Store-distributed package after it passes certification.

## Partner Center submission checklist

After the production MSIX is generated:

1. Start a new submission for the reserved **FrameView Analyzer** product.
2. Complete pricing and availability.
3. Complete product properties/category.
4. Complete the age-ratings questionnaire.
5. Upload the generated `.msix` on the **Packages** page.
6. Complete the Store listing: description, features, screenshots, icons, and contact/support information as requested.
7. Review submission options and add certification notes if useful.
8. Submit for certification.

Before final submission, run the Windows App Certification Kit against the production package when possible and test installation/launch behavior on a clean Windows machine.

## Updating the Store later

For a later product release, bump the application version normally in `Directory.Build.props`, build a new MSIX, and submit it as an update in Partner Center. For example:

- App `3.0.1` → MSIX `3.0.1.0`
- App `3.1.0` → MSIX `3.1.0.0`
- App `4.0.0` → MSIX `4.0.0.0`

Never reuse a lower package version for a Store update.
