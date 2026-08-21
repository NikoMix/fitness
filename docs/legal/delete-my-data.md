---
title: Delete my data
slug: delete-my-data
order: 4
description: How to delete all Forge fitness app data, including how to erase it without installing the app. Forge has no account and stores nothing on a server.
summary: Forge has no account to delete. All data is on your device, and you can erase it in the app, by uninstalling, or by request.
---

## There is no Forge account to delete

Forge has no account system, no sign-up and no Forge-operated server. Nothing about you is stored
off your device, so there is no remote account or remote profile that could be deleted. Deleting
your Forge data means erasing it from your own device.

This page is published publicly so you can read it, and complete a deletion, without installing or
opening the app.

## Option 1: delete from inside the app

This is the most thorough route, and it does not require contacting anyone.

1. Open Forge.
2. Go to **Settings**.
3. Choose **Delete my data**.
4. Read the summary of what will be erased, then confirm.

This erases the encrypted local database, the database encryption key held in secure storage,
cached media, app preferences, saved purchase entitlement state and any temporary export files.

If you want to keep a copy first, use **Export** or **Backup** before deleting. Once deletion
completes it cannot be undone, because no copy exists anywhere else.

## Option 2: uninstall the app

Uninstalling Forge removes the app and its private storage, which is where all Forge data lives.
Use this if you cannot or do not want to open the app.

**Android:** press and hold the Forge icon, choose **Uninstall**, and confirm. You can also go to
**Settings**, then **Apps**, then **Forge**, then **Uninstall**. To clear the data without removing
the app, choose **Storage**, then **Clear storage**.

**iOS and iPadOS:** press and hold the Forge icon, choose **Remove App**, then **Delete App**, and
confirm.

On both platforms this removes Forge's app-private data, which includes the encrypted database and
cached media.

## Option 3: ask us to confirm deletion

Because Forge holds no copy of your data, there is nothing on our side to delete for you, and no
request is needed to make deletion happen. If you would still like written confirmation, or you
need help completing the steps above, email
TODO(owner: deletion request email address, which may be the same as the support address) with the
subject "Data deletion".

Include your device platform and the app version if you can. Do not include health data in the
email. Expect a reply within TODO(owner: realistic response window, for example five working days).

## What gets deleted

| Data | Where it lives | Deleted by in-app delete | Deleted by uninstall |
| --- | --- | --- | --- |
| Workouts, sets, exercise history | Encrypted local database | Yes | Yes |
| Nutrition, hydration, body metrics | Encrypted local database | Yes | Yes |
| Goals, preferences, units, notification settings | Local database and platform preferences | Yes | Yes |
| Health platform values Forge imported | Encrypted local database | Yes | Yes |
| Database encryption key | Platform secure storage | Yes | Yes |
| Cached exercise media | App cache directory | Yes | Yes |
| Temporary export files | App cache directory | Yes | Yes |
| Local purchase entitlement record | Platform secure storage | Yes | Yes |

## What is not deleted, and why

- **Backups and exports you created yourself.** Files you saved to your device, a cloud drive, a
  chat or an email are outside Forge's control. Delete them wherever you put them.
- **Data in Apple Health or Health Connect.** Forge reads from and, with permission, writes to your
  platform health store. Deleting Forge data does not remove records held by Apple or Google.
  Remove those in the Apple Health app or the Health Connect settings.
- **Your purchase history.** Purchases are recorded by Apple or Google, not by Forge. Deleting
  Forge data does not cancel a purchase or a subscription, and does not entitle you to a refund.
  Manage those in your App Store or Google Play account. If you reinstall Forge, you can restore
  purchases through the store.
- **Support emails you sent.** If you emailed support, that correspondence is held by the
  publisher. Ask for it to be deleted and it will be.

## Retention

Forge retains your data on your device for as long as the app is installed and you keep it. There
is no server-side retention period, because there is no server. Once you delete in-app or
uninstall, the data is gone immediately and is not recoverable, archived or backed up by Forge.

Support correspondence, if you send any, is kept only as long as needed to answer you.

## Related

See the [privacy policy](../privacy/) for the full detail, or the
[data safety summary](../data-safety/) for the short version. For help, see [support](../support/).
