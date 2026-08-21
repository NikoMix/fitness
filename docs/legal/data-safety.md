---
title: Data safety summary
slug: data-safety
order: 3
description: A plain-language summary of what the Forge fitness app collects, shares and stores. Short version, no legal jargon.
summary: The short, plain-language version of the privacy policy.
---

## The short version

Forge stores your fitness data on your phone, in an encrypted database. There is no Forge server,
no account and no cloud copy. Nothing about your training, food or body is sent to us, because
there is nowhere for it to be sent.

This page is a plain-language summary. The [privacy policy](../privacy/) is the full document and
takes precedence if the two ever differ.

## Does Forge collect my data?

No. "Collect" in app-store terms means data leaving your device and reaching the developer. That
does not happen. Forge stores data on your device, which is a different thing, and the app-store
data safety forms are filled in accordingly.

## Does Forge share my data?

No. Forge does not share your data with anyone, does not sell it and does not transfer it to
advertisers, data brokers or analytics companies.

## Is my data encrypted?

Yes, at rest on your device. The database is encrypted with SQLCipher and the key is stored in the
Android Keystore or the iOS Keychain.

There is no encryption "in transit" for your fitness data, because your fitness data is never in
transit.

## Does Forge use my health data for ads?

No. Forge has no advertising at all, and no advertising SDK.

## Does Forge track me?

No. There are no third-party analytics, no crash reporting that phones home, no advertising
identifier and no cross-app or cross-site tracking.

## Does Forge use the internet?

Only for things the platform handles, never for your fitness data:

- buying and restoring purchases, handled by the App Store or Google Play;
- downloading optional exercise video packs, which Apple and Google host and serve;
- app updates.

Those requests reveal your IP address to Apple or Google, exactly as any app download does. They
carry no workout, nutrition or health information.

## Can I get my data out?

Yes. Forge exports open formats you can read without Forge: a full JSON archive, or a ZIP of CSV
files, one per table. You can limit an export by date range and by data group.

## Can I delete my data?

Yes, and you do not need to ask. Use **Delete my data** in Settings, or uninstall the app. See
[delete my data](../delete-my-data/) for details, including how to do it without installing Forge.

## What happens if I lose my phone?

Your Forge data is gone. This is the honest cost of having no server: nobody can read your data,
and equally nobody can restore it. Export a backup regularly and keep it somewhere you trust.

## What Forge stores, at a glance

| Category | Stored on device | Sent to Forge | Shared with third parties | Used for ads |
| --- | --- | --- | --- | --- |
| Workouts, sets, exercise history | Yes, encrypted | No | No | No |
| Nutrition and hydration logs | Yes, encrypted | No | No | No |
| Body metrics such as weight | Yes, encrypted | No | No | No |
| Health platform imports you allow | Yes, encrypted | No | No | No |
| Goals and app preferences | Yes | No | No | No |
| Purchase entitlement | Yes, in secure storage | No | No | No |
| Name, email, address, phone | Not collected | No | No | No |
| Precise or approximate location | Not collected | No | No | No |
| Contacts, messages, photos, browsing history | Not collected | No | No | No |
| Advertising or tracking identifiers | Not collected | No | No | No |
