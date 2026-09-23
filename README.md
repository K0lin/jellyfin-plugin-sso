<h1 align="center">Jellyfin SSO Plugin</h1>

<p align="center">

<img alt="Banner" src="https://raw.githubusercontent.com/k0lin/jellyfin-plugin-sso/main/img/banner/banner.png"/>
<br/>
<br/>
<a href="https://github.com/k0lin/jellyfin-plugin-sso">
<img alt="GPL 3.0 License" src="https://img.shields.io/github/license/k0lin/jellyfin-plugin-sso.svg"/>
</a>
<a href="https://github.com/k0lin/jellyfin-plugin-sso/actions/workflows/dotnet.yml">
<img alt="GitHub Actions Build Status" src="https://github.com/k0lin/jellyfin-plugin-sso/actions/workflows/dotnet.yml/badge.svg"/>
</a>
<a href="https://github.com/k0lin/jellyfin-plugin-sso/releases">
<img alt="Current Release" src="https://img.shields.io/github/release/k0lin/jellyfin-plugin-sso.svg"/>
</a>
<a href="https://github.com/k0lin/jellyfin-plugin-sso/releases.atom">
<img alt="Release RSS Feed" src="https://img.shields.io/badge/rss-releases-ffa500?logo=rss" />
</a>
<a href="https://github.com/k0lin/jellyfin-plugin-sso/commits/main.atom">
<img alt="Main Commits RSS Feed" src="https://img.shields.io/badge/rss-commits-ffa500?logo=rss" />
</a>
</p>

