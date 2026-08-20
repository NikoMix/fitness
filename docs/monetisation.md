# Monetisation

Forge v1 should sell a one-off **Forge Pro** unlock, not lead with a subscription.

Forge is local-first: there is no account server, sync service or Forge-operated content feed to fund. That makes a recurring subscription hard to justify honestly. A one-off unlock is easier for users to understand and better matches the product cost structure.

## Recommended model

- **Free core app:** basic workout logging, the exercise library, basic nutrition logging, Health Connect/HealthKit import where available, and local backup/export remain free.
- **Forge Pro one-off unlock:** advanced training analytics, deeper personal-record views, custom plan templates and extra personalisation.
- **Optional subscription only if future content exists:** a future content subscription can fund continuously released template packs or coached programmes, but it should not be required for the core local training loop.

## Why not subscription-first?

A purely local fitness app has weak recurring-value justification. Users notice when a subscription funds neither cloud sync nor fresh content. Subscription-first would improve recurring revenue, but it risks worse reviews, lower conversion trust and store scrutiny if the value proposition feels manufactured.

## Store compliance

Prices must be loaded from Apple or Google and shown localized. Forge must not hard-code price strings or currency symbols. Restore purchases is a visible route because Apple guideline 3.1.1 requires a functional restore path for in-app purchases.

If a subscription product exists, Forge provides a visible manage/cancel subscription link to the platform account subscription page.

## Local entitlement storage limits

Forge stores entitlements on the device using platform secure storage plus a local signature to make casual edits harder. Without a server, this is not strong DRM: a determined attacker controlling the device can eventually tamper with local state or patch the app.

For a low-price consumer fitness app, that is a reasonable commercial trade-off. It preserves the privacy and simplicity of a no-backend product. Forge should state this honestly in code and product planning, rather than pretending local storage is server-grade proof of purchase.
