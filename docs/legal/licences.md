---
title: Third-party licences
slug: licences
order: 8
inApp: Licences
description: Open-source and commercial components used by the Forge fitness app, and their licence terms.
summary: The software Forge is built on, and the licences that cover it.
---

## About this page

Forge is built on third-party components, each covered by its own licence. Those licences continue
to apply to those components and are not replaced by Forge's own terms.

The authoritative list of packages and exact versions is the dependency manifest in the Forge
repository. This page summarises the significant components.

## DevExpress .NET MAUI

Forge uses DevExpress MAUI controls for mobile user interface surfaces, under the DevExpress
commercial licence terms applicable to the package version in use.

## CommunityToolkit

Forge uses CommunityToolkit.Mvvm and CommunityToolkit.Maui components under their open-source
licence terms, published by the .NET Foundation and contributors.

## Entity Framework Core

Forge uses Microsoft Entity Framework Core for local persistence access, under Microsoft's
open-source licence terms.

## SQLite and SQLCipher

Forge stores local data in SQLite. SQLite is in the public domain. Encryption is supplied by the
SQLCipher bundle configured in the persistence layer, under its own licence terms.

## .NET and .NET MAUI

Forge is built on the .NET platform and .NET MAUI, published by Microsoft and the .NET Foundation
under the MIT licence.

## Attribution

TODO(owner: confirm the final component list and paste any licence notices that must be reproduced verbatim, particularly for DevExpress and the SQLCipher bundle).
