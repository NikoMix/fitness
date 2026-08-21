---
title: Privacy policy
slug: privacy
order: 2
inApp: PrivacyPolicy
effective: 2026-08-21
description: How the Forge fitness app handles your data. Forge stores workouts, nutrition and health data locally in an encrypted database and operates no backend.
summary: Forge stores your data on your device in an encrypted database. There is no Forge server that receives it.
---

## Local-first privacy

Forge is a fitness app for Android and iOS, published by
TODO(owner: registered legal entity name and, if applicable, company registration number).
Version 1 has no Forge-operated backend, account server or cloud database. Your workouts, body
metrics, nutrition logs, goals, preferences and health-platform imports are stored on your device.

Forge does not ask you to create an account and cannot identify you. There is no Forge server that
receives your data, so there is no Forge copy of it to disclose, sell or lose.

## Health data stays on your device

Health and fitness data can reveal sensitive information about your body and your daily routine.
Under the UK and EU General Data Protection Regulation it is special-category data and deserves
stricter handling than ordinary app data.

Forge does not sell health data, does not use health data for advertising, does not use it for
tracking or cross-app profiling, and does not send it to Forge servers. Health data is not stored
in iCloud or any other Forge-operated cloud service.

If you export a backup, produce a diagnostic file or move a file with your operating system's
share sheet, you control where that file goes. Once you share or store an exported file outside
Forge, that destination's privacy and security rules apply instead of this policy.

## What Forge stores on your device

Forge may store the following categories locally:

- workout plans, exercise history, sets, reps, loads and notes;
- nutrition, hydration and body metric entries;
- goals, preferences, units, notification settings and app-lock state;
- Health Connect or HealthKit values you explicitly allow Forge to read;
- data you import yourself from another fitness app's export file;
- a local record of purchases, so paid features stay unlocked without an account;
- cached media and temporary export files needed for app features.

Forge does not collect contacts, precise location, browsing history, advertising identifiers or
device identifiers used for tracking.

## Storage and encryption

Forge stores app data in an encrypted local SQLite database using SQLCipher. The database
encryption key is held in platform secure storage, which is backed by the Android Keystore and the
iOS Keychain. Some non-sensitive settings, such as your preferred units, are stored through the
platform preferences system, which is not encrypted by Forge.

Device-level protection still matters. If someone can unlock your device, they can generally open
your apps. Use a device passcode and, if you want a second barrier, turn on Forge's optional app
lock.

## Network connections

Forge is designed to work with the network permanently unavailable, and every feature that handles
your health data works offline. Forge still needs a network connection for a small number of
things that are handled by the platform rather than by Forge:

- purchases, where the App Store or Google Play processes payment and tells the device what you own;
- optional exercise video packs, which are hosted and served by Apple On-Demand Resources and Google Play Asset Delivery rather than by Forge;
- app updates, which the store handles in the usual way.

These requests go to Apple and Google, not to Forge. Like any network request, they reveal your IP
address and device information to the store you downloaded Forge from, under that store's own
privacy policy. No health, workout or nutrition data is included in them.

## Permissions

Forge asks for a permission only when a feature needs it, and explains why at the moment it asks.
Depending on the features you use, Forge may request access to Health Connect or HealthKit,
notifications, and files for import and export. You can revoke any of these in Android or iOS
settings at any time.

Refusing a permission does not lock you out. Every health and fitness feature remains usable with
manual entry when a health platform is unavailable, not configured, or denied.

## Health platform integration

On iOS, Forge can read step count and body mass from HealthKit and can save completed workouts
back to HealthKit, but only for the data types you explicitly authorise. On Android, Health
Connect integration is not enabled in this version, and health data is entered manually.

Health platform data read by Forge is stored in the same encrypted local database as everything
else and is used only for the feature you enabled it for. Forge does not write health values to
logs, and does not share health platform data with any third party.

## Advertising, analytics and tracking

Forge contains no advertising, no advertising SDK and no third-party analytics or crash-reporting
service that reports to a server. Forge does not track you across apps or websites operated by
other companies, and does not build a profile of you.

Diagnostic information stays on your device and is shared only when you deliberately choose to
share it.

## Purchases

If you buy a paid Forge feature, Apple or Google processes the payment. Forge never sees your card
details, billing address or store account. Forge stores only a local entitlement record on your
device, so paid features stay unlocked without a Forge account server. Your purchase history is
held by the store, and you manage refunds and subscriptions there.

## Backups and deletion

Because Forge has no cloud backup, your device is the only system of record. If you uninstall
Forge, reset or lose your device, or delete your data without an exported backup, Forge cannot
recover it. Nobody at Forge has a copy.

The in-app **Delete my data** flow works without contacting support and erases the encrypted local
database, the encryption key in secure storage, cached media, preferences and temporary export
files. Deletion is irreversible unless you exported and kept your own backup first. You can also
erase everything by uninstalling the app. Full instructions, including how to erase Forge data
without installing the app, are published on the Delete my data page.

## Your rights

Data protection law gives you rights over your personal data, including access, correction,
erasure, restriction, objection and portability.

Because Forge holds no copy of your data, you exercise these rights directly on your device rather
than by asking anyone's permission. Access and portability are provided by the in-app export, which
produces open JSON and CSV files. Correction is editing an entry. Erasure is the **Delete my data**
flow, or uninstalling.

If you email support, the message and address you send become personal data held by
TODO(owner: registered legal entity name), processed to answer you and kept no longer than needed.
You can ask for that correspondence to be deleted.

The supervisory authority for complaints is TODO(owner: relevant data protection authority for the publisher's jurisdiction).

## Children

Forge is intended for general fitness use by adults and is not directed at children. Forge does not
knowingly collect data from children. Because Forge has no account system and no server, any data
in the app is held on the device by whoever uses it, and a parent or guardian controls it through
normal device controls.

## Changes to this policy

This policy may be updated with future app releases. The published version at
TODO(owner: final public policy URL, for example https://nikomix.github.io/fitness/privacy/) is
always the current one, and always matches the copy shown inside the app. Material changes will be
noted with a new effective date.

## Contact

For privacy questions or requests, email
TODO(owner: privacy contact email address, for example privacy@yourdomain.example).

The data controller is TODO(owner: registered legal entity name and registered postal address).
