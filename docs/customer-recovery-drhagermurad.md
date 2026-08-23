# Customer Recovery: Hager Murad (`drhagermurad2026@gmail.com`)

## Transaction Overview
* **Customer:** Hager Murad (`drhagermurad2026@gmail.com`)
* **Product:** Reading Starter (Order amount: GBP 4.00 base + 20% UK VAT = GBP 4.80)
* **Payment Gateway:** Whop (Embedded Checkout)
* **Date & Time:** August 23, 2026, ~15:25 - 15:34 (BST)
* **Status:** Paid on Whop, unconfirmed on website due to webhook signature / routing failure.

---

## Recovery Options

### Option A: Via Admin Dashboard (Recommended - No Code Required)
1. Log in to the Admin Dashboard at `https://withdrhesham.co.uk/admin` (or `https://app.oetwithdrhesham.co.uk/admin`).
2. Navigate to **Learners / Users** and search for `drhagermurad2026@gmail.com`.
3. Open Hager Murad's user profile.
4. Under **Entitlements / Courses / Subscriptions**, click **Grant Course / Product Entitlement**.
5. Select **Reading Starter** and click **Grant Access**.
6. The user will immediately see the Reading Starter course in their learner dashboard.

---

### Option B: Via Admin API / PowerShell
Run the following authorized API request to grant the entitlement directly:
```bash
curl -X POST "https://api.oetwithdrhesham.co.uk/v1/admin/users/by-email/drhagermurad2026@gmail.com/entitlements" \
  -H "Authorization: Bearer <ADMIN_JWT_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "productId": "reading-starter",
    "reason": "Whop payment manual recovery for order on 2026-08-23",
    "grantedBy": "Admin"
  }'
```

---

### Option C: Whop Webhook Replay
Once this fix is deployed and the Whop Webhook Endpoint is set in the Whop Dashboard:
1. Go to your **Whop Dashboard -> Settings -> Webhooks**.
2. Find the failed/recent webhook event for payment of £4.80 by Hager Murad.
3. Click **Resend / Replay Webhook**.
4. The system will automatically receive the webhook, verify the signature, mark the quote as completed, and grant the course automatically.
