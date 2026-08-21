# Versioning

One git tag produces exactly two numbers, and `tools/release/Get-ReleaseVersion.ps1` is the
only thing that produces them. Nothing else in the repository is allowed to invent a
version.

| MSBuild property | Android | iOS | Comes from |
| --- | --- | --- | --- |
| `ApplicationDisplayVersion` | `versionName` | `CFBundleShortVersionString` | `major.minor.patch` from the tag |
| `ApplicationVersion` | `versionCode` | `CFBundleVersion` | the derived build number below |

`Directory.Build.props` supplies development defaults (`0.1.0` / `1`) so a local build works.
The release workflow overrides both from the tag.

## Tag grammar

```
v<major>.<minor>.<patch>            production release
v<major>.<minor>.<patch>-rc.<n>     release candidate, n = 1..89
v<major>.<minor>.<patch>+<n>        re-upload of an already-tagged release, n = 1..9
```

Anything else is rejected before a build starts. `v1.0`, `v1.0.0.1`, `v1.0.0-beta.1` and a
bare `1.0.0` all fail in the first job, in seconds.

The display version is always exactly three integers. That is not stylistic: Apple rejects a
`CFBundleShortVersionString` that is not one to three dot-separated integers, so a
prerelease label can never appear in it.

## Build number

```
versionCode = major * 1000000 + minor * 10000 + patch * 100 + revision
```

`revision` is derived, never written by hand:

| Tag | revision | Meaning |
| --- | ---: | --- |
| `v1.2.3-rc.1` … `-rc.89` | 1 … 89 | release candidates, below the release they lead to |
| `v1.2.3` | 90 | the production release |
| `v1.2.3+1` … `+9` | 91 … 99 | re-uploads of that release |

Worked example of a normal release life, all strictly increasing:

| Tag | Build number |
| --- | ---: |
| `v1.0.0-rc.1` | 1000001 |
| `v1.0.0-rc.2` | 1000002 |
| `v1.0.0` | 1000090 |
| `v1.0.0+1` | 1000091 |
| `v1.0.1-rc.1` | 1000101 |
| `v1.0.1` | 1000190 |
| `v1.1.0` | 1010090 |
| `v2.0.0` | 2000090 |

`Get-ReleaseVersion.ps1 -SelfTest` asserts exactly this: that a representative tag sequence
is strictly increasing and that thirteen malformed tags are rejected. The release workflow
runs it on every release, so the property is checked rather than assumed.

## Why not the CI run number

The workflow this replaced used `github.run_number` as the build number. That is monotonic
*for a workflow*, which is not the same as monotonic for a package id:

* it advances when somebody re-runs a failed job, so the same commit can produce two
  different store builds;
* it resets to 1 if the workflow file is renamed or the repository is migrated;
* it carries no relationship to the version a user sees, so a Play Console build number
  cannot be traced back to a release without opening the run.

Google Play remembers every `versionCode` it has ever accepted for a package, forever. A
counter that can reset is a trap that springs exactly once and cannot be undone: the only
fix is to burn version numbers upward until you clear the old high-water mark.

Deriving the number from the tag removes all of that. The same tag always produces the same
build, so re-running a release job is safe, and the number is readable: `1040203` decodes by
eye as `1.04.02` revision `03`.

## Limits, and what happens at them

| Field | Range | Enforced by |
| --- | --- | --- |
| major | 0 – 2099 | `Get-ReleaseVersion.ps1`, because Play caps `versionCode` at 2100000000 |
| minor | 0 – 99 | same |
| patch | 0 – 99 | same |
| release candidates per version | 89 | same |
| re-uploads per version | 9 | same |

Hitting any of these means bumping the next component up. The cost of that is a version
number nobody minds; the benefit is a build number a human can read in the Play Console.

## Doing it by hand

```powershell
pwsh tools/release/Get-ReleaseVersion.ps1 -Tag v1.0.0-rc.3
pwsh tools/release/Get-ReleaseVersion.ps1 -Tag v1.0.0 -Format Json
pwsh tools/release/Get-ReleaseVersion.ps1 -SelfTest
```

A local build with release version stamping:

```powershell
dotnet publish src/Forge.App/Forge.App.csproj -f net10.0-android -c Release `
  -p:AndroidPackageFormats=aab `
  -p:ApplicationDisplayVersion=1.0.0 `
  -p:ApplicationVersion=1000090
```
