# Publishing to NuGet

Two packages are published together, with one version number (`<Version>` in `Directory.Build.props`):

- `SmsTunnel`: the client (no dependencies);
- `SmsTunnel.Extensions.DependencyInjection`: `services.AddSmsTunnel()`.

They are published by GitHub Actions (`.github/workflows/publish.yml`) with **NuGet Trusted Publishing**: nuget.org
trusts that workflow file, in the repository `narcisbodea/smstunnel-dotnet`, in the GitHub environment `nuget`. Each
run swaps GitHub's short-lived token for a NuGet API key that is valid for one hour. No API key is stored anywhere:
not in GitHub secrets, not on a computer.

Both names were free on 3 November 2026 (`https://api.nuget.org/v3-flatcontainer/smstunnel/index.json` and
`.../smstunnel.extensions.dependencyinjection/index.json` answered 404). NuGet doesn't reserve a name before the first
upload, so do steps 1 to 4 close together.

## 1. nuget.org: the account (once)

1. Open <https://www.nuget.org>, click **Sign in** and sign in with a Microsoft account (create one if needed). Pick
   the profile name (user name) nuget.org asks for, for example `narcisbodea`. This profile name is what the workflow
   needs in step 2.3, not the email address.
2. Make sure two-factor authentication is on for that account.
3. Optional, for packages owned by the company: on nuget.org, **your name → Add Organization**, for example `NARBOWEB`,
   and choose it as the owner of the policy in step 3.

## 2. GitHub: the repository, the `nuget` environment and one variable (once)

1. Create the public repository `narcisbodea/smstunnel-dotnet` and push this folder to its `main` branch.
2. In the repository, **Settings → Environments → New environment**, name it `nuget`, then:
   - **Required reviewers**: add yourself. Every publish waits for your click on **Review deployments → Approve**.
   - **Deployment branches and tags → Selected branches and tags → Add rule**: tag pattern `v*`.
3. **Settings → Secrets and variables → Actions → Variables → New repository variable**: name `NUGET_USER`, value
   your nuget.org profile name from step 1.1. It is not a secret.

## 3. nuget.org: the trusted publishing policy (once)

1. On nuget.org, click your user name (top right) and choose **Trusted Publishing**.
2. Add a policy with exactly these values (they are not case-sensitive):

   | Field | Value |
   | --- | --- |
   | Policy owner | you (or the organization from step 1.3) |
   | Repository Owner | `narcisbodea` |
   | Repository | `smstunnel-dotnet` |
   | Workflow File | `publish.yml` (the file name only, without `.github/workflows/`) |
   | Environment | `nuget` |

   If the form asks which packages and actions the policy covers, allow publishing new packages and new versions for
   `SmsTunnel*` (or for all your packages).
3. Save. A new policy can show as **temporarily active for 7 days** (nuget.org needs the first publish to learn the
   repository's IDs); publish within those days, or restart the 7 days from the same page. After the first successful
   publish it stays active.

## 4. Release 1.0.0

1. Check that `<Version>` in `Directory.Build.props` and the top entry of `CHANGELOG.md` say `1.0.0`.
2. On GitHub: **Releases → Draft a new release → Choose a tag**, type `v1.0.0` (create it on `main`), title `1.0.0`,
   paste the changelog entry, and click **Publish release**.
3. The **Publish** workflow runs: it checks that the tag matches the version, builds, runs the unit tests and packs
   both packages. Then the **Publish to NuGet** job waits for your approval (**Review deployments → nuget →
   Approve and deploy**), logs in to nuget.org and pushes `SmsTunnel`, then `SmsTunnel.Extensions.DependencyInjection`,
   each with its symbol package.
4. nuget.org checks and indexes new packages: usually 5 to 15 minutes, sometimes up to an hour. Until then the package
   page says it isn't indexed yet and `dotnet add package` may not find it.
5. Check <https://www.nuget.org/packages/SmsTunnel> and
   <https://www.nuget.org/packages/SmsTunnel.Extensions.DependencyInjection>, then try it in an empty folder:

   ```bash
   dotnet new console -o try-smstunnel && cd try-smstunnel && dotnet add package SmsTunnel
   ```

nuget.org shows the README, but only shows images from the hosts it trusts (GitHub, shields.io and a few more), so the
Google Play badge from smstunnel.io may not appear there; the text link next to it does.

## Later versions

1. Raise `<Version>` in `Directory.Build.props` (it is shared by both packages) and add a `CHANGELOG.md` entry.
2. Commit, then publish a GitHub release tagged `v<version>`, and approve the deployment.

A version can be pushed only once: a fix always needs a new version number (a re-run skips versions that are already
there). Packages can be unlisted on nuget.org, not deleted.

## Optional

- **ID prefix reservation**: nuget.org can reserve the `SmsTunnel` prefix for the owner, so that no one else can
  publish `SmsTunnel.*` and the packages get a verified check mark. Ask by email as described at
  <https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation>; it takes some days.
- **A dry run**: the same commands as the workflow, on any computer with the .NET SDK:

  ```bash
  dotnet build SmsTunnel.sln -c Release
  dotnet test tests/SmsTunnel.Tests -c Release
  dotnet pack src/SmsTunnel/SmsTunnel.csproj -c Release -o artifacts
  dotnet pack src/SmsTunnel.Extensions.DependencyInjection/SmsTunnel.Extensions.DependencyInjection.csproj -c Release -o artifacts
  ```

## If the publish fails

- **NuGet login fails** (no matching policy): the repository owner, repository, workflow file name or environment on
  nuget.org differs from the run, the policy is no longer active (restart its 7 days), or `NUGET_USER` is empty or
  is an email address instead of the profile name.
- **403 on push**: the package name belongs to another nuget.org account, or the policy owner isn't the package's owner.
- **The Publish to NuGet job never starts**: approve the deployment, or check the `nuget` environment's tag rule.

## What stays manual

Only the steps above: the nuget.org account, the GitHub repository and its settings, the trusted publishing policy,
and publishing a release. Nothing in this repository needs a secret.
