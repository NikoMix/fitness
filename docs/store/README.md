# Forge store readiness runbook

## Commercial model

Use one non-consumable in-app purchase for v1:

| Store product ID | Type | Purpose |
| --- | --- | --- |
| `forge.pro.lifetime` | Non-consumable / one-time purchase | Unlocks Forge Pro planning, analysis and personalisation features. |

Do not create subscriptions for v1. A one-off unlock fits a local-first app, avoids recurring-billing disclosure risk, and keeps the free workout history/data-management loop available without pressure.

## App Store Connect setup

1. Create the non-consumable product `forge.pro.lifetime`.
2. Localise the display name as "Forge Pro" and describe it as a one-time unlock.
3. Attach a review screenshot showing the Forge Pro shop page, local price, and Restore purchases button.
4. Confirm the binary exposes:
   - in-app privacy policy and terms, offline;
   - Restore purchases;
   - Delete my data;
   - medical disclaimer before relying on fitness guidance.

## Play Console setup

1. Create an in-app product with ID `forge.pro.lifetime`.
2. Use a managed product, not a subscription.
3. Add regional prices and tax category as required.
4. Complete Data safety accurately: health/fitness data is processed on device, not sold, and not used for ads.
5. Complete the Play Health Apps declaration. This is the critical path: Google review can take 4–8 weeks and requires a publicly hosted privacy policy URL that matches the in-app policy.

## Required metadata

- Public privacy policy URL matching `docs/legal/privacy-policy.md`.
- Support/contact URL or email for store review.
- Screenshots showing local-first health data handling, Pro purchase, Restore purchases and Delete my data.
- Plain-language medical disclaimer in listing text if using training/nutrition recommendations.
- Age rating: target general fitness users; do not mark as child-directed. Answer health/medical questions conservatively because Forge stores health and fitness information.

## Rejection risks to test before submission

- Missing or hidden Restore purchases button (Apple Guideline 3.1.1).
- Hard-coded or misleading IAP prices instead of store-localised prices.
- Granting Pro on cancellation, pending family approval, billing errors or store unavailability.
- No functional Delete my data flow, or deletion that only opens support contact.
- In-app privacy policy differing from the hosted privacy policy.
- Health claims that imply diagnosis, treatment or guaranteed outcomes.
- Play Health Apps declaration not started early enough for the 4–8 week lead time.

## Local entitlement trust model

Forge validates purchases with Apple/Google through `Plugin.InAppBilling`, then stores entitlements on-device. The local store is signed to make casual editing tamper-evident, but a determined user with device control can still patch the app or storage. Without a backend, this is an accepted privacy/product trade-off, not server-grade DRM.
