# BLUU DELI kiosk field profile

This profile records only what is visible in the user-provided kiosk photograph and what is documented in official UP Solution manuals. It contains no credentials, cookies, or customer data.

## Observed kiosk screen

- Brand: BLUU DELI
- Layout: category rail on the left, three-column product grid, order totals and payment/navigation actions at the bottom.
- Categories, in visible order: COFFEE, BREAKFAST, SANDWICHES, PASTRY, SALAD, SOUP, FOOD TO GO.
- Bottom actions: Home, Credit, Others.
- Product cards marked with a green `M` appear to have additional modifier/options behavior. This is an observation, not a verified product rule.

### Visible COFFEE products

| Product | Displayed price |
| --- | ---: |
| DRIP COFFEE | $0.00 |
| AMERICANO | $0.00 |
| CAFE MOCHA | $0.00 |
| CAPPUCCINO | $0.00 |
| ESPRESSO | $3.00 |
| LATTE | $0.00 |
| CHAI LATTE | $0.00 |
| MATCHA LATTE | $0.00 |
| MACCHIATO | $4.75 |
| HOT CHOCOLATE | $0.00 |
| TEA | $0.00 |

`$0.00` is preserved as the observed display value. Do not assume it means free; it may indicate an unset base price or modifier-driven pricing.

## Verified back-office workflows

Official UPR KIOSK manual v2.3 documents these paths and actions:

1. Create a menu item: open Master Management > Menu Management, select New, then enter category, item name, price, and relevant kitchen/note options.
2. Register a menu image: open Master Management > POS Screen Configuration > Menu Screen Configuration, select the target menu's Edit icon, then register the default/list image and optionally the large/detail image.
3. Register kiosk main/order images: open Master Management > POS Screen Configuration > Kiosk Image Settings and use Upload.

### Documented image constraints

- Main image: 768×1024 for 15-inch kiosks; 1080×1920 for 21.5/27/32-inch kiosks; maximum 5 MB.
- Order-screen image: 768×232 for 15-inch kiosks; 1080×330 for 21.5/27/32-inch kiosks; maximum 3 MB; up to four images.
- Default/list menu image: 184×110 for 15-inch kiosks; 208×124 for 21.5/27/32-inch kiosks; maximum 200 KB.
- Topping image: 76×75 for 15-inch kiosks; larger-screen layouts use 200×90 for two/four groups or 190×160 for multi-group layouts; maximum 200 KB.

## Confidence boundary

The login page and its controls were directly crawled from `uprwbq.upsolutioncloud.com`. Authenticated page DOM, exact English labels, and store-specific save/publish behavior have not yet been observed. ShowWhere should match visible controls at runtime and ask for confirmation instead of inventing a control when the screen differs from this profile.

## Sources

- Store back office: https://uprwbq.upsolutioncloud.com/BasicSetting/Management_Kiosk_HD/100842
- Official UP Solution manual index: https://usms.upsolution.co.kr/Manual.aspx
- Official UPR KIOSK user manual v2.3: https://usms.upsolution.co.kr/Files/Notice/20211223/3EAB0B2562FF4EB788B33EF186CEFA1F_210119%20KIOSK_%EB%A9%94%EB%89%B4%EC%96%BC_V2.3.pdf
