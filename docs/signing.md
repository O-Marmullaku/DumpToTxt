# Windows signing with Azure Artifact Signing

`tools/build.ps1 -Flavor all -Installer -Sign` signs each edition's application
before packaging it, then uses Inno Setup's signing hook to sign setup and its
embedded uninstaller. Each signed build gets a separate uninstaller work directory.
SHA-256 signatures and RFC3161 timestamps are required. Signing or verification
failure stops the build; there is no unsigned fallback in signing mode.

The ordinary build without `-Sign` remains available for development. Adding
this integration does not establish Azure account approval, a successful cloud
signature, installed Windows acceptance, or a signed public release. Keep the
website's unsigned-release disclosure until signed binaries are actually released.

## Azure setup

Use a paid Azure subscription; free, trial and sponsored subscriptions are not
supported for Artifact Signing. Review the current Basic tier price before creating
an account. A Microsoft Store/Partner Center account does not establish that an
Azure subscription or Artifact Signing identity validation exists.

1. Create an Artifact Signing account in **Switzerland North**, using **Basic**
   and **Public Trust**. The matching endpoint is
   `https://swn.codesigning.azure.net`.
2. Submit **organization** identity validation using the business's legal records.
   Review the certificate subject preview: this verified identity becomes public
   in signatures. Business/identity documents and account recovery information
   belong in private business custody, never this repository.
3. After Microsoft approves validation, create a Public Trust certificate profile
   for DumpToTxt. Signing cannot work while validation is pending.
4. Create a dedicated Microsoft Entra app registration/service principal for this
   repository. Use a federated credential, not a client secret or private-key export:

   | Field | Value |
   | --- | --- |
   | Issuer | `https://token.actions.githubusercontent.com` |
   | Audience | `api://AzureADTokenExchange` |
   | Subject | `repo:O-Marmullaku/DumpToTxt:environment:code-signing` |

5. Assign that principal **Artifact Signing Certificate Profile Signer**, scoped
   to this signing account. Do not grant subscription Contributor/Owner for builds.
6. Create the GitHub environment **code-signing**, allow deployment from **main
   only**, and configure owner review before granting it signing access. The workflow
   is manual and also rejects refs other than `main`; the environment branch policy
   is required because its OIDC subject is bound to the environment.
7. Add these **environment variables** in GitHub. These identifiers are not passwords:

   | Variable | Value from Azure |
   | --- | --- |
   | `AZURE_CLIENT_ID` | Dedicated app registration's application/client ID |
   | `AZURE_TENANT_ID` | Business tenant/directory ID |
   | `AZURE_SUBSCRIPTION_ID` | Paid subscription ID |
   | `ARTIFACT_SIGNING_ENDPOINT` | Account's regional endpoint |
   | `ARTIFACT_SIGNING_ACCOUNT` | Signing account name |
   | `ARTIFACT_SIGNING_PROFILE` | Approved Public Trust certificate profile name |

Record the account, tenant, subscription, profile, federated subject and role scope
in private recovery custody. Microsoft keeps the signing private key; this setup
does not create a portable signing-key backup. Preserve identity/access recovery
and record that provider boundary honestly.

## Automated build

Run **Build signed Windows packages** from GitHub Actions on `main` and approve
the `code-signing` environment. The workflow uses short-lived GitHub OIDC login
and Microsoft's SignTool/dlib integration, including Inno's uninstaller signing
hook. It pins its actions and signing-client dependencies and uses no signing-key
secret. Source checks run before Azure login.

All three app executables, installers and embedded uninstallers must pass
SignTool's Authenticode/all-signatures/timestamp checks and PowerShell's trusted
signature/timestamp checks. The signing hook verifies the temporary uninstaller
before Inno embeds it and deletes the temporary file. Only then does the workflow stage installer checksums
and save the candidates as a GitHub Actions artifact. The checksum manifest is
generated from the signed bytes, not the earlier unsigned files.

The workflow does not publish a GitHub release or npm package. Download and test
these exact signed candidates in the disposable Windows VM described in
[development](development.md#release-acceptance), then follow the normal release
procedure. A previously published version must not be overwritten with different
installer hashes; use the next release version.

## Local signed build

Install Azure CLI, .NET 8, a supported x64 Windows SDK SignTool and Microsoft's
Artifact Signing client dlib. The workflow lists the pinned dependency versions.
Log in with an identity granted the signing role. Use an absolute metadata JSON
path outside the source checkout:

```json
{
  "Endpoint": "https://swn.codesigning.azure.net",
  "CodeSigningAccountName": "YOUR_ACCOUNT",
  "CertificateProfileName": "YOUR_PROFILE",
  "ExcludeCredentials": [
    "EnvironmentCredential", "WorkloadIdentityCredential", "ManagedIdentityCredential",
    "SharedTokenCacheCredential", "VisualStudioCredential", "VisualStudioCodeCredential",
    "AzurePowerShellCredential", "AzureDeveloperCliCredential", "InteractiveBrowserCredential"
  ]
}
```

Set `DUMPTOTXT_SIGNTOOL` to x64 `signtool.exe`, `DUMPTOTXT_SIGNING_DLIB` to the
matching x64 `Azure.CodeSigning.Dlib.dll`, and `DUMPTOTXT_SIGNING_METADATA` to that
JSON file. Then run:

```powershell
pwsh -NoProfile -File tools/build.ps1 -Flavor all -Installer -Sign
pwsh -NoProfile -File tools/prepare-npm.ps1
```

The second command stages files and hashes; it does not publish them. A lone file
can be reverified with `tools/sign.ps1 -Path <absolute-path> -VerifyOnly`.

## References

- [Artifact Signing setup and eligible countries](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)
- [Subscription restrictions](https://learn.microsoft.com/en-us/azure/artifact-signing/faq)
- [Pricing](https://azure.microsoft.com/en-us/pricing/details/artifact-signing/)
- [Microsoft SignTool/dlib setup and timestamp requirements](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations)
- [GitHub OIDC with Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure)
- [Inno Setup signing hook](https://jrsoftware.org/ishelp/topic_setup_signtool.htm)
- [Signed uninstallers](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)