This project has been forked from the [creator's code](https://github.com/9p4/jellyfin-plugin-sso), with the intention of continuing to maintain it and add features. If you use it, please open discussions or issues so we can continue development as best as possible.

## About

This plugin allows users to sign in through an SSO provider (such as Google, Microsoft, or your own provider). This enables one-click signin.

https://user-images.githubusercontent.com/17993169/149681516-f93b43f5-fa5c-4c1f-a909-e5414878a864.mp4

Existing users may link new SSO accounts, or remove existing links using self-service at `/SSOViews/linking`.

## Current State:

This is 100% alpha software! PRs are welcome to improve the code.

**This version (>= 5.1) requires Jellyfin >= 12.0 and only works on the Web UI or clients supporting [Quick Connect](https://jellyfin.org/docs/general/server/quick-connect). For Jellyfin 10.8 - 10.11, use plugin version 5.0.x.**

**This README reflects the branch it is currently on! Switch tags to view version-specific documentation!**

## Tested Providers

[Find provider specific documentation in providers.md](providers.md)

- Authelia
- authentik
- Keycloak
  - OIDC & SAML
- Pocket ID
- Kanidm
- Google OpenID: Works, but usernames are all numeric

## Supported Protocols

- [OpenID](https://openid.net/developers/how-connect-works/)
- [SAML](https://www.cloudflare.com/learning/access-management/what-is-saml/)

## Installing

Add the package repo [https://raw.githubusercontent.com/k0lin/jellyfin-plugin-sso/manifest-release/manifest.json](https://raw.githubusercontent.com/k0lin/jellyfin-plugin-sso/manifest-release/manifest.json) to your Jellyfin plugin repositories.

Then, install the plugin from the plugin catalog!

See [Contributing](#contributing) for instructions on how to build from source.

### (Fallback) Legacy package repo (Versions <= 3.3.0)

We have transitioned to a release system that automates distribution, packaging & hosting.
This system is new, and if something goes wrong, you can try using the old package repository as a fallback.

Instead add the **old** package repository: [https://repo.ersei.net/jellyfin/manifest.json](https://repo.ersei.net/jellyfin/manifest.json) to your jellyfin plugin repositories.

### Installing cutting edge/nightly builds

If you're impatient/brave/feel like helping us test things out, you can install the nightly build of the plugin, which is automatically built against the main branch.

The nightly build can be installed from the [main plugin repo](https://raw.githubusercontent.com/k0lin/jellyfin-plugin-sso/manifest-release/manifest.json), and will always have a version number of `0.0.0.9000`.

The nightly build may have new features unavailable in other builds, but **be warned**, things may change frequently in nightly builds, and things may break, and you could lose data.

## Troubleshooting

### Every `/SSO/...` request returns HTTP 500 after an upgrade

If the Jellyfin log shows an `AmbiguousMatchException` naming the same action twice, for example:

```
Microsoft.AspNetCore.Routing.Matching.AmbiguousMatchException: The request matched multiple endpoints. Matches:

Jellyfin.Plugin.SSO_Auth.Api.SSOController.OidProviders (SSO-Auth)
Jellyfin.Plugin.SSO_Auth.Api.SSOController.OidProviders (SSO-Auth)
```

then the server has loaded two copies of the plugin. Look in the `plugins` folder of your Jellyfin data
directory (`/config/plugins` in the official container image) for more than one `SSO Authentication_*`
directory, for example `SSO Authentication_5.0.0.0` next to `SSO Authentication_5.1.1`. Jellyfin adds every
loaded plugin assembly to its API as a separate application part, so a leftover directory registers the
plugin's controllers a second time and the router can no longer pick a match.

Stop Jellyfin, delete the directory of the older version, and start Jellyfin again. The plugin logs the
paths of all loaded copies at start-up, so the server log names the directory to remove.

Plugin versions up to 5.1.1 report a plugin name (`SSO-Auth`) that differs from the name in the repository
manifest (`SSO Authentication`). Jellyfin can write the reported name into the installed `meta.json` and only
cleans up an older plugin directory when both directories carry the same manifest name, so the old
directory could survive an upgrade. Later versions report the manifest name, which lets Jellyfin's own
clean-up remove the stale directory. Upgrading from an affected version still needs the old directory
removed once by hand.

## Roadmap

- [ ] Finalize RBAC access for all user properties
- [ ] Automated tests
- [x] Admin page
- [x] Add role/claims support
- [x] Use canonical usernames instead of preferred usernames
- [x] Add user self-service

## Limitations

Logging in with an SSO account that has the same username as an existing Jellyfin account will override the permissions for the user. Use caution when overriding the administrator account!

By default, administrator status is managed strictly from SSO admin roles when `enableAuthorization` is enabled. If an existing administrator does not match an admin role during login, the plugin can revoke the administrator flag. Set `preserveAdminPermissions` to `true` to prevent SSO logins from demoting existing administrators. Other managed permissions, such as folder access and Live TV access, are still updated on every login when `enableAuthorization` is enabled and are persisted through Jellyfin's user policy path.

There is also no logout callback. Logging out of Jellyfin will log you out of Jellyfin only, instead of the SSO provider as well.

# Contributing

## Dependencies

This project uses Nix flakes to manage development dependencies. Run `nix develop` to use the same toolchain versions.

## Building

This is built with .NET 10.0. Build with `dotnet publish .` for the debug release in the `SSO-Auth` directory. Copy over the `IdentityModel.OidcClient.dll`, the `IdentityModel.dll` and the `SSO-Auth.dll` files in the `/bin/Debug/net10.0/publish` directory to a new folder in your Jellyfin configuration: `config/plugins/sso`.

### VSCode Workflow

An example `.vscode` configuration may be found at [strazto/jellyfin-plugin-sso-vscode](https://github.com/strazto/jellyfin-plugin-sso-vscode).

From the root of this repo, you may clone that to `.vscode`

```bash
# From repo root

git clone https://github.com/strazto/jellyfin-plugin-sso-vscode .vscode
```

## Releasing

This plugin uses [JPRM](https://github.com/oddstr13/jellyfin-plugin-repository-manager) to build the plugin. Refer to the documentation there to install JPRM.

Build the zipped plugin with `jprm --verbosity=debug plugin build .`.

### CI Releases

Anything merged to the main branch will be built and published by our CI system.

Anything tagged/released as a formal Github release will also be built and published by our CI system.

If you wish to use releases from your own fork, refer to
[Installing](#installing), however, you will need to change the url to the
manifest file, `https://raw.githubusercontent.com/k0lin/jellyfin-plugin-sso/manifest-release/manifest.json`
so that it refers to your fork.

## Credits and Thanks

A huge thank you to [tradicije](https://github.com/tradicije) for designing the branding plugin.

Much thanks to the [Jellyfin LDAP plugin](https://github.com/jellyfin/jellyfin-plugin-ldapauth) for offering a base for me to start on my plugin.

I use the [AspNet SAML](https://github.com/jitbit/AspNetSaml/) library for the SAML side of things (patched to work with Base64 on non-Windows machines).

I use the [Duende IdentityModel OIDC Client](https://github.com/DuendeSoftware/foss) library for the OpenID side of things.

Thanks to these projects, without which I would have been pulling my hair out implementing these protocols from scratch.

## Something funny about the origins of this plugin

It totally slipped my mind, but I had [requested this functionality a few years back](https://github.com/jellyfin/jellyfin/issues/2012). What goes around comes around, I guess.
